using Mirage.Domain.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mirage.Domain.Entities;

public sealed class Testimonial : Entity
{
    private Testimonial() { }

    public Testimonial(Guid authorUserId, string title, string body, string? imageUrl = null, Guid? taggedUserId = null,
        string? imageUrl2 = null, string? imageUrl3 = null)
    {
        if (authorUserId == taggedUserId) throw new ArgumentException("You cannot tag yourself.", nameof(taggedUserId));
        AuthorUserId = authorUserId;
        Title = title.Trim();
        Body = body.Trim();
        ImageUrl = imageUrl?.Trim();
        ImageUrl2 = imageUrl2?.Trim();
        ImageUrl3 = imageUrl3?.Trim();
        TaggedUserId = taggedUserId;
    }

    public Guid AuthorUserId { get; private set; }
    public Guid? TaggedUserId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public string? ImageUrl { get; private set; }
    public string? ImageUrl2 { get; private set; }
    public string? ImageUrl3 { get; private set; }
    [NotMapped] public IReadOnlyList<string> ImageUrls =>
        new[] { ImageUrl, ImageUrl2, ImageUrl3 }.OfType<string>().Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
    public List<TestimonialRead> Reads { get; private set; } = [];
    public List<TestimonialLike> Likes { get; private set; } = [];
    public List<TestimonialComment> Comments { get; private set; } = [];
}

public sealed class TestimonialRead : Entity
{
    private TestimonialRead() { }
    public TestimonialRead(Guid testimonialId, Guid userId) { TestimonialId = testimonialId; UserId = userId; }
    public Guid TestimonialId { get; private set; }
    public Guid UserId { get; private set; }
    public Testimonial Testimonial { get; private set; } = null!;
}

public sealed class TestimonialLike : Entity
{
    private TestimonialLike() { }
    public TestimonialLike(Guid testimonialId, Guid userId) { TestimonialId = testimonialId; UserId = userId; }
    public Guid TestimonialId { get; private set; }
    public Guid UserId { get; private set; }
    public Testimonial Testimonial { get; private set; } = null!;
}

public sealed class TestimonialComment : Entity
{
    private TestimonialComment() { }
    public TestimonialComment(Guid testimonialId, Guid authorUserId, string body, Guid? parentCommentId = null)
    {
        TestimonialId = testimonialId;
        AuthorUserId = authorUserId;
        Body = body.Trim();
        ParentCommentId = parentCommentId;
    }
    public Guid TestimonialId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public Guid? ParentCommentId { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public Testimonial Testimonial { get; private set; } = null!;
    public TestimonialComment? ParentComment { get; private set; }
    public List<TestimonialComment> Replies { get; private set; } = [];
}
