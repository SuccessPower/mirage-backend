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
        b.Property(x => x.PhotoUrls).HasColumnType("text[]");
        b.Property(x => x.PreferredLanguage).HasMaxLength(60);
        b.Property(x => x.Occupation).HasMaxLength(160);
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
        b.Property(x => x.LogoUrl).HasMaxLength(1000);
        b.Property(x => x.WebsiteUrl).HasMaxLength(1000);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AdminUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class VendorConfiguration : IEntityTypeConfiguration<Vendor>
{
    public void Configure(EntityTypeBuilder<Vendor> b)
    {
        b.ToTable("vendors");
        b.HasIndex(x => x.OwnerUserId);
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.Category);
        b.Property(x => x.BusinessName).HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.Email).HasMaxLength(255);
        b.Property(x => x.Phone).HasMaxLength(40);
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.City).HasMaxLength(100);
        b.Property(x => x.Country).HasMaxLength(100);
        b.Property(x => x.PhotoUrls).HasColumnType("text[]");
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OrganisationBranchConfiguration : IEntityTypeConfiguration<OrganisationBranch>
{
    public void Configure(EntityTypeBuilder<OrganisationBranch> b)
    {
        b.ToTable("organisation_branches");
        b.HasIndex(x => x.OrganisationId);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.City).HasMaxLength(100);
        b.Property(x => x.Country).HasMaxLength(100);
        b.Property(x => x.Address).HasMaxLength(300);
        b.HasOne(x => x.Organisation).WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class OrganisationMemberConfiguration : IEntityTypeConfiguration<OrganisationMember>
{
    public void Configure(EntityTypeBuilder<OrganisationMember> b)
    {
        b.ToTable("organisation_members");
        b.HasIndex(x => new { x.OrganisationId, x.UserId }).IsUnique();
        b.HasOne(x => x.Organisation).WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<OrganisationBranch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AssignedMentorUserId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AssignedCounsellorUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class OrganisationManagerConfiguration : IEntityTypeConfiguration<OrganisationManager>
{
    public void Configure(EntityTypeBuilder<OrganisationManager> b)
    {
        b.ToTable("organisation_managers");
        b.HasIndex(x => new { x.OrganisationId, x.UserId }).IsUnique();
        b.HasOne(x => x.Organisation).WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<OrganisationBranch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class OrgEventConfiguration : IEntityTypeConfiguration<OrgEvent>
{
    public void Configure(EntityTypeBuilder<OrgEvent> b)
    {
        b.ToTable("org_events");
        b.HasIndex(x => x.OrganisationId);
        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.ImageUrl).HasMaxLength(1000);
        b.Property(x => x.Location).HasMaxLength(300);
        b.HasOne(x => x.Organisation).WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<OrganisationBranch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class EventTicketConfiguration : IEntityTypeConfiguration<EventTicket>
{
    public void Configure(EntityTypeBuilder<EventTicket> b)
    {
        b.ToTable("event_tickets");
        b.HasIndex(x => new { x.EventId, x.UserId }).IsUnique();
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Code).HasMaxLength(40);
        b.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CommunityConfiguration : IEntityTypeConfiguration<Community>
{
    public void Configure(EntityTypeBuilder<Community> b)
    {
        b.ToTable("communities");
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.Category);
        b.HasIndex(x => new { x.OrganisationId, x.Category }).IsUnique()
            .HasFilter("\"OrganisationId\" IS NOT NULL");
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.Category).HasMaxLength(80);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.AvatarUrl).HasMaxLength(1000);
        b.Property(x => x.AvatarKey).HasMaxLength(80);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Organisation>().WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CommunityMemberConfiguration : IEntityTypeConfiguration<CommunityMember>
{
    public void Configure(EntityTypeBuilder<CommunityMember> b)
    {
        b.ToTable("community_members");
        b.HasIndex(x => new { x.CommunityId, x.UserId }).IsUnique();
        b.HasIndex(x => new { x.UserId, x.LeftAt });
        b.HasOne(x => x.Community).WithMany(x => x.Members).HasForeignKey(x => x.CommunityId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CommunityPostConfiguration : IEntityTypeConfiguration<CommunityPost>
{
    public void Configure(EntityTypeBuilder<CommunityPost> b)
    {
        b.ToTable("community_posts");
        b.HasIndex(x => new { x.CommunityId, x.CreatedAt });
        b.HasIndex(x => new { x.CommunityId, x.IsHidden });
        b.Property(x => x.Body).HasMaxLength(2000);
        b.Property(x => x.ImageUrl).HasMaxLength(1000);
        b.HasOne(x => x.Community).WithMany(x => x.Posts).HasForeignKey(x => x.CommunityId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CommunityPostLikeConfiguration : IEntityTypeConfiguration<CommunityPostLike>
{
    public void Configure(EntityTypeBuilder<CommunityPostLike> b)
    {
        b.ToTable("community_post_likes");
        b.HasIndex(x => new { x.PostId, x.UserId }).IsUnique();
        b.HasOne(x => x.Post).WithMany(x => x.Likes).HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CommunityPostVoteConfiguration : IEntityTypeConfiguration<CommunityPostVote>
{
    public void Configure(EntityTypeBuilder<CommunityPostVote> b)
    {
        b.ToTable("community_post_votes");
        b.HasIndex(x => new { x.PostId, x.UserId }).IsUnique();
        b.Property(x => x.Value).HasColumnType("smallint");
        b.HasOne(x => x.Post).WithMany(x => x.Votes).HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CommunityPostCommentConfiguration : IEntityTypeConfiguration<CommunityPostComment>
{
    public void Configure(EntityTypeBuilder<CommunityPostComment> b)
    {
        b.ToTable("community_post_comments");
        b.HasIndex(x => new { x.PostId, x.CreatedAt });
        b.HasIndex(x => x.ParentCommentId);
        b.HasIndex(x => new { x.PostId, x.IsHidden });
        b.Property(x => x.Body).HasMaxLength(2000);
        b.Property(x => x.MentionedUserIds).HasColumnType("uuid[]");
        b.HasOne(x => x.Post).WithMany(x => x.Comments).HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.ParentComment).WithMany(x => x.Replies).HasForeignKey(x => x.ParentCommentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CommunityPostCommentLikeConfiguration : IEntityTypeConfiguration<CommunityPostCommentLike>
{
    public void Configure(EntityTypeBuilder<CommunityPostCommentLike> b)
    {
        b.ToTable("community_post_comment_likes");
        b.HasIndex(x => new { x.CommentId, x.UserId }).IsUnique();
        b.HasOne(x => x.Comment).WithMany(x => x.Likes).HasForeignKey(x => x.CommentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CommunityPostCommentVoteConfiguration : IEntityTypeConfiguration<CommunityPostCommentVote>
{
    public void Configure(EntityTypeBuilder<CommunityPostCommentVote> b)
    {
        b.ToTable("community_post_comment_votes");
        b.HasIndex(x => new { x.CommentId, x.UserId }).IsUnique();
        b.Property(x => x.Value).HasColumnType("smallint");
        b.HasOne(x => x.Comment).WithMany(x => x.Votes).HasForeignKey(x => x.CommentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class MentorPostConfiguration : IEntityTypeConfiguration<MentorPost>
{
    public void Configure(EntityTypeBuilder<MentorPost> b)
    {
        b.ToTable("mentor_posts");
        b.HasIndex(x => x.MentorProfileId);
        b.Property(x => x.Content).HasMaxLength(2000);
        b.HasOne<MentorProfile>().WithMany().HasForeignKey(x => x.MentorProfileId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class MentorGroupMessageConfiguration : IEntityTypeConfiguration<MentorGroupMessage>
{
    public void Configure(EntityTypeBuilder<MentorGroupMessage> b)
    {
        b.ToTable("mentor_group_messages");
        b.HasIndex(x => x.MentorProfileId);
        b.Property(x => x.Content).HasMaxLength(2000);
        b.HasOne<MentorProfile>().WithMany().HasForeignKey(x => x.MentorProfileId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MentorMeetingConfiguration : IEntityTypeConfiguration<MentorMeeting>
{
    public void Configure(EntityTypeBuilder<MentorMeeting> b)
    {
        b.ToTable("mentor_meetings");
        b.HasIndex(x => x.MentorProfileId);
        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.MeetingLink).HasMaxLength(500);
        b.HasOne<MentorProfile>().WithMany().HasForeignKey(x => x.MentorProfileId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ScheduledByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MentorMessageConfiguration : IEntityTypeConfiguration<MentorMessage>
{
    public void Configure(EntityTypeBuilder<MentorMessage> b)
    {
        b.ToTable("mentor_messages");
        b.HasIndex(x => x.MentorRequestId);
        b.Property(x => x.Content).HasMaxLength(2000);
        b.HasOne<MentorRequest>().WithMany().HasForeignKey(x => x.MentorRequestId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OrganisationAdminInviteConfiguration : IEntityTypeConfiguration<OrganisationAdminInvite>
{
    public void Configure(EntityTypeBuilder<OrganisationAdminInvite> b)
    {
        b.ToTable("organisation_admin_invites");
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.Email);
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.TokenHash).HasMaxLength(64);
    }
}

public sealed class GatheringInviteConfiguration : IEntityTypeConfiguration<GatheringInvite>
{
    public void Configure(EntityTypeBuilder<GatheringInvite> b)
    {
        b.ToTable("gathering_invites");
        b.HasIndex(x => new { x.Kind, x.TargetId, x.InviteeUserId });
        b.HasIndex(x => new { x.InviteeUserId, x.Status });
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.InviterUserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.InviteeUserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<OrganisationBranch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class CoupleConfiguration : IEntityTypeConfiguration<Couple>
{
    public void Configure(EntityTypeBuilder<Couple> b)
    {
        b.ToTable("couples");
        b.HasIndex(x => new { x.User1Id, x.User2Id }).IsUnique();
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.User1Id).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.User2Id).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CounsellingMessageConfiguration : IEntityTypeConfiguration<CounsellingMessage>
{
    public void Configure(EntityTypeBuilder<CounsellingMessage> b)
    {
        b.ToTable("counselling_messages");
        b.HasIndex(x => x.SessionId);
        b.Property(x => x.Content).HasMaxLength(2000);
        b.HasOne<CounsellingSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CounsellingMeetingConfiguration : IEntityTypeConfiguration<CounsellingMeeting>
{
    public void Configure(EntityTypeBuilder<CounsellingMeeting> b)
    {
        b.ToTable("counselling_meetings");
        b.HasIndex(x => x.SessionId);
        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.MeetingLink).HasMaxLength(500);
        b.HasOne<CounsellingSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ScheduledByUserId).OnDelete(DeleteBehavior.Restrict);
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
        b.Property(x => x.VerificationDocumentUrls).HasColumnType("text[]");
        b.Property(x => x.RejectionReason).HasMaxLength(500);
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
        b.Property(x => x.ImageUrl).HasMaxLength(1000);
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
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.PartnerUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("payments");
        b.HasIndex(x => x.CounsellingSessionId).IsUnique();
        b.HasIndex(x => x.ProviderReference).IsUnique();
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.ProviderReference).HasMaxLength(200);
        b.Property(x => x.ProviderTransactionId).HasMaxLength(200);
        b.HasOne(x => x.CounsellingSession).WithOne(x => x.Payment)
            .HasForeignKey<Payment>(x => x.CounsellingSessionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.PayerUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CounsellorProfile>().WithMany().HasForeignKey(x => x.CounsellorId).OnDelete(DeleteBehavior.Restrict);
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
