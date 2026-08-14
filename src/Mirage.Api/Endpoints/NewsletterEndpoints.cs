using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Api.Services;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Email;
using Mirage.Infrastructure.Identity;
using Mirage.Infrastructure.Persistence;

namespace Mirage.Api.Endpoints;

internal static partial class NewsletterEndpoints
{
    public static RouteGroupBuilder MapNewsletterEndpoints(this RouteGroupBuilder api)
    {
        var newsletters = api.MapGroup("/newsletters").WithTags("Newsletters").RequireAuthorization();
        newsletters.MapGet("/", ListPublished);
        newsletters.MapGet("/{id:guid}", GetPublished);
        newsletters.MapPost("/{id:guid}/like", ToggleLike);
        newsletters.MapPost("/{id:guid}/comments", AddComment);
        newsletters.MapPost("/comments/{commentId:guid}/like", ToggleCommentLike);
        newsletters.MapGet("/subscription", GetSubscription);
        newsletters.MapPut("/subscription", SetSubscription);

        // One-click unsubscribe from an email link: signed token instead of a session, so a reader never has to
        // sign in to stop hearing from us.
        var publicNewsletters = api.MapGroup("/newsletters").WithTags("Newsletters").AllowAnonymous();
        publicNewsletters.MapPost("/unsubscribe", UnsubscribeByToken);

        var manage = api.MapGroup("/newsletter-management").WithTags("Newsletter Management")
            .RequireAuthorization(MiragePolicy.NewsletterManagement);
        manage.MapGet("/", ListManaged);
        manage.MapPost("/", Create);
        manage.MapGet("/audience", Audience);
        manage.MapGet("/dashboard", Dashboard);
        manage.MapGet("/{id:guid}", GetManaged);
        manage.MapGet("/{id:guid}/preview", Preview);
        manage.MapPut("/{id:guid}", Update);
        manage.MapDelete("/{id:guid}", Delete);
        manage.MapPost("/{id:guid}/schedule", Schedule);
        manage.MapPost("/{id:guid}/cancel", Cancel);
        manage.MapPost("/{id:guid}/test-send", TestSend);
        manage.MapPost("/{id:guid}/submit", SubmitForReview);
        manage.MapPost("/{id:guid}/withdraw", WithdrawFromReview);
        manage.MapGet("/{id:guid}/reviews", ListReviews);
        manage.MapPost("/{id:guid}/reviews", AddReview);

        var admin = api.MapGroup("/admin/platform-managers").WithTags("Platform Managers")
            .RequireAuthorization(MiragePolicy.PlatformAdmin);
        admin.MapPost("/invite", InviteManager);
        admin.MapGet("/invites", ListInvites);
        admin.MapGet("/search", SearchMembers);
        admin.MapPost("/grant", GrantManager);
        admin.MapGet("/", ListManagers);
        admin.MapDelete("/{userId:guid}", RevokeManager);
        newsletters.MapPost("/platform-manager-invites/accept", AcceptManagerInvite);
        return api;
    }

    private static async Task<IResult> ListPublished(HttpContext context, MirageDbContext db, int page = 1,
        int pageSize = 12, CancellationToken ct = default)
    {
        var userId = context.User.GetUserId();
        var query = db.Newsletters.AsNoTracking().Where(x => x.Status == NewsletterStatus.Sent)
            .OrderByDescending(x => x.SentAt).Select(x => new { x.Id, x.Title, x.Excerpt, x.ImageUrls, x.ThumbnailUrl, x.SentAt,
                AuthorName = db.Profiles.Where(p => p.UserId == x.AuthorUserId).Select(p => p.DisplayName).FirstOrDefault(),
                AuthorAvatarUrl = db.Profiles.Where(p => p.UserId == x.AuthorUserId).Select(p => p.AvatarUrl).FirstOrDefault(),
                LikeCount = db.NewsletterLikes.Count(l => l.NewsletterId == x.Id),
                CommentCount = db.NewsletterComments.Count(c => c.NewsletterId == x.Id),
                IsLiked = db.NewsletterLikes.Any(l => l.NewsletterId == x.Id && l.UserId == userId) });
        return ApiResults.Ok(context, await query.ToPagedResultAsync(page, Math.Clamp(pageSize, 1, 50), ct), "Newsletters retrieved.");
    }

    private static async Task<IResult> GetPublished(Guid id, HttpContext context, MirageDbContext db, CancellationToken ct)
    {
        var userId = context.User.GetUserId();
        var item = await db.Newsletters.AsNoTracking().Where(x => x.Id == id && x.Status == NewsletterStatus.Sent)
            .Select(x => new { x.Id, x.Title, x.Excerpt, x.ContentHtml, x.ImageUrls, x.ThumbnailUrl, x.SentAt,
                AuthorName = db.Profiles.Where(p => p.UserId == x.AuthorUserId).Select(p => p.DisplayName).FirstOrDefault(),
                AuthorAvatarUrl = db.Profiles.Where(p => p.UserId == x.AuthorUserId).Select(p => p.AvatarUrl).FirstOrDefault(),
                LikeCount = db.NewsletterLikes.Count(l => l.NewsletterId == x.Id),
                IsLiked = db.NewsletterLikes.Any(l => l.NewsletterId == x.Id && l.UserId == userId) }).SingleOrDefaultAsync(ct);
        if (item is null) return EndpointHelpers.NotFound(context, "Newsletter was not found.");
        var comments = await db.NewsletterComments.AsNoTracking().Where(x => x.NewsletterId == id)
            .OrderBy(x => x.CreatedAt).Select(x => new { x.Id, x.ParentCommentId, x.Body, x.CreatedAt, x.UserId,
                AuthorName = db.Profiles.Where(p => p.UserId == x.UserId).Select(p => p.DisplayName).FirstOrDefault() ?? "Member",
                AuthorAvatarUrl = db.Profiles.Where(p => p.UserId == x.UserId).Select(p => p.AvatarUrl).FirstOrDefault(),
                LikeCount = db.NewsletterCommentLikes.Count(l => l.CommentId == x.Id),
                IsLiked = db.NewsletterCommentLikes.Any(l => l.CommentId == x.Id && l.UserId == userId) }).ToListAsync(ct);
        return ApiResults.Ok(context, new { Newsletter = item, Comments = comments }, "Newsletter retrieved.");
    }

