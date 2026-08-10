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
        manage.MapPost("/{id:guid}/schedule", Schedule);
        manage.MapPost("/{id:guid}/cancel", Cancel);

        var admin = api.MapGroup("/admin/platform-managers").WithTags("Platform Managers")
            .RequireAuthorization(MiragePolicy.PlatformAdmin);
        admin.MapPost("/invite", InviteManager);
        admin.MapGet("/invites", ListInvites);
        newsletters.MapPost("/platform-manager-invites/accept", AcceptManagerInvite);
        return api;
    }

    private static async Task<IResult> ListPublished(HttpContext context, MirageDbContext db, int page = 1,
        int pageSize = 12, CancellationToken ct = default)
    {
        var userId = context.User.GetUserId();
        var query = db.Newsletters.AsNoTracking().Where(x => x.Status == NewsletterStatus.Sent)
            .OrderByDescending(x => x.SentAt).Select(x => new { x.Id, x.Title, x.Excerpt, x.ImageUrls, x.SentAt,
                LikeCount = db.NewsletterLikes.Count(l => l.NewsletterId == x.Id),
                CommentCount = db.NewsletterComments.Count(c => c.NewsletterId == x.Id),
                IsLiked = db.NewsletterLikes.Any(l => l.NewsletterId == x.Id && l.UserId == userId) });
        return ApiResults.Ok(context, await query.ToPagedResultAsync(page, Math.Clamp(pageSize, 1, 50), ct), "Newsletters retrieved.");
    }

    private static async Task<IResult> GetPublished(Guid id, HttpContext context, MirageDbContext db, CancellationToken ct)
    {
        var userId = context.User.GetUserId();
        var item = await db.Newsletters.AsNoTracking().Where(x => x.Id == id && x.Status == NewsletterStatus.Sent)
            .Select(x => new { x.Id, x.Title, x.Excerpt, x.ContentHtml, x.ImageUrls, x.SentAt,
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

    private static async Task<IResult> Audience(HttpContext context, MirageDbContext db, CancellationToken ct) =>
        ApiResults.Ok(context, new { RecipientCount = await db.Users.CountAsync(NewsletterAudience.IsSubscriber, ct) },
            "Audience size retrieved.");

    private static async Task<IResult> GetManaged(Guid id, HttpContext context, MirageDbContext db, CancellationToken ct)
    {
        var item = await db.Newsletters.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.Id, x.Title, x.Subject,
            x.Excerpt, x.ContentHtml, x.ImageUrls, x.Status, x.ScheduledFor, x.SentAt, x.RecipientCount, x.DeliveredCount,
            x.FailedCount, x.FailureReason, x.CreatedAt }).SingleOrDefaultAsync(ct);
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
        var item = await db.Newsletters.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return EndpointHelpers.NotFound(context, "Newsletter was not found.");
        var appUrl = (configuration["Frontend:BaseUrl"] ?? "https://www.themiragehub.com").TrimEnd('/');
        var name = await db.Profiles.AsNoTracking().Where(x => x.UserId == context.User.GetUserId())
            .Select(x => x.DisplayName).FirstOrDefaultAsync(ct) ?? "Friend";
        var html = NewsletterEmailTemplate.Render(name, item.Title, item.Excerpt, item.ContentHtml, item.ImageUrls,
            $"{appUrl}/newsletters/{item.Id}", $"{appUrl}/newsletter-unsubscribe?token=preview");
        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static async Task<IResult> ListManaged(HttpContext context, MirageDbContext db, int page = 1, int pageSize = 30, CancellationToken ct = default) =>
        ApiResults.Ok(context, await db.Newsletters.AsNoTracking().OrderByDescending(x => x.CreatedAt).Select(x => new { x.Id, x.Title, x.Subject, x.Excerpt, x.ContentHtml, x.ImageUrls, x.Status, x.ScheduledFor, x.SentAt, x.RecipientCount, x.DeliveredCount, x.FailedCount, x.FailureReason, x.CreatedAt }).ToPagedResultAsync(page, Math.Clamp(pageSize, 1, 100), ct), "Managed newsletters retrieved.");

    private static async Task<IResult> Create(CreateNewsletterRequest request, HttpContext context, MirageDbContext db, CancellationToken ct)
    {
        var error = ValidatePost(request); if (error is not null) return EndpointHelpers.ValidationProblem(context, (error.Value.Field, error.Value.Message));
        var newsletter = new Newsletter(context.User.GetUserId(), request.Title, request.Subject, request.Excerpt, SanitizeHtml(request.ContentHtml), ValidImages(request.ImageUrls)); db.Newsletters.Add(newsletter); await db.SaveChangesAsync(ct);
        return ApiResults.Created(context, $"/api/v1/newsletter-management/{newsletter.Id}", new { newsletter.Id }, "Newsletter draft created.");
    }

    private static async Task<IResult> Update(Guid id, CreateNewsletterRequest request, HttpContext context, MirageDbContext db, CancellationToken ct)
    {
        var error = ValidatePost(request); if (error is not null) return EndpointHelpers.ValidationProblem(context, (error.Value.Field, error.Value.Message));
        var item = await db.Newsletters.SingleOrDefaultAsync(x => x.Id == id, ct); if (item is null) return EndpointHelpers.NotFound(context, "Newsletter was not found.");
        try { item.Update(request.Title, request.Subject, request.Excerpt, SanitizeHtml(request.ContentHtml), ValidImages(request.ImageUrls)); } catch (InvalidOperationException e) { return EndpointHelpers.Conflict(context, e.Message); }
        await db.SaveChangesAsync(ct); return ApiResults.Ok(context, new { item.Id }, "Newsletter saved.");
    }

    private static async Task<IResult> Schedule(Guid id, ScheduleNewsletterRequest request, HttpContext context, MirageDbContext db, CancellationToken ct)
    {
        var item = await db.Newsletters.SingleOrDefaultAsync(x => x.Id == id, ct); if (item is null) return EndpointHelpers.NotFound(context, "Newsletter was not found.");
        var count = await db.Users.CountAsync(NewsletterAudience.IsSubscriber, ct);
        try { item.Schedule(request.ScheduledFor.ToUniversalTime(), count); } catch (InvalidOperationException e) { return EndpointHelpers.Conflict(context, e.Message); }
        await db.SaveChangesAsync(ct); return ApiResults.Ok(context, new { item.Id, item.ScheduledFor, RecipientCount = count }, "Newsletter scheduled.");
    }

    private static async Task<IResult> Cancel(Guid id, HttpContext context, MirageDbContext db, CancellationToken ct)
    {
        var item = await db.Newsletters.SingleOrDefaultAsync(x => x.Id == id, ct); if (item is null) return EndpointHelpers.NotFound(context, "Newsletter was not found.");
        try { item.Cancel(); } catch (InvalidOperationException e) { return EndpointHelpers.Conflict(context, e.Message); } await db.SaveChangesAsync(ct); return ApiResults.Ok(context, new { item.Id }, "Newsletter cancelled.");
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
    private static string[] ValidImages(string[]? images) => (images ?? []).Where(x => Uri.TryCreate(x, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps).Distinct().Take(10).ToArray();
    private static string SanitizeHtml(string html) { html = DangerousElementRegex().Replace(html, string.Empty); html = EventHandlerRegex().Replace(html, string.Empty); return JavascriptUrlRegex().Replace(html, "$1=\"#\""); }
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    [GeneratedRegex(@"<(script|iframe|object|embed|form|style)\b[^>]*>.*?</\1\s*>|<(script|iframe|object|embed|form|style)\b[^>]*/?>", RegexOptions.IgnoreCase | RegexOptions.Singleline)] private static partial Regex DangerousElementRegex();
    [GeneratedRegex("""\s+on[a-z]+\s*=\s*(?:"[^"]*"|'[^']*'|[^\s>]+)""", RegexOptions.IgnoreCase)] private static partial Regex EventHandlerRegex();
    [GeneratedRegex("""(href|src)\s*=\s*["']?\s*javascript:[^\s>"']*["']?""", RegexOptions.IgnoreCase)] private static partial Regex JavascriptUrlRegex();
}
