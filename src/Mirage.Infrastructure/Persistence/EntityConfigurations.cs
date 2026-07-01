using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mirage.Domain.Entities;
using Mirage.Infrastructure.Identity;

namespace Mirage.Infrastructure.Persistence;

public sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> b)
    {
        b.ToTable("profiles");
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.UserId).IsUnique();
        b.HasIndex(x => new { x.Intent, x.City });
        b.Property(x => x.DisplayName).HasMaxLength(120);
        b.Property(x => x.City).HasMaxLength(100);
        b.Property(x => x.Country).HasMaxLength(100);
        b.Property(x => x.Denomination).HasMaxLength(100);
        b.Property(x => x.Bio).HasMaxLength(1000);
        b.Property(x => x.Interests).HasColumnType("text[]");
        b.HasOne<ApplicationUser>().WithOne().HasForeignKey<UserProfile>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class OrganisationConfiguration : IEntityTypeConfiguration<Organisation>
{
    public void Configure(EntityTypeBuilder<Organisation> b)
    {
        b.ToTable("organisations");
        b.HasIndex(x => x.RegistrationNumber).IsUnique();
        b.HasIndex(x => x.Status);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.RegistrationNumber).HasMaxLength(100);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AdminUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CounsellorConfiguration : IEntityTypeConfiguration<CounsellorProfile>
{
    public void Configure(EntityTypeBuilder<CounsellorProfile> b)
    {
        b.ToTable("counsellors");
        b.HasIndex(x => x.UserId).IsUnique();
        b.Property(x => x.Specialisations).HasColumnType("text[]");
        b.Property(x => x.Languages).HasColumnType("text[]");
        b.HasOne(x => x.Organisation).WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.UserProfile).WithMany().HasForeignKey(x => x.UserId).HasPrincipalKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RecommendationConfiguration : IEntityTypeConfiguration<Recommendation>
{
    public void Configure(EntityTypeBuilder<Recommendation> b)
    {
        b.ToTable("recommendations");
        b.HasIndex(x => new { x.RecommendedUserId, x.RecommendedByUserId }).IsUnique();
        b.Property(x => x.Note).HasMaxLength(500);
        b.HasOne(x => x.Organisation).WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RecommendedUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RecommendedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class UserLikeConfiguration : IEntityTypeConfiguration<UserLike>
{
    public void Configure(EntityTypeBuilder<UserLike> b)
    {
        b.ToTable("likes");
        b.HasIndex(x => new { x.SourceUserId, x.TargetUserId }).IsUnique();
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.SourceUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.TargetUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MatchConfiguration : IEntityTypeConfiguration<Match>
{
    public void Configure(EntityTypeBuilder<Match> b)
    {
        b.ToTable("matches");
        b.HasIndex(x => new { x.User1Id, x.User2Id }).IsUnique();
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.User1Id).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.User2Id).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ChatRequestedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DateRequestConfiguration : IEntityTypeConfiguration<DateRequest>
{
    public void Configure(EntityTypeBuilder<DateRequest> b)
    {
        b.ToTable("date_requests");
        b.HasIndex(x => new { x.Status, x.StartsAt });
        b.Property(x => x.Activity).HasMaxLength(200);
        b.Property(x => x.LocationArea).HasMaxLength(200);
        b.Property(x => x.Note).HasMaxLength(1000);
        b.Property(x => x.ItemsToBring).HasMaxLength(500);
        b.HasIndex(x => new { x.Intent, x.Status, x.StartsAt });
        b.HasMany(x => x.Acceptances).WithOne(x => x.DateRequest).HasForeignKey(x => x.DateRequestId);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RequestorUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.SelectedUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DateRequestAcceptanceConfiguration : IEntityTypeConfiguration<DateRequestAcceptance>
{
    public void Configure(EntityTypeBuilder<DateRequestAcceptance> b)
    {
        b.ToTable("date_request_acceptances");
        b.HasIndex(x => new { x.DateRequestId, x.AcceptorUserId }).IsUnique();
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AcceptorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CounsellingSessionConfiguration : IEntityTypeConfiguration<CounsellingSession>
{
    public void Configure(EntityTypeBuilder<CounsellingSession> b)
    {
        b.ToTable("counselling_sessions");
        b.HasIndex(x => new { x.CounsellorId, x.ScheduledAt });
        b.HasIndex(x => new { x.ClientUserId, x.ScheduledAt });
        b.Property(x => x.Topic).HasMaxLength(1000);
        b.HasOne(x => x.Counsellor).WithMany().HasForeignKey(x => x.CounsellorId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ClientUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AnonymityAuditLogConfiguration : IEntityTypeConfiguration<AnonymityAuditLog>
{
    public void Configure(EntityTypeBuilder<AnonymityAuditLog> b)
    {
        b.ToTable("anonymity_audit_logs");
        b.HasIndex(x => new { x.SessionId, x.CreatedAt });
        b.Property(x => x.Action).HasMaxLength(100);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> b)
    {
        b.ToTable("messages");
        b.HasIndex(x => new { x.MatchId, x.CreatedAt });
        b.HasIndex(x => new { x.MatchId, x.IsRead }).HasFilter("\"IsRead\" = false");
        b.Property(x => x.Content).HasMaxLength(2000);
        b.Property(x => x.AttachmentUrl).HasMaxLength(1000);
        b.HasOne(x => x.Match).WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MentorRequestConfiguration : IEntityTypeConfiguration<MentorRequest>
{
    public void Configure(EntityTypeBuilder<MentorRequest> b)
    {
        b.ToTable("mentor_requests");
        b.HasIndex(x => new { x.MentorProfileId, x.MenteeUserId }).IsUnique();
        b.HasIndex(x => x.Status);
        b.Property(x => x.Message).HasMaxLength(1000);
        b.HasOne(x => x.Mentor).WithMany().HasForeignKey(x => x.MentorProfileId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.MenteeUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SessionNoteConfiguration : IEntityTypeConfiguration<SessionNote>
{
    public void Configure(EntityTypeBuilder<SessionNote> b)
    {
        b.ToTable("session_notes");
        b.HasIndex(x => x.SessionId);
        b.Property(x => x.Content).HasMaxLength(5000);
        b.HasOne<CounsellingSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SessionRatingConfiguration : IEntityTypeConfiguration<SessionRating>
{
    public void Configure(EntityTypeBuilder<SessionRating> b)
    {
        b.ToTable("session_ratings");
        b.HasIndex(x => new { x.SessionId, x.ReviewerUserId }).IsUnique();
        b.Property(x => x.Comment).HasMaxLength(1000);
        b.HasOne<CounsellingSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ReviewerUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MilestoneLogConfiguration : IEntityTypeConfiguration<MilestoneLog>
{
    public void Configure(EntityTypeBuilder<MilestoneLog> b)
    {
        b.ToTable("milestone_logs");
        b.HasIndex(x => new { x.UserId, x.Type });
        b.Property(x => x.Note).HasMaxLength(500);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.PartnerId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class DateFeedbackConfiguration : IEntityTypeConfiguration<DateFeedback>
{
    public void Configure(EntityTypeBuilder<DateFeedback> b)
    {
        b.ToTable("date_feedbacks");
        b.HasIndex(x => new { x.DateRequestId, x.ReviewerUserId }).IsUnique();
        b.HasIndex(x => x.ReviewedUserId);
        b.Property(x => x.Comment).HasMaxLength(1000);
        b.HasOne<DateRequest>().WithMany().HasForeignKey(x => x.DateRequestId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ReviewerUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ReviewedUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ContentReportConfiguration : IEntityTypeConfiguration<ContentReport>
{
    public void Configure(EntityTypeBuilder<ContentReport> b)
    {
        b.ToTable("content_reports");
        b.HasIndex(x => new { x.Status, x.CreatedAt });
        b.HasIndex(x => new { x.TargetType, x.TargetId });
        b.Property(x => x.Details).HasMaxLength(2000);
        b.Property(x => x.Resolution).HasMaxLength(2000);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ReportedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("notifications");
        b.HasIndex(x => new { x.UserId, x.CreatedAt });
        b.HasIndex(x => new { x.UserId, x.IsRead }).HasFilter("\"IsRead\" = false");
        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.Body).HasMaxLength(1000);
        b.Property(x => x.ReferenceType).HasMaxLength(100);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CounsellorInviteConfiguration : IEntityTypeConfiguration<CounsellorInvite>
{
    public void Configure(EntityTypeBuilder<CounsellorInvite> b)
    {
        b.ToTable("counsellor_invites");
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.OrganisationId, x.Email });
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.TokenHash).HasMaxLength(64);
        b.HasOne(x => x.Organisation).WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class MentorProfileConfiguration : IEntityTypeConfiguration<MentorProfile>
{
    public void Configure(EntityTypeBuilder<MentorProfile> b)
    {
        b.ToTable("mentors");
        b.HasIndex(x => x.UserId).IsUnique();
        b.Property(x => x.Testimony).HasMaxLength(2000);
        b.Property(x => x.AreasOfGuidance).HasColumnType("text[]");
        b.Property(x => x.Languages).HasColumnType("text[]");
        b.HasOne(x => x.UserProfile).WithMany().HasForeignKey(x => x.UserId).HasPrincipalKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.UserId, x.ExpiresAt });
        b.Property(x => x.TokenHash).HasMaxLength(64);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
