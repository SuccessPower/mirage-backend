using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;

namespace Mirage.Api.Endpoints;

internal static class TestimonialEndpoints
{
    public static RouteGroupBuilder MapTestimonialEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/testimonials").WithTags("Testimonials").RequireAuthorization();
        group.MapGet("/", List);
        group.MapGet("/taggable-profiles", SearchTaggableProfiles);
        group.MapPost("/", Create);
        group.MapGet("/{id:guid}", Get);
        group.MapGet("/{id:guid}/share", GetShareInfo).AllowAnonymous();
        group.MapPut("/{id:guid}", Update);
        group.MapDelete("/{id:guid}", Delete);
        group.MapPost("/{id:guid}/likes", Like);
        group.MapDelete("/{id:guid}/likes", Unlike);
        group.MapGet("/{id:guid}/comments", ListComments);
        group.MapPost("/{id:guid}/comments", Comment);
        group.MapPost("/{id:guid}/comments/{commentId:guid}/likes", LikeComment);
        group.MapDelete("/{id:guid}/comments/{commentId:guid}/likes", UnlikeComment);
        return api;
    }

    private static IQueryable<TestimonialResponse> Project(IQueryable<Testimonial> query, IMirageDbContext db, Guid me) =>
        query.Select(x => new TestimonialResponse(
            x.Id, x.AuthorUserId,
            db.Profiles.Where(p => p.UserId == x.AuthorUserId).Select(p => p.DisplayName).FirstOrDefault() ?? "Mirage member",
            db.Profiles.Where(p => p.UserId == x.AuthorUserId).Select(p => p.AvatarUrl).FirstOrDefault(),
            x.TaggedUserId,
            x.TaggedUserId == null ? null : db.Profiles.Where(p => p.UserId == x.TaggedUserId).Select(p => p.DisplayName).FirstOrDefault(),
            x.TaggedUserId == null ? null : db.Profiles.Where(p => p.UserId == x.TaggedUserId).Select(p => p.AvatarUrl).FirstOrDefault(),
            x.Title, x.Body, x.ImageUrl, x.ImageUrl2, x.ImageUrl3, x.Reads.Count, x.Likes.Count, x.Comments.Count,
            x.Likes.Any(l => l.UserId == me), x.CreatedAt, x.CelebrationType));

    private static async Task<IResult> List(HttpContext context, IMirageDbContext db, string? search,
        int page = 1, int pageSize = 12, CancellationToken cancellationToken = default)
    {
        var query = db.Testimonials.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(x => EF.Functions.ILike(x.Title, term) || EF.Functions.ILike(x.Body, term));
        }
        var result = await Project(query.OrderByDescending(x => x.CreatedAt), db, context.User.GetUserId())
            .ToPagedResultAsync(page, pageSize, cancellationToken);
        return ApiResults.Ok(context, result, "Testimonials retrieved successfully.");
    }

    private static async Task<IResult> Get(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var me = context.User.GetUserId();
        if (!await db.Testimonials.AsNoTracking().AnyAsync(x => x.Id == id, cancellationToken))
            return EndpointHelpers.NotFound(context, "Testimonial was not found.");
        if (!await db.TestimonialReads.AnyAsync(x => x.TestimonialId == id && x.UserId == me, cancellationToken))
        {
            db.TestimonialReads.Add(new TestimonialRead(id, me));
            await db.SaveChangesAsync(cancellationToken);
        }
        var result = await Project(db.Testimonials.AsNoTracking().Where(x => x.Id == id), db, me)
            .SingleAsync(cancellationToken);
        return ApiResults.Ok(context, result, "Testimonial retrieved successfully.");
    }

    // Public by design: external recipients and link-preview clients need the story without a
    // Mirage session. Keep this projection limited to data already displayed on a published story.
    private static async Task<IResult> GetShareInfo(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var story = await db.Testimonials.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new TestimonialShareResponse(
                x.Id, x.AuthorUserId,
                db.Profiles.Where(p => p.UserId == x.AuthorUserId).Select(p => p.DisplayName).FirstOrDefault()
                    ?? "Mirage member",
                db.Profiles.Where(p => p.UserId == x.AuthorUserId).Select(p => p.AvatarUrl).FirstOrDefault(),
                x.TaggedUserId,
                x.TaggedUserId == null ? null : db.Profiles.Where(p => p.UserId == x.TaggedUserId)
                    .Select(p => p.DisplayName).FirstOrDefault(),
                x.TaggedUserId == null ? null : db.Profiles.Where(p => p.UserId == x.TaggedUserId)
                    .Select(p => p.AvatarUrl).FirstOrDefault(),
                x.Title, x.Body, x.ImageUrl, x.ImageUrl2, x.ImageUrl3,
                x.Reads.Count, x.Likes.Count, x.Comments.Count, x.CreatedAt, x.CelebrationType))
            .SingleOrDefaultAsync(cancellationToken);

        return story is null
            ? EndpointHelpers.NotFound(context, "Testimonial was not found.")
            : ApiResults.Ok(context, story, "Testimonial share info retrieved successfully.");
    }

    private static async Task<IResult> Create(CreateTestimonialRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length is < 8 or > 160)
            return EndpointHelpers.ValidationProblem(context, ("title", "Use a title between 8 and 160 characters."));
        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Trim().Length is < 100 or > 12000)
            return EndpointHelpers.ValidationProblem(context, ("body", "Your story must be between 100 and 12,000 characters."));
        var me = context.User.GetUserId();
        if (request.TaggedUserId == me)
            return EndpointHelpers.ValidationProblem(context, ("taggedUserId", "You cannot tag yourself."));
        if (request.TaggedUserId.HasValue && !await db.Profiles.AsNoTracking()
                .AnyAsync(x => x.UserId == request.TaggedUserId, cancellationToken))
            return EndpointHelpers.ValidationProblem(context, ("taggedUserId", "The tagged profile was not found."));
        var images = NormaliseImages(request.ImageUrls, request.ImageUrl);
        if (images.Count > 3)
            return EndpointHelpers.ValidationProblem(context, ("imageUrls", "A testimony can contain at most three images."));
        var item = new Testimonial(me, request.Title, request.Body, images.ElementAtOrDefault(0),
            request.TaggedUserId, images.ElementAtOrDefault(1), images.ElementAtOrDefault(2));
        db.Testimonials.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        var response = await Project(db.Testimonials.AsNoTracking().Where(x => x.Id == item.Id), db, me)
            .SingleAsync(cancellationToken);
        return ApiResults.Created(context, $"/api/v1/testimonials/{item.Id}", response, "Your testimony was published.");
    }

    private static async Task<IResult> Update(Guid id, UpdateTestimonialRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length is < 8 or > 160)
            return EndpointHelpers.ValidationProblem(context, ("title", "Use a title between 8 and 160 characters."));
        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Trim().Length is < 100 or > 12000)
            return EndpointHelpers.ValidationProblem(context, ("body", "Your story must be between 100 and 12,000 characters."));

        var me = context.User.GetUserId();
        var testimonial = await db.Testimonials.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (testimonial is null) return EndpointHelpers.NotFound(context, "Testimonial was not found.");
        if (testimonial.AuthorUserId != me)
            return EndpointHelpers.Forbidden(context, "Only the testimony author can edit this story.");
        if (request.TaggedUserId == me)
            return EndpointHelpers.ValidationProblem(context, ("taggedUserId", "You cannot tag yourself."));
        if (request.TaggedUserId.HasValue && !await db.Profiles.AsNoTracking()
                .AnyAsync(x => x.UserId == request.TaggedUserId, cancellationToken))
            return EndpointHelpers.ValidationProblem(context, ("taggedUserId", "The tagged profile was not found."));

        var images = NormaliseImages(request.ImageUrls, null);
        if (images.Count > 3)
            return EndpointHelpers.ValidationProblem(context, ("imageUrls", "A testimony can contain at most three images."));
        testimonial.Update(request.Title, request.Body, images, request.TaggedUserId);
        await db.SaveChangesAsync(cancellationToken);
        var response = await Project(db.Testimonials.AsNoTracking().Where(x => x.Id == id), db, me)
            .SingleAsync(cancellationToken);
        return ApiResults.Ok(context, response, "Your testimony was updated.");
    }

    private static async Task<IResult> Delete(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var testimonial = await db.Testimonials.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (testimonial is null) return EndpointHelpers.NotFound(context, "Testimonial was not found.");
        if (testimonial.AuthorUserId != context.User.GetUserId())
            return EndpointHelpers.Forbidden(context, "Only the testimony author can delete this story.");

        db.Testimonials.Remove(testimonial);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> Like(Guid id, HttpContext context, IMirageDbContext db, CancellationToken ct)
    {
        var me = context.User.GetUserId();
        if (!await db.Testimonials.AsNoTracking().AnyAsync(x => x.Id == id, ct)) return EndpointHelpers.NotFound(context, "Testimonial was not found.");
        if (!await db.TestimonialLikes.AnyAsync(x => x.TestimonialId == id && x.UserId == me, ct))
        { db.TestimonialLikes.Add(new TestimonialLike(id, me)); await db.SaveChangesAsync(ct); }
        var response = await Project(db.Testimonials.AsNoTracking().Where(x => x.Id == id), db, me).SingleAsync(ct);
        return ApiResults.Ok(context, response, "Testimonial liked.");
    }

    private static async Task<IResult> Unlike(Guid id, HttpContext context, IMirageDbContext db, CancellationToken ct)
    {
        var me = context.User.GetUserId();
        var like = await db.TestimonialLikes.SingleOrDefaultAsync(x => x.TestimonialId == id && x.UserId == me, ct);
        if (like is not null) { db.TestimonialLikes.Remove(like); await db.SaveChangesAsync(ct); }
        var response = await Project(db.Testimonials.AsNoTracking().Where(x => x.Id == id), db, me).SingleAsync(ct);
        return ApiResults.Ok(context, response, "Like removed.");
    }

    private static async Task<IResult> ListComments(Guid id, HttpContext context, IMirageDbContext db, CancellationToken ct)
    {
        var me = context.User.GetUserId();
        var comments = await db.TestimonialComments.AsNoTracking().Where(x => x.TestimonialId == id)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new TestimonialCommentResponse(x.Id, x.TestimonialId, x.AuthorUserId,
                db.Profiles.Where(p => p.UserId == x.AuthorUserId).Select(p => p.DisplayName).FirstOrDefault() ?? "Mirage member",
                db.Profiles.Where(p => p.UserId == x.AuthorUserId).Select(p => p.AvatarUrl).FirstOrDefault(),
                x.ParentCommentId, x.Body, x.CreatedAt, x.Likes.Count, x.Likes.Any(l => l.UserId == me),
                x.Replies.Count)).ToListAsync(ct);
        return ApiResults.Ok(context, comments, "Comments retrieved.");
    }

    private static async Task<IResult> Comment(Guid id, CreateTestimonialCommentRequest request,
        HttpContext context, IMirageDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Trim().Length is < 2 or > 2000)
            return EndpointHelpers.ValidationProblem(context, ("body", "Comment must be between 2 and 2,000 characters."));
        if (!await db.Testimonials.AsNoTracking().AnyAsync(x => x.Id == id, ct)) return EndpointHelpers.NotFound(context, "Testimonial was not found.");
        if (request.ParentCommentId.HasValue)
        {
            var parent = await db.TestimonialComments.AsNoTracking()
                .Where(x => x.Id == request.ParentCommentId && x.TestimonialId == id)
                .Select(x => new { x.ParentCommentId }).SingleOrDefaultAsync(ct);
            if (parent is null)
                return EndpointHelpers.ValidationProblem(context, ("parentCommentId", "Reply target was not found."));
            if (parent.ParentCommentId.HasValue)
                return EndpointHelpers.ValidationProblem(context, ("parentCommentId", "Replies can only be posted to top-level comments."));
        }
        var comment = new TestimonialComment(id, context.User.GetUserId(), request.Body, request.ParentCommentId);
        db.TestimonialComments.Add(comment); await db.SaveChangesAsync(ct);
        var author = await db.Profiles.AsNoTracking().Where(x => x.UserId == comment.AuthorUserId)
            .Select(x => new { x.DisplayName, x.AvatarUrl }).SingleAsync(ct);
        var response = new TestimonialCommentResponse(comment.Id, comment.TestimonialId, comment.AuthorUserId,
            author.DisplayName, author.AvatarUrl, comment.ParentCommentId, comment.Body, comment.CreatedAt, 0, false, 0);
        return ApiResults.Created(context, $"/api/v1/testimonials/{id}", response, "Comment posted.");
    }

    private static async Task<IResult> LikeComment(Guid id, Guid commentId, HttpContext context,
        IMirageDbContext db, CancellationToken ct)
    {
        var me = context.User.GetUserId();
        if (!await db.TestimonialComments.AsNoTracking().AnyAsync(x => x.Id == commentId && x.TestimonialId == id, ct))
            return EndpointHelpers.NotFound(context, "Comment was not found.");
        if (!await db.TestimonialCommentLikes.AnyAsync(x => x.CommentId == commentId && x.UserId == me, ct))
        {
            db.TestimonialCommentLikes.Add(new TestimonialCommentLike(commentId, me));
            await db.SaveChangesAsync(ct);
        }
        return ApiResults.Ok(context, new
        {
            CommentId = commentId,
            LikeCount = await db.TestimonialCommentLikes.CountAsync(x => x.CommentId == commentId, ct),
            LikedByMe = true
        }, "Comment liked.");
    }

    private static async Task<IResult> UnlikeComment(Guid id, Guid commentId, HttpContext context,
        IMirageDbContext db, CancellationToken ct)
    {
        var me = context.User.GetUserId();
        if (!await db.TestimonialComments.AsNoTracking().AnyAsync(x => x.Id == commentId && x.TestimonialId == id, ct))
            return EndpointHelpers.NotFound(context, "Comment was not found.");
        var like = await db.TestimonialCommentLikes.SingleOrDefaultAsync(
            x => x.CommentId == commentId && x.UserId == me, ct);
        if (like is not null)
        {
            db.TestimonialCommentLikes.Remove(like);
            await db.SaveChangesAsync(ct);
        }
        return ApiResults.Ok(context, new
        {
            CommentId = commentId,
            LikeCount = await db.TestimonialCommentLikes.CountAsync(x => x.CommentId == commentId, ct),
            LikedByMe = false
        }, "Comment like removed.");
    }

    private static async Task<IResult> SearchTaggableProfiles(HttpContext context, IMirageDbContext db,
        string? search, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(search) || search.Trim().Length < 2)
            return ApiResults.Ok(context, Array.Empty<object>(), "Profiles retrieved.");
        var me = context.User.GetUserId();
        var term = $"%{search.Trim()}%";
        var profiles = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId != me && EF.Functions.ILike(x.DisplayName, term))
            .OrderBy(x => x.DisplayName).Take(8)
            .Select(x => new { x.UserId, x.DisplayName, x.AvatarUrl }).ToListAsync(ct);
        return ApiResults.Ok(context, profiles, "Profiles retrieved.");
    }

    private static IReadOnlyList<string> NormaliseImages(IReadOnlyList<string>? imageUrls, string? legacyImageUrl)
    {
        var values = imageUrls?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList() ?? [];
        if (values.Count == 0 && !string.IsNullOrWhiteSpace(legacyImageUrl)) values.Add(legacyImageUrl.Trim());
        return values.Distinct(StringComparer.Ordinal).ToList();
    }
}
