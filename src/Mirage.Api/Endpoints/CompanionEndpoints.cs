using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Api.Services;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Persistence;

namespace Mirage.Api.Endpoints;

internal static class CompanionEndpoints
{
    public static RouteGroupBuilder MapCompanionEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/companion").WithTags("Companion").RequireAuthorization();
        group.MapGet("/today", GetToday);
        group.MapPut("/subscription", SetSubscription);
        group.MapPost("/entries", CreateEntry);
        group.MapGet("/entries/mine", GetMyEntries);
        group.MapGet("/entries/partner", GetPartnerEntries);
        group.MapGet("/partner", GetPartners);
        group.MapPost("/partner/invite", InvitePartner);
        group.MapPatch("/partner/{id:guid}/approve", ApprovePartner);
        group.MapPatch("/partner/{id:guid}/decline", DeclinePartner);
        return group;
    }

    private static async Task<CompanionPrompt?> PickRandomPromptAsync(MirageDbContext db, CompanionCadence cadence,
        CancellationToken cancellationToken)
    {
        var matching = await db.CompanionPrompts.AsNoTracking()
            .Where(p => p.IsActive && p.Cadence == cadence).ToListAsync(cancellationToken);
        if (matching.Count == 0)
            matching = await db.CompanionPrompts.AsNoTracking().Where(p => p.IsActive).ToListAsync(cancellationToken);
        return matching.Count == 0 ? null : matching[Random.Shared.Next(matching.Count)];
    }

    private static async Task<Guid?> GetApprovedPartnerUserIdAsync(Guid userId, MirageDbContext db,
        CancellationToken cancellationToken) =>
        await db.CompanionPartners.AsNoTracking()
            .Where(p => p.Status == CompanionPartnerStatus.Approved && (p.User1Id == userId || p.User2Id == userId))
            .Select(p => p.User1Id == userId ? p.User2Id : p.User1Id)
            .FirstOrDefaultAsync(cancellationToken);

    private static async Task<IResult> GetToday(HttpContext context, MirageDbContext db, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var subscription = await db.CompanionSubscriptions.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (subscription is null)
        {
            subscription = new CompanionSubscription(userId);
            db.CompanionSubscriptions.Add(subscription);
        }

        if (subscription.CurrentPromptId is null)
        {
            var picked = await PickRandomPromptAsync(db, subscription.Cadence, cancellationToken);
            if (picked is null) return EndpointHelpers.NotFound(context, "No Companion prompts are available yet.");
            subscription.AssignPrompt(picked.Id);
        }
        await db.SaveChangesAsync(cancellationToken);

        var prompt = await db.CompanionPrompts.AsNoTracking()
            .SingleAsync(p => p.Id == subscription.CurrentPromptId!.Value, cancellationToken);
        var answeredToday = await db.CompanionEntries.AsNoTracking()
            .AnyAsync(e => e.AuthorUserId == userId && e.PromptId == subscription.CurrentPromptId, cancellationToken);

        var response = new CompanionTodayResponse(
            new CompanionPromptResponse(prompt.Id, prompt.Text, prompt.Category, prompt.Cadence),
            subscription.Cadence, subscription.NextDueAt, answeredToday);
        return ApiResults.Ok(context, response, "Today's Companion prompt retrieved successfully.");
    }

    private static async Task<IResult> SetSubscription(SetCompanionCadenceRequest request, HttpContext context,
        MirageDbContext db, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var subscription = await db.CompanionSubscriptions.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (subscription is null)
        {
            subscription = new CompanionSubscription(userId, request.Cadence);
            db.CompanionSubscriptions.Add(subscription);
        }
        else
        {
            subscription.SetCadence(request.Cadence);
        }
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { subscription.Cadence, subscription.NextDueAt }, "Companion cadence updated successfully.");
    }

    private static async Task<IResult> CreateEntry(CreateCompanionEntryRequest request, HttpContext context,
        MirageDbContext db, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AnswerText))
            return EndpointHelpers.ValidationProblem(context, ("answerText", "An answer is required."));

        var prompt = await db.CompanionPrompts.AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.PromptId, cancellationToken);
        if (prompt is null) return EndpointHelpers.NotFound(context, "Companion prompt was not found.");

        var userId = context.User.GetUserId();
        var entry = new CompanionEntry(request.PromptId, userId, request.AnswerText);
        db.CompanionEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);

        var displayName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
        var response = new CompanionEntryResponse(entry.Id, entry.PromptId, prompt.Text, userId,
            displayName ?? "You", entry.AnswerText, entry.CreatedAt);
        return ApiResults.Created(context, $"/api/v1/companion/entries/{entry.Id}", response, "Journal entry saved successfully.");
    }

    private static async Task<List<CompanionEntryResponse>> LoadEntriesAsync(MirageDbContext db, Guid authorUserId,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var entries = await db.CompanionEntries.AsNoTracking()
            .Where(e => e.AuthorUserId == authorUserId)
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        if (entries.Count == 0) return [];

        var promptIds = entries.Select(e => e.PromptId).Distinct().ToArray();
        var promptTexts = await db.CompanionPrompts.AsNoTracking()
            .Where(p => promptIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Text, cancellationToken);
        var authorName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == authorUserId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);

        return entries.Select(e => new CompanionEntryResponse(e.Id, e.PromptId,
            promptTexts.GetValueOrDefault(e.PromptId, "Companion prompt"), authorUserId,
            authorName ?? "Unknown", e.AnswerText, e.CreatedAt)).ToList();
    }

    private static async Task<IResult> GetMyEntries(HttpContext context, MirageDbContext db,
        int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var userId = context.User.GetUserId();
        var entries = await LoadEntriesAsync(db, userId, page, pageSize, cancellationToken);
        return ApiResults.Ok(context, entries, "Journal entries retrieved successfully.");
    }

    private static async Task<IResult> GetPartnerEntries(HttpContext context, MirageDbContext db,
        int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var userId = context.User.GetUserId();
        var partnerUserId = await GetApprovedPartnerUserIdAsync(userId, db, cancellationToken);
        if (partnerUserId is null) return ApiResults.Ok(context, new List<CompanionEntryResponse>(), "No linked Companion partner yet.");

        var entries = await LoadEntriesAsync(db, partnerUserId.Value, page, pageSize, cancellationToken);
        return ApiResults.Ok(context, entries, "Partner's journal entries retrieved successfully.");
    }

    private static async Task<IResult> GetPartners(HttpContext context, MirageDbContext db, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var partners = await db.CompanionPartners.AsNoTracking()
            .Where(x => x.User1Id == userId || x.User2Id == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { Partner = x, OtherUserId = x.User1Id == userId ? x.User2Id : x.User1Id })
            .ToListAsync(cancellationToken);

        var otherIds = partners.Select(x => x.OtherUserId).Distinct().ToList();
        var names = await db.Profiles.AsNoTracking()
            .Where(p => otherIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, p => p.DisplayName, cancellationToken);

        var response = partners.Select(x => new CompanionPartnerResponse(
            x.Partner.Id, x.OtherUserId, names.GetValueOrDefault(x.OtherUserId, "Unknown"),
            x.Partner.RequestedByUserId, x.Partner.Status, x.Partner.CreatedAt)).ToList();
        return ApiResults.Ok(context, response, "Companion partner records retrieved successfully.");
    }

    private static async Task<IResult> InvitePartner(InviteCompanionPartnerRequest request, HttpContext context,
        MirageDbContext db, NotificationService notifications, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        Guid partnerUserId;

        if (request.PartnerUserId is { } explicitPartnerId)
        {
            // Already-synced couples can invite by user id directly, skipping the email
            // re-entry — their partner's identity is already known from the Couple link.
            if (!await db.Profiles.AsNoTracking().AnyAsync(p => p.UserId == explicitPartnerId, cancellationToken))
                return EndpointHelpers.NotFound(context, "No account found for that partner.");
            partnerUserId = explicitPartnerId;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.PartnerEmail))
                return EndpointHelpers.ValidationProblem(context, ("partnerEmail", "Partner email is required."));

            var normalizedEmail = request.PartnerEmail.Trim().ToLowerInvariant();
            var partner = await db.Profiles.AsNoTracking()
                .Join(db.Users.AsNoTracking(), p => p.UserId, u => u.Id, (p, u) => new { p.UserId, u.Email })
                .SingleOrDefaultAsync(x => x.Email != null && x.Email.ToLower() == normalizedEmail, cancellationToken);
            if (partner is null) return EndpointHelpers.NotFound(context, "No account found with that email address.");
            partnerUserId = partner.UserId;
        }

        if (partnerUserId == userId)
            return EndpointHelpers.ValidationProblem(context, ("partnerEmail", "You cannot link yourself as your own Companion partner."));

        var user1Id = userId.CompareTo(partnerUserId) < 0 ? userId : partnerUserId;
        var user2Id = userId.CompareTo(partnerUserId) < 0 ? partnerUserId : userId;
        if (await db.CompanionPartners.AnyAsync(x => x.User1Id == user1Id && x.User2Id == user2Id
                && x.Status != CompanionPartnerStatus.Declined, cancellationToken))
            return EndpointHelpers.Conflict(context, "A Companion partner invitation already exists between you and this person.");

        var link = new CompanionPartner(userId, partnerUserId);
        db.CompanionPartners.Add(link);
        await db.SaveChangesAsync(cancellationToken);

        var requesterName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
        await notifications.NotifyAsync(partnerUserId, NotificationType.CompanionPartnerInvite, "Companion partner request",
            $"{requesterName} wants to share Companion journal answers with you.", link.Id, "CompanionPartner", cancellationToken);

        return ApiResults.Created(context, $"/api/v1/companion/partner/{link.Id}", new { link.Id, link.Status },
            "Companion partner invitation sent successfully.");
    }

    private static async Task<IResult> ApprovePartner(Guid id, HttpContext context, MirageDbContext db,
        NotificationService notifications, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var link = await db.CompanionPartners.SingleOrDefaultAsync(
            x => x.Id == id && (x.User1Id == userId || x.User2Id == userId), cancellationToken);
        if (link is null) return EndpointHelpers.NotFound(context, "Companion partner invitation was not found.");

        try { link.Approve(userId); }
        catch (InvalidOperationException ex) { return EndpointHelpers.Conflict(context, ex.Message); }
        await db.SaveChangesAsync(cancellationToken);

        await notifications.NotifyAsync(link.RequestedByUserId, NotificationType.CompanionPartnerApproved, "Companion partner approved",
            "Your Companion partner request was approved. You can now see each other's journal answers.",
            link.Id, "CompanionPartner", cancellationToken);

        return ApiResults.Ok(context, new { link.Id, link.Status }, "Companion partner link approved successfully.");
    }

    private static async Task<IResult> DeclinePartner(Guid id, HttpContext context, MirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var link = await db.CompanionPartners.SingleOrDefaultAsync(
            x => x.Id == id && (x.User1Id == userId || x.User2Id == userId), cancellationToken);
        if (link is null) return EndpointHelpers.NotFound(context, "Companion partner invitation was not found.");
        try { link.Decline(); }
        catch (InvalidOperationException ex) { return EndpointHelpers.Conflict(context, ex.Message); }
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { link.Id, link.Status }, "Companion partner invitation declined.");
    }
}