    private static async Task<IResult> ToggleLike(Guid id, HttpContext context, MirageDbContext db, CancellationToken ct)
    {
        if (!await db.Newsletters.AnyAsync(x => x.Id == id && x.Status == NewsletterStatus.Sent, ct)) return EndpointHelpers.NotFound(context, "Newsletter was not found.");
        var userId = context.User.GetUserId(); var like = await db.NewsletterLikes.SingleOrDefaultAsync(x => x.NewsletterId == id && x.UserId == userId, ct);
        var liked = like is null; if (liked) db.NewsletterLikes.Add(new NewsletterLike(id, userId)); else db.NewsletterLikes.Remove(like!);
        await db.SaveChangesAsync(ct); return ApiResults.Ok(context, new { IsLiked = liked, LikeCount = await db.NewsletterLikes.CountAsync(x => x.NewsletterId == id, ct) }, liked ? "Newsletter liked." : "Like removed.");
    }

    private static async Task<IResult> AddComment(Guid id, NewsletterCommentRequest request, HttpContext context, MirageDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Trim().Length > 2000) return EndpointHelpers.ValidationProblem(context, ("body", "Comment must be between 1 and 2,000 characters."));
        if (!await db.Newsletters.AnyAsync(x => x.Id == id && x.Status == NewsletterStatus.Sent, ct)) return EndpointHelpers.NotFound(context, "Newsletter was not found.");
        if (request.ParentCommentId.HasValue && !await db.NewsletterComments.AnyAsync(x => x.Id == request.ParentCommentId && x.NewsletterId == id, ct)) return EndpointHelpers.ValidationProblem(context, ("parentCommentId", "Parent comment is invalid."));
        var comment = new NewsletterComment(id, context.User.GetUserId(), request.Body, request.ParentCommentId); db.NewsletterComments.Add(comment); await db.SaveChangesAsync(ct);
        return ApiResults.Created(context, $"/api/v1/newsletters/{id}", new { comment.Id }, "Comment added.");
    }

    private static async Task<IResult> ToggleCommentLike(Guid commentId, HttpContext context, MirageDbContext db, CancellationToken ct)
    {
        if (!await db.NewsletterComments.AnyAsync(x => x.Id == commentId, ct)) return EndpointHelpers.NotFound(context, "Comment was not found.");
        var userId = context.User.GetUserId(); var like = await db.NewsletterCommentLikes.SingleOrDefaultAsync(x => x.CommentId == commentId && x.UserId == userId, ct);
        var liked = like is null; if (liked) db.NewsletterCommentLikes.Add(new NewsletterCommentLike(commentId, userId)); else db.NewsletterCommentLikes.Remove(like!);
        await db.SaveChangesAsync(ct); return ApiResults.Ok(context, new { IsLiked = liked, LikeCount = await db.NewsletterCommentLikes.CountAsync(x => x.CommentId == commentId, ct) }, "Comment reaction updated.");
    }

    private static async Task<IResult> GetSubscription(HttpContext context, MirageDbContext db, CancellationToken ct) =>
        ApiResults.Ok(context, new { IsSubscribed = await db.Users.Where(x => x.Id == context.User.GetUserId()).Select(x => x.IsNewsletterSubscribed).SingleAsync(ct) }, "Newsletter preference retrieved.");

    private static async Task<IResult> SetSubscription(NewsletterSubscriptionRequest request, HttpContext context, MirageDbContext db, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(x => x.Id == context.User.GetUserId(), ct); user.IsNewsletterSubscribed = request.IsSubscribed; user.NewsletterSubscribedAt = request.IsSubscribed ? DateTimeOffset.UtcNow : null; await db.SaveChangesAsync(ct);
        return ApiResults.Ok(context, new { request.IsSubscribed }, request.IsSubscribed ? "Subscribed to newsletters." : "Unsubscribed from newsletters.");
    }

    private static async Task<IResult> UnsubscribeByToken(UnsubscribeNewsletterRequest request, HttpContext context,
        MirageDbContext db, IConfiguration configuration, CancellationToken ct)
    {
        if (!NewsletterUnsubscribe.TryReadUserId(request.Token, configuration, out var userId))
            return EndpointHelpers.ValidationProblem(context, ("token", "This unsubscribe link is invalid or incomplete."));
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null) return EndpointHelpers.NotFound(context, "Account was not found.");
        user.IsNewsletterSubscribed = false; user.NewsletterSubscribedAt = null; await db.SaveChangesAsync(ct);
        return ApiResults.Ok(context, new { IsSubscribed = false, user.Email }, "You have been unsubscribed from Mirage newsletters.");
    }

    private static async Task<IResult> Audience(HttpContext context, MirageDbContext db, Sex? sex = null,
        string? relationshipStatuses = null, CancellationToken ct = default)
    {
        var statuses = NewsletterAudience.ParseStatuses(relationshipStatuses);
        return ApiResults.Ok(context, new
        {
            RecipientCount = await NewsletterAudience.Filtered(db, sex, statuses).CountAsync(ct),
            TotalSubscribers = await db.Users.CountAsync(NewsletterAudience.IsSubscriber, ct),
            Sex = sex,
            RelationshipStatuses = statuses
        }, "Audience size retrieved.");
    }

    private static async Task<IResult> GetManaged(Guid id, HttpContext context, MirageDbContext db, CancellationToken ct)
    {
        var callerId = context.User.GetUserId();
        var item = await db.Newsletters.AsNoTracking()
            .Where(x => x.Id == id && (x.Status != NewsletterStatus.Draft || x.AuthorUserId == callerId))
            .Select(x => new { x.Id, x.Title, x.Subject,
            x.Excerpt, x.ContentHtml, x.ImageUrls, x.ThumbnailUrl, x.Status, x.ScheduledFor, x.SentAt, x.RecipientCount, x.DeliveredCount,
            x.FailedCount, x.FailureReason, x.CreatedAt, x.AudienceSex, x.AudienceRelationshipStatuses, x.ReviewRound,
            ApprovalCount = db.NewsletterReviews.Where(r => r.NewsletterId == x.Id && r.Round == x.ReviewRound && r.Decision == NewsletterReviewDecision.Approved).Select(r => r.ReviewerUserId).Distinct().Count(),
            AuthorName = db.Profiles.Where(p => p.UserId == x.AuthorUserId).Select(p => p.DisplayName).FirstOrDefault(),
            AuthorAvatarUrl = db.Profiles.Where(p => p.UserId == x.AuthorUserId).Select(p => p.AvatarUrl).FirstOrDefault(),
            IsMine = x.AuthorUserId == callerId })
            .SingleOrDefaultAsync(ct);
        if (item is null) return EndpointHelpers.NotFound(context, "Newsletter was not found.");
        var deliveries = await db.NewsletterDeliveries.AsNoTracking().Where(x => x.NewsletterId == id)
            .GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        var failures = await db.NewsletterDeliveries.AsNoTracking()
            .Where(x => x.NewsletterId == id && x.Status == NewsletterDeliveryStatus.Failed)
            .OrderByDescending(x => x.UpdatedAt).Take(20).Select(x => new { x.Email, x.Error }).ToListAsync(ct);
        return ApiResults.Ok(context, new
        {
            Newsletter = item,
            Report = new
            {
                Pending = deliveries.FirstOrDefault(x => x.Status == NewsletterDeliveryStatus.Pending)?.Count ?? 0,
                Delivered = deliveries.FirstOrDefault(x => x.Status == NewsletterDeliveryStatus.Sent)?.Count ?? 0,
                Failed = deliveries.FirstOrDefault(x => x.Status == NewsletterDeliveryStatus.Failed)?.Count ?? 0,
                RecentFailures = failures
            },
            Engagement = new
            {
                Likes = await db.NewsletterLikes.CountAsync(x => x.NewsletterId == id, ct),
                Comments = await db.NewsletterComments.CountAsync(x => x.NewsletterId == id, ct)
            }
        }, "Newsletter retrieved.");
    }

    // Renders the exact email a subscriber will receive, so an edition is never scheduled sight-unseen.
    private static async Task<IResult> Preview(Guid id, HttpContext context, MirageDbContext db, IConfiguration configuration, CancellationToken ct)
    {
        var item = await FindVisibleAsync(id, context, db, ct, tracked: false);
        if (item is null) return EndpointHelpers.NotFound(context, "Newsletter was not found.");
        var appUrl = (configuration["Frontend:BaseUrl"] ?? "https://www.themiragehub.com").TrimEnd('/');
        var name = await db.Profiles.AsNoTracking().Where(x => x.UserId == context.User.GetUserId())
            .Select(x => x.DisplayName).FirstOrDefaultAsync(ct) ?? "Friend";
        var html = NewsletterEmailTemplate.Render(name, item.Title, item.Excerpt, item.ContentHtml, item.ImageUrls,
            $"{appUrl}/newsletters/{item.Id}", $"{appUrl}/newsletter-unsubscribe?token=preview",
            NewsletterEmailTemplate.SocialLinks(configuration), item.ThumbnailUrl, NewsletterEmailTemplate.MastheadUrl(configuration));
        return Results.Content(html, "text/html; charset=utf-8");
    }

    // A draft belongs to the person writing it. Once it is scheduled it becomes the team's, and everyone with
    // newsletter management can see, edit, and cancel it.
    private static async Task<IResult> ListManaged(HttpContext context, MirageDbContext db, int page = 1, int pageSize = 30, CancellationToken ct = default)
    {
        var userId = context.User.GetUserId();
        return ApiResults.Ok(context, await db.Newsletters.AsNoTracking()
            .Where(x => x.Status != NewsletterStatus.Draft || x.AuthorUserId == userId)
            .OrderByDescending(x => x.CreatedAt).Select(x => new { x.Id, x.Title, x.Subject, x.Excerpt, x.ContentHtml, x.ImageUrls, x.ThumbnailUrl, x.Status, x.ScheduledFor, x.SentAt, x.RecipientCount, x.DeliveredCount, x.FailedCount, x.FailureReason, x.CreatedAt, x.AudienceSex, x.AudienceRelationshipStatuses, x.ReviewRound,
            ApprovalCount = db.NewsletterReviews.Where(r => r.NewsletterId == x.Id && r.Round == x.ReviewRound && r.Decision == NewsletterReviewDecision.Approved).Select(r => r.ReviewerUserId).Distinct().Count(),
            AuthorName = db.Profiles.Where(p => p.UserId == x.AuthorUserId).Select(p => p.DisplayName).FirstOrDefault(),
            AuthorAvatarUrl = db.Profiles.Where(p => p.UserId == x.AuthorUserId).Select(p => p.AvatarUrl).FirstOrDefault(),
            IsMine = x.AuthorUserId == userId }).ToPagedResultAsync(page, Math.Clamp(pageSize, 1, 100), ct), "Managed newsletters retrieved.");
    }

    /// <summary>Loads an edition the caller is allowed to act on. Another author's unscheduled draft reports as
    /// missing rather than forbidden, so the list of who is drafting what stays private too.</summary>
    private static async Task<Newsletter?> FindVisibleAsync(Guid id, HttpContext context, MirageDbContext db,
        CancellationToken ct, bool tracked = true)
    {
        var query = tracked ? db.Newsletters : db.Newsletters.AsNoTracking();
        var item = await query.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return null;
        return item.Status == NewsletterStatus.Draft && item.AuthorUserId != context.User.GetUserId() ? null : item;
    }

    /// <summary>An edition is its author's work from the first keystroke to the moment it lands in an inbox.
    /// Everyone else with newsletter management can read it, review it, and put an approved one on the calendar —
    /// but the words, the send time, and the decision to pull it back are the author's alone. Without this,
    /// scheduling an edition would quietly hand it to the whole team.</summary>
    private static IResult? RequireAuthor(Newsletter item, HttpContext context, string action) =>
        item.AuthorUserId == context.User.GetUserId()
            ? null
            : EndpointHelpers.Problem(context, 403, "Not the author", $"Only the author of this edition can {action}.");

    private static async Task<IResult> Create(CreateNewsletterRequest request, HttpContext context, MirageDbContext db, CancellationToken ct)
    {
        var error = ValidatePost(request); if (error is not null) return EndpointHelpers.ValidationProblem(context, (error.Value.Field, error.Value.Message));
        var newsletter = new Newsletter(context.User.GetUserId(), request.Title, request.Subject, request.Excerpt, SanitizeHtml(request.ContentHtml), ValidImages(request.ImageUrls), ValidImage(request.ThumbnailUrl)); db.Newsletters.Add(newsletter); await db.SaveChangesAsync(ct);
        return ApiResults.Created(context, $"/api/v1/newsletter-management/{newsletter.Id}", new { newsletter.Id }, "Newsletter draft created.");
    }

    private static async Task<IResult> Update(Guid id, CreateNewsletterRequest request, HttpContext context, MirageDbContext db, CancellationToken ct)
    {
        var error = ValidatePost(request); if (error is not null) return EndpointHelpers.ValidationProblem(context, (error.Value.Field, error.Value.Message));
        var item = await FindVisibleAsync(id, context, db, ct); if (item is null) return EndpointHelpers.NotFound(context, "Newsletter was not found.");
        if (RequireAuthor(item, context, "edit it") is { } denied) return denied;
        try { item.Update(request.Title, request.Subject, request.Excerpt, SanitizeHtml(request.ContentHtml), ValidImages(request.ImageUrls), ValidImage(request.ThumbnailUrl)); } catch (InvalidOperationException e) { return EndpointHelpers.Conflict(context, e.Message); }
        await db.SaveChangesAsync(ct); return ApiResults.Ok(context, new { item.Id }, "Newsletter saved.");
    }

    private static async Task<IResult> Schedule(Guid id, ScheduleNewsletterRequest request, HttpContext context,
        MirageDbContext db, IConfiguration configuration, CancellationToken ct)
    {
        var item = await FindVisibleAsync(id, context, db, ct); if (item is null) return EndpointHelpers.NotFound(context, "Newsletter was not found.");

        // An edition already on the calendar is the author's to move, and nobody else's. Only the clock changes:
        // the text and the audience were approved as a pair, so a reschedule never touches either.
        if (item.Status == NewsletterStatus.Scheduled)
        {
            if (RequireAuthor(item, context, "move its send time") is { } notAuthor) return notAuthor;
            var audience = await NewsletterAudience.Filtered(db, item.AudienceSex, item.AudienceRelationshipStatuses).CountAsync(ct);
            try { item.Reschedule(request.ScheduledFor.ToUniversalTime(), audience); }
            catch (InvalidOperationException e) { return EndpointHelpers.Conflict(context, e.Message); }
            await db.SaveChangesAsync(ct);
            return ApiResults.Ok(context, new { item.Id, item.ScheduledFor, RecipientCount = audience, item.AudienceSex, item.AudienceRelationshipStatuses }, "Send time updated.");
        }

        // Maker-checker: the author never sends their own edition, and it needs two sign-offs on the current text.
        if (item.AuthorUserId == context.User.GetUserId())
            return EndpointHelpers.Problem(context, 403, "Author cannot schedule",
                "An edition has to be scheduled by a reviewer other than its author.");
        var required = RequiredApprovals(configuration);
        var approvals = await ApprovalCountAsync(db, item, ct);
        if (approvals < required)
            return EndpointHelpers.Conflict(context, $"This edition has {approvals} of {required} approvals on its current text.");
        var statuses = NewsletterAudience.ParseStatuses(request.RelationshipStatuses);
        var count = await NewsletterAudience.Filtered(db, request.Sex, statuses).CountAsync(ct);
        if (count == 0) return EndpointHelpers.ValidationProblem(context, ("audience", "No subscriber matches that audience. Widen the filters before scheduling."));
        try
        {
            item.SetAudience(request.Sex, statuses);
            item.Schedule(request.ScheduledFor.ToUniversalTime(), count);
        }
        catch (InvalidOperationException e) { return EndpointHelpers.Conflict(context, e.Message); }
        await db.SaveChangesAsync(ct);
        return ApiResults.Ok(context, new { item.Id, item.ScheduledFor, RecipientCount = count, item.AudienceSex, item.AudienceRelationshipStatuses }, "Newsletter scheduled.");
    }

    // Pulling an edition off the calendar returns it to its author's drafts, unapproved. It is the only way back
    // into editing once scheduled, and only the author may take it.
    private static async Task<IResult> Cancel(Guid id, HttpContext context, MirageDbContext db, CancellationToken ct)
    {
        var item = await FindVisibleAsync(id, context, db, ct); if (item is null) return EndpointHelpers.NotFound(context, "Newsletter was not found.");
        if (RequireAuthor(item, context, "cancel its scheduled send") is { } denied) return denied;
        try { item.Cancel(); } catch (InvalidOperationException e) { return EndpointHelpers.Conflict(context, e.Message); }
        await db.SaveChangesAsync(ct);
        return ApiResults.Ok(context, new { item.Id, item.Status }, "Schedule cancelled. The edition is back in your drafts and will need approval again before it can be scheduled.");
    }

    /// <summary>How many sign-offs an edition needs before it can be scheduled — none of them the author's, and
    /// all given on the current text. One by default: the maker-checker separation is what matters, and a second
    /// reviewer mostly adds delay. Raise <c>Newsletter:RequiredApprovals</c> for a stricter process.</summary>
    private static int RequiredApprovals(IConfiguration configuration) =>
        Math.Clamp(configuration.GetValue("Newsletter:RequiredApprovals", 1), 1, 5);

    private static IQueryable<NewsletterReview> CurrentRoundApprovals(MirageDbContext db, Newsletter item) =>
        db.NewsletterReviews.AsNoTracking().Where(x => x.NewsletterId == item.Id && x.Round == item.ReviewRound
            && x.Decision == NewsletterReviewDecision.Approved);

    private static async Task<int> ApprovalCountAsync(MirageDbContext db, Newsletter item, CancellationToken ct) =>
        await CurrentRoundApprovals(db, item).Select(x => x.ReviewerUserId).Distinct().CountAsync(ct);

    private static async Task<IResult> SubmitForReview(Guid id, HttpContext context, MirageDbContext db,
        IConfiguration configuration, CancellationToken ct)
    {
        var item = await FindVisibleAsync(id, context, db, ct);
        if (item is null) return EndpointHelpers.NotFound(context, "Newsletter was not found.");
        if (RequireAuthor(item, context, "submit it for review") is { } denied) return denied;
        try { item.SubmitForReview(); } catch (InvalidOperationException e) { return EndpointHelpers.Conflict(context, e.Message); }
        await db.SaveChangesAsync(ct);
        var needed = RequiredApprovals(configuration);
        return ApiResults.Ok(context, new { item.Id, item.Status },
            $"Submitted for review. {needed} other reviewer{(needed == 1 ? "" : "s")} must approve it before it can be scheduled.");
    }

    private static async Task<IResult> WithdrawFromReview(Guid id, HttpContext context, MirageDbContext db, CancellationToken ct)
    {
        var item = await FindVisibleAsync(id, context, db, ct);
        if (item is null) return EndpointHelpers.NotFound(context, "Newsletter was not found.");
        if (item.AuthorUserId != context.User.GetUserId())
            return EndpointHelpers.Problem(context, 403, "Not the author", "Only the author can withdraw an edition from review.");
        try { item.WithdrawFromReview(); } catch (InvalidOperationException e) { return EndpointHelpers.Conflict(context, e.Message); }
        await db.SaveChangesAsync(ct);
        return ApiResults.Ok(context, new { item.Id, item.Status }, "Withdrawn from review and back in your drafts.");
    }

    private static async Task<IResult> ListReviews(Guid id, HttpContext context, MirageDbContext db,
        IConfiguration configuration, CancellationToken ct)
    {
        var item = await FindVisibleAsync(id, context, db, ct, tracked: false);
        if (item is null) return EndpointHelpers.NotFound(context, "Newsletter was not found.");
        var thread = await db.NewsletterReviews.AsNoTracking().Where(x => x.NewsletterId == id)
            .OrderBy(x => x.CreatedAt).Select(x => new { x.Id, x.Decision, x.Comment, x.Round, x.CreatedAt, x.ReviewerUserId,
                ReviewerName = db.Profiles.Where(p => p.UserId == x.ReviewerUserId).Select(p => p.DisplayName).FirstOrDefault() ?? "Reviewer",
                ReviewerAvatarUrl = db.Profiles.Where(p => p.UserId == x.ReviewerUserId).Select(p => p.AvatarUrl).FirstOrDefault(),
                IsCurrentRound = x.Round == item.ReviewRound }).ToListAsync(ct);
        return ApiResults.Ok(context, new { Thread = thread, Approval = await ApprovalStateAsync(db, item, context, configuration, ct) }, "Review thread retrieved.");
    }

    private static async Task<object> ApprovalStateAsync(MirageDbContext db, Newsletter item, HttpContext context,
        IConfiguration configuration, CancellationToken ct)
    {
        var required = RequiredApprovals(configuration);
        var me = context.User.GetUserId();
        var approvals = await CurrentRoundApprovals(db, item).Select(x => x.ReviewerUserId).Distinct().ToListAsync(ct);
        var isAuthor = item.AuthorUserId == me;
        return new
        {
            item.ReviewRound,
            Required = required,
            Count = approvals.Count,
            IsAuthor = isAuthor,
            HasMyApproval = approvals.Contains(me),
            // The author is the maker; the checkers are everyone else with newsletter management.
            CanApprove = !isAuthor && item.Status == NewsletterStatus.InReview && !approvals.Contains(me),
            CanSchedule = !isAuthor && approvals.Count >= required
                && item.Status is NewsletterStatus.Approved or NewsletterStatus.Cancelled,
            // Everything below is the author's alone. A scheduled edition is frozen: the time can move and the
            // send can be called off, but the words cannot change without going back through review.
            CanEdit = isAuthor && item.Status is NewsletterStatus.Draft or NewsletterStatus.InReview
                or NewsletterStatus.Approved or NewsletterStatus.Cancelled or NewsletterStatus.Failed,
            CanReschedule = isAuthor && item.Status == NewsletterStatus.Scheduled,
            CanCancel = isAuthor && item.Status == NewsletterStatus.Scheduled,
            IsLocked = item.Status is NewsletterStatus.Scheduled or NewsletterStatus.Sending or NewsletterStatus.Sent
        };
    }

    private static async Task<IResult> AddReview(Guid id, NewsletterReviewRequest request, HttpContext context,
        MirageDbContext db, IConfiguration configuration, CancellationToken ct)
    {
        var item = await FindVisibleAsync(id, context, db, ct);
        if (item is null) return EndpointHelpers.NotFound(context, "Newsletter was not found.");
        var me = context.User.GetUserId();
        var comment = request.Comment?.Trim();
        if (comment is { Length: > 2000 }) return EndpointHelpers.ValidationProblem(context, ("comment", "A review note cannot exceed 2,000 characters."));

        if (request.Decision != NewsletterReviewDecision.Comment)
        {
            if (item.AuthorUserId == me) return EndpointHelpers.Problem(context, 403, "Author cannot review",
                "An edition has to be approved by someone other than its author.");
            if (item.Status is not (NewsletterStatus.InReview or NewsletterStatus.Approved))
                return EndpointHelpers.Conflict(context, "This edition is not currently in review.");
        }
        if (request.Decision == NewsletterReviewDecision.ChangesRequested && string.IsNullOrWhiteSpace(comment))
            return EndpointHelpers.ValidationProblem(context, ("comment", "Say what needs to change."));

        // Recorded before the status moves, so the note is stamped with the round it was actually written in.
        db.NewsletterReviews.Add(new NewsletterReview(id, me, request.Decision, comment, item.ReviewRound));
        await db.SaveChangesAsync(ct);

        var message = "Note added to the review.";
        if (request.Decision == NewsletterReviewDecision.ChangesRequested)
        {
            item.RequestChanges();
            await db.SaveChangesAsync(ct);
            message = "Changes requested. Every approval so far has been cleared and the edition needs approving again.";
        }
        else if (request.Decision == NewsletterReviewDecision.Approved)
        {
            var required = RequiredApprovals(configuration);
            var count = await ApprovalCountAsync(db, item, ct);
            if (count >= required && item.Status == NewsletterStatus.InReview)
            {
                item.MarkApproved();
                await db.SaveChangesAsync(ct);
                message = "Approved. This edition can now be scheduled.";
            }
            else
            {
                message = $"Approved. {Math.Max(0, required - count)} more approval(s) needed.";
            }
        }
        return ApiResults.Ok(context, new { item.Status, Approval = await ApprovalStateAsync(db, item, context, configuration, ct) }, message);
    }

    private static async Task<IResult> Delete(Guid id, HttpContext context, MirageDbContext db, CancellationToken ct)
    {
        var item = await FindVisibleAsync(id, context, db, ct);
        if (item is null) return EndpointHelpers.NotFound(context, "Newsletter was not found.");
        if (RequireAuthor(item, context, "delete it") is { } denied) return denied;
        if (!item.CanBeDeleted) return EndpointHelpers.Conflict(context,
            "A newsletter that has been sent or is sending cannot be deleted. Cancel it first if it is still scheduled.");
        db.NewsletterDeliveries.RemoveRange(db.NewsletterDeliveries.Where(x => x.NewsletterId == id));
        db.Newsletters.Remove(item);
        await db.SaveChangesAsync(ct);
        return ApiResults.Ok(context, new { Id = id }, "Draft deleted.");
    }

    // Sends the real email to a handful of reviewers without touching status, delivery rows, or the audience.
    // A test send is a rehearsal: nothing about it should look like a send in the reports.
    private static async Task<IResult> TestSend(Guid id, TestSendNewsletterRequest request, HttpContext context,
        MirageDbContext db, IEmailService email, IConfiguration configuration, CancellationToken ct)
    {
        var recipients = (request.Emails ?? [])
            .Select(x => x.Trim().ToLowerInvariant()).Where(x => x.Length > 0).Distinct().ToArray();
        if (recipients.Length == 0) return EndpointHelpers.ValidationProblem(context, ("emails", "Add at least one reviewer email address."));
        if (recipients.Length > 5) return EndpointHelpers.ValidationProblem(context, ("emails", "A test send is limited to 5 reviewers."));
        if (recipients.FirstOrDefault(x => !new EmailAddressAttribute().IsValid(x)) is { } invalid)
            return EndpointHelpers.ValidationProblem(context, ("emails", $"'{invalid}' is not a valid email address."));

        var item = await FindVisibleAsync(id, context, db, ct, tracked: false);
        if (item is null) return EndpointHelpers.NotFound(context, "Newsletter was not found.");

        var appUrl = (configuration["Frontend:BaseUrl"] ?? "https://www.themiragehub.com").TrimEnd('/');
        // Reviewers are usually real members, so the rehearsal should greet them by name exactly as the real
        // send does. Matched on NormalizedEmail (uppercase, as Identity stores it) against the lowercased input.
        var normalized = recipients.Select(x => x.ToUpperInvariant()).ToArray();
        var names = await db.Users.AsNoTracking()
            .Where(x => x.NormalizedEmail != null && normalized.Contains(x.NormalizedEmail))
            .Join(db.Profiles.AsNoTracking(), u => u.Id, p => p.UserId,
                (u, p) => new { u.NormalizedEmail, p.DisplayName })
            .ToDictionaryAsync(x => x.NormalizedEmail!, x => x.DisplayName, ct);
        var sent = new List<string>();
        var failed = new List<string>();
        foreach (var recipient in recipients)
        {
            var displayName = names.GetValueOrDefault(recipient.ToUpperInvariant()) is { Length: > 0 } known
                ? known
                : "Friend";
            var delivered = await email.SendNewsletterAsync(recipient, displayName, $"[TEST] {item.Subject}", item.Title,
                item.Excerpt, item.ContentHtml, item.ImageUrls, $"{appUrl}/newsletters/{item.Id}",
                $"{appUrl}/newsletter-unsubscribe?token=test", item.ThumbnailUrl, ct);
            (delivered ? sent : failed).Add(recipient);
        }
        return ApiResults.Ok(context, new { Sent = sent, Failed = failed },
            failed.Count == 0
                ? $"Test edition sent to {sent.Count} reviewer{(sent.Count == 1 ? string.Empty : "s")}."
                : $"Sent to {sent.Count}; {failed.Count} could not be delivered.");
    }

    private static async Task<IResult> Dashboard(HttpContext context, MirageDbContext db, CancellationToken ct)
    {
        var users = await db.Users.CountAsync(x => x.IsActive && !x.IsDeleted, ct); var subscribers = await db.Users.CountAsync(x => x.IsActive && !x.IsDeleted && x.IsNewsletterSubscribed, ct);
        return ApiResults.Ok(context, new { Subscribers = subscribers, Unsubscribed = users - subscribers, SubscriptionRate = users == 0 ? 0 : Math.Round(subscribers * 100m / users, 1), Published = await db.Newsletters.CountAsync(x => x.Status == NewsletterStatus.Sent, ct), Scheduled = await db.Newsletters.CountAsync(x => x.Status == NewsletterStatus.Scheduled, ct), TotalLikes = await db.NewsletterLikes.CountAsync(ct), TotalComments = await db.NewsletterComments.CountAsync(ct), Delivered = await db.NewsletterDeliveries.CountAsync(x => x.Status == NewsletterDeliveryStatus.Sent, ct), Failed = await db.NewsletterDeliveries.CountAsync(x => x.Status == NewsletterDeliveryStatus.Failed, ct) }, "Newsletter dashboard retrieved.");
    }

    private static async Task<IResult> InviteManager(InvitePlatformManagerRequest request, HttpContext context, MirageDbContext db, IEmailService email, IConfiguration config, CancellationToken ct)
    {
        var normalized = request.Email.Trim().ToLowerInvariant(); if (!new EmailAddressAttribute().IsValid(normalized)) return EndpointHelpers.ValidationProblem(context, ("email", "A valid email address is required."));
        if (await db.PlatformManagerInvites.AnyAsync(x => x.Email == normalized && x.AcceptedAt == null && x.ExpiresAt > DateTimeOffset.UtcNow, ct)) return EndpointHelpers.Conflict(context, "An active invitation already exists.");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); var hash = Hash(token); var expires = DateTimeOffset.UtcNow.AddDays(7);
        db.PlatformManagerInvites.Add(new PlatformManagerInvite(normalized, hash, context.User.GetUserId(), expires)); await db.SaveChangesAsync(ct);
        var baseUrl = (config["Frontend:BaseUrl"] ?? "https://www.themiragehub.com").TrimEnd('/'); var sent = await email.SendPlatformManagerInviteAsync(normalized, $"{baseUrl}/platform-manager-invite?token={token}", ct);
        if (!sent)
        {
            var failedInvite = await db.PlatformManagerInvites.SingleAsync(x => x.TokenHash == hash, ct);
            db.PlatformManagerInvites.Remove(failedInvite); await db.SaveChangesAsync(ct);
            return EndpointHelpers.Problem(context, StatusCodes.Status502BadGateway, "Invitation delivery failed", "No invitation was retained. Please retry when the email provider is available.");
        }
        return ApiResults.Ok(context, new { Email = normalized, ExpiresAt = expires, EmailSent = true }, "Invitation sent.");
    }

    /// <summary>Finds existing members by the name they are known by on Mirage, or by email. An invitation is only
    /// needed for someone who is not on the platform yet — anyone already here can simply be granted the role.</summary>
    private static async Task<IResult> SearchMembers(HttpContext context, MirageDbContext db, string? q,
        CancellationToken ct)
    {
        var term = (q ?? string.Empty).Trim();
        if (term.Length < 2) return ApiResults.Ok(context, Array.Empty<object>(), "Type at least two characters.");
        var managerRoleIds = await db.Roles.Where(r => r.Name == MirageRoles.PlatformManager || r.Name == MirageRoles.PlatformAdmin)
            .Select(r => r.Id).ToListAsync(ct);
        var results = await db.Users.AsNoTracking().Where(u => u.IsActive && !u.IsDeleted)
            .Where(u => EF.Functions.ILike(u.Email!, $"%{term}%")
                || db.Profiles.Any(p => p.UserId == u.Id && EF.Functions.ILike(p.DisplayName, $"%{term}%")))
            .OrderBy(u => u.Email)
            .Take(10)
            .Select(u => new
            {
                u.Id,
                u.Email,
                DisplayName = db.Profiles.Where(p => p.UserId == u.Id).Select(p => p.DisplayName).FirstOrDefault(),
                AvatarUrl = db.Profiles.Where(p => p.UserId == u.Id).Select(p => p.AvatarUrl).FirstOrDefault(),
                AlreadyManages = db.UserRoles.Any(ur => ur.UserId == u.Id && managerRoleIds.Contains(ur.RoleId))
            })
            .ToListAsync(ct);
        return ApiResults.Ok(context, results, "Members found.");
    }

    /// <summary>Grants the role straight away to someone already on Mirage. No token, no email round trip — they
    /// pick it up on their next sign-in, because roles are carried in the access token.</summary>
    private static async Task<IResult> GrantManager(GrantPlatformManagerRequest request, HttpContext context,
        MirageDbContext db, UserManager<ApplicationUser> users, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(request.UserId.ToString());
        if (user is null || user.IsDeleted) return EndpointHelpers.NotFound(context, "That member was not found.");
        if (await users.IsInRoleAsync(user, MirageRoles.PlatformManager))
            return EndpointHelpers.Conflict(context, $"{user.Email} is already a Platform Manager.");
        var result = await users.AddToRoleAsync(user, MirageRoles.PlatformManager);
        if (!result.Succeeded) return EndpointHelpers.Conflict(context, "Could not assign the Platform Manager role.");
        var name = await db.Profiles.AsNoTracking().Where(x => x.UserId == user.Id).Select(x => x.DisplayName)
            .FirstOrDefaultAsync(ct) ?? user.Email!;
        return ApiResults.Ok(context, new { user.Id, user.Email, DisplayName = name },
            $"{name} is now a Platform Manager. They will see the studio after signing in again.");
    }

    private static async Task<IResult> ListManagers(HttpContext context, MirageDbContext db, CancellationToken ct)
    {
        var roleId = await db.Roles.Where(r => r.Name == MirageRoles.PlatformManager).Select(r => r.Id)
            .FirstOrDefaultAsync(ct);
        var managers = await db.UserRoles.AsNoTracking().Where(ur => ur.RoleId == roleId)
            .Join(db.Users.AsNoTracking().Where(u => !u.IsDeleted), ur => ur.UserId, u => u.Id, (ur, u) => u)
            .Select(u => new
            {
                u.Id,
                u.Email,
                DisplayName = db.Profiles.Where(p => p.UserId == u.Id).Select(p => p.DisplayName).FirstOrDefault(),
                AvatarUrl = db.Profiles.Where(p => p.UserId == u.Id).Select(p => p.AvatarUrl).FirstOrDefault()
            }).ToListAsync(ct);
        return ApiResults.Ok(context, managers, "Platform managers retrieved.");
    }

    private static async Task<IResult> RevokeManager(Guid userId, HttpContext context,
        UserManager<ApplicationUser> users, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return EndpointHelpers.NotFound(context, "That member was not found.");
        var result = await users.RemoveFromRoleAsync(user, MirageRoles.PlatformManager);
        if (!result.Succeeded) return EndpointHelpers.Conflict(context, "Could not remove the Platform Manager role.");
        return ApiResults.Ok(context, new { user.Id }, "Platform Manager access removed.");
    }

    private static async Task<IResult> ListInvites(HttpContext context, MirageDbContext db, CancellationToken ct) => ApiResults.Ok(context,
        await db.PlatformManagerInvites.AsNoTracking().OrderByDescending(x => x.CreatedAt).Select(x => new { x.Id, x.Email, x.CreatedAt, x.ExpiresAt, x.AcceptedAt }).ToListAsync(ct), "Platform manager invitations retrieved.");

    private static async Task<IResult> AcceptManagerInvite(AcceptPlatformManagerInviteRequest request, HttpContext context, MirageDbContext db, UserManager<ApplicationUser> users, CancellationToken ct)
    {
        var invite = await db.PlatformManagerInvites.SingleOrDefaultAsync(x => x.TokenHash == Hash(request.Token), ct); if (invite is null || invite.IsAccepted || invite.ExpiresAt <= DateTimeOffset.UtcNow) return EndpointHelpers.ValidationProblem(context, ("token", "Invitation is invalid or expired."));
        var user = await users.FindByIdAsync(context.User.GetUserId().ToString()); if (user is null || !string.Equals(user.Email, invite.Email, StringComparison.OrdinalIgnoreCase)) return EndpointHelpers.Problem(context, 403, "Invitation email mismatch", "Sign in with the email address that received this invitation.");
        if (!await users.IsInRoleAsync(user, MirageRoles.PlatformManager)) { var result = await users.AddToRoleAsync(user, MirageRoles.PlatformManager); if (!result.Succeeded) return EndpointHelpers.Conflict(context, "Could not assign the Platform Manager role."); }
        invite.Accept(); await db.SaveChangesAsync(ct); return ApiResults.Ok(context, new { Role = MirageRoles.PlatformManager, RefreshRequired = true }, "Platform Manager invitation accepted. Sign in again to refresh permissions.");
    }

    private static (string Field, string Message)? ValidatePost(CreateNewsletterRequest r)
    { if (string.IsNullOrWhiteSpace(r.Title) || r.Title.Length > 200) return ("title", "Title must be between 1 and 200 characters."); if (string.IsNullOrWhiteSpace(r.Subject) || r.Subject.Length > 250) return ("subject", "Subject must be between 1 and 250 characters."); if (string.IsNullOrWhiteSpace(r.Excerpt) || r.Excerpt.Length > 500) return ("excerpt", "Excerpt must be between 1 and 500 characters."); if (string.IsNullOrWhiteSpace(r.ContentHtml) || r.ContentHtml.Length > 100_000) return ("contentHtml", "Content must be between 1 and 100,000 characters."); return null; }
    private static string? ValidImage(string? image) => ValidImages(image is null ? null : [image]).FirstOrDefault();
    private static string[] ValidImages(string[]? images) => (images ?? []).Where(x => Uri.TryCreate(x, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps).Distinct().Take(10).ToArray();
    private static string SanitizeHtml(string html) { html = DangerousElementRegex().Replace(html, string.Empty); html = EventHandlerRegex().Replace(html, string.Empty); return JavascriptUrlRegex().Replace(html, "$1=\"#\""); }
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    [GeneratedRegex(@"<(script|iframe|object|embed|form|style)\b[^>]*>.*?</\1\s*>|<(script|iframe|object|embed|form|style)\b[^>]*/?>", RegexOptions.IgnoreCase | RegexOptions.Singleline)] private static partial Regex DangerousElementRegex();
    [GeneratedRegex("""\s+on[a-z]+\s*=\s*(?:"[^"]*"|'[^']*'|[^\s>]+)""", RegexOptions.IgnoreCase)] private static partial Regex EventHandlerRegex();
    [GeneratedRegex("""(href|src)\s*=\s*["']?\s*javascript:[^\s>"']*["']?""", RegexOptions.IgnoreCase)] private static partial Regex JavascriptUrlRegex();
}
