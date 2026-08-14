using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Mirage.Domain.Services;
using Xunit;

namespace Mirage.Domain.Tests;

public sealed class DomainInvariantTests
{
    [Fact]
    public void Encrypting_a_message_removes_all_plaintext_payload_fields()
    {
        var message = new Message(Guid.NewGuid(), Guid.NewGuid(), "private caption", MessageType.Image,
            "https://storage.example/private-photo.jpg");

        message.SetEncryptedContent("ciphertext-value", "nonce-value", Guid.NewGuid().ToString("N"));

        Assert.True(message.IsEncrypted);
        Assert.Empty(message.Content);
        Assert.Null(message.AttachmentUrl);
        Assert.Equal(1, message.EncryptionVersion);
    }

    [Fact]
    public void Encryption_identity_rejects_weak_recovery_derivation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChatEncryptionIdentity(Guid.NewGuid(),
            "public-key", "encrypted-private-key", "nonce", "salt", 100_000));
    }

    [Fact]
    public void Encrypting_a_counselling_message_removes_plaintext_and_attachment_location()
    {
        var message = new CounsellingMessage(Guid.NewGuid(), Guid.NewGuid(), "confidential notes",
            MessageType.Image, "https://storage.example/counselling-photo.jpg");

        message.SetEncryptedContent("ciphertext-value", "nonce-value", Guid.NewGuid().ToString("N"));

        Assert.True(message.IsEncrypted);
        Assert.Empty(message.Content);
        Assert.Null(message.AttachmentUrl);
    }

    [Fact]
    public void Message_preserves_the_replied_to_message_reference()
    {
        var repliedToId = Guid.NewGuid();

        var message = new Message(Guid.NewGuid(), Guid.NewGuid(), "Reply", replyToMessageId: repliedToId);

        Assert.Equal(repliedToId, message.ReplyToMessageId);
    }

    [Fact]
    public void Google_profile_completion_sets_the_validated_avatar()
    {
        var profile = new UserProfile(Guid.NewGuid(), "Google User", avatarUrl: null);

        profile.CompleteProfile(new DateOnly(1995, 5, 20), "Lagos", "Nigeria", "Christian",
            "A complete profile", "https://res.cloudinary.com/mirage/face.jpg",
            Sex.Female, RelationshipStatus.Single, "Engineer");

        Assert.True(profile.IsProfileComplete);
        Assert.Equal("https://res.cloudinary.com/mirage/face.jpg", profile.AvatarUrl);
    }

    [Theory]
    [InlineData("Daystar", "Daystar Christian Centre")]
    [InlineData("DAYSTAR!", "Daystar Christian Center")]
    [InlineData("The Elevation", "The Elevation Church International")]
    public void Organisation_identity_matches_brand_name_variants(string candidate, string existing)
    {
        Assert.True(OrganisationIdentity.IsLikelyDuplicate(
            candidate, "Nigeria", null, existing, "Nigeria", null));
    }

    [Theory]
    [InlineData("Nigeria", "NG", "AF")]
    [InlineData("United Kingdom", "GB", "EU")]
    [InlineData("United States", "US", "NA")]
    public void Country_metadata_normalizes_international_locations(string country, string code, string continent)
    {
        var profile = new UserProfile(Guid.NewGuid(), "International User", new DateOnly(1990, 1, 1),
            "City", country, "Other", "International profile description");

        Assert.Equal(code, profile.CountryCode);
        Assert.Equal(continent, profile.ContinentCode);
        Assert.Equal(DiscoveryScope.Continent, profile.DiscoveryScope);
    }

    [Fact]
    public void Organisation_identity_does_not_merge_same_name_across_countries()
    {
        Assert.False(OrganisationIdentity.IsLikelyDuplicate(
            "Daystar", "Ghana", null, "Daystar Christian Centre", "Nigeria", null));
    }

    [Fact]
    public void Organisation_identity_uses_canonical_website_host()
    {
        Assert.True(OrganisationIdentity.IsLikelyDuplicate(
            "Unrelated display name", "Ghana", "https://www.daystarng.org/about",
            "Daystar Christian Centre", "Nigeria", "daystarng.org"));
    }

    [Fact]
    public void Organisation_identity_avoids_ambiguous_single_word_names()
    {
        Assert.False(OrganisationIdentity.IsLikelyDuplicate(
            "Grace", "Nigeria", null, "Grace Church International", "Nigeria", null));
    }

    [Fact]
    public void Date_request_rejects_invalid_time_window()
    {
        var startsAt = DateTimeOffset.UtcNow.AddDays(1);
        Assert.Throws<ArgumentException>(() =>
            new DateRequest(Guid.NewGuid(), "Coffee", startsAt, startsAt, "Lagos", null));
    }

    [Fact]
    public void Match_orders_users_to_enforce_unique_pair()
    {
        var first = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var second = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var match = new Match(second, first);
        Assert.Equal(first, match.User1Id);
        Assert.Equal(second, match.User2Id);
    }

    [Fact]
    public void Trust_unlock_requires_both_parties()
    {
        var session = new CounsellingSession(Guid.NewGuid(), Guid.NewGuid(), SessionType.Personal,
            DateTimeOffset.UtcNow.AddDays(1), "Communication", true, true);
        session.ConsentToReveal(true);
        Assert.Equal(TrustUnlockStatus.Pending, session.TrustUnlockStatus);
        session.ConsentToReveal(false);
        Assert.Equal(TrustUnlockStatus.Unlocked, session.TrustUnlockStatus);
    }

    [Fact]
    public void Paid_counselling_payout_requires_completion_then_admin_approval()
    {
        var payment = new Payment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10_000m, "ngn");
        payment.Initialize(PaymentProvider.Paystack, PaymentMethod.Card, "payment-reference");
        payment.MarkSuccessful("transaction-id");

        Assert.Equal(1_500m, payment.PlatformFeeAmount);
        Assert.Equal(8_500m, payment.CounsellorAmount);
        Assert.Equal(PayoutStatus.Held, payment.PayoutStatus);

        payment.RequestPayoutApproval();
        payment.ApprovePayout(Guid.NewGuid());
        payment.MarkPayoutSubmitted("transfer-id");
        payment.MarkPayoutPaid();

        Assert.Equal(PayoutStatus.Paid, payment.PayoutStatus);
        Assert.NotNull(payment.PayoutPaidAt);
        Assert.StartsWith("mirage-payout-", payment.PayoutReference);
    }

    [Fact]
    public void Refunding_a_paid_session_cancels_the_counsellor_payout()
    {
        var payment = new Payment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10_000m, "ngn");
        payment.Initialize(PaymentProvider.Paystack, PaymentMethod.Card, "payment-reference");
        payment.MarkSuccessful("transaction-id");

        payment.MarkRefunded(10_000m, RefundReason.CounsellorNoShow, "refund-id", "Did not attend", null);

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(10_000m, payment.RefundedAmount);
        Assert.Equal(RefundReason.CounsellorNoShow, payment.RefundReason);
        // The money is going back to the member, so the counsellor is never paid for this session.
        Assert.Equal(PayoutStatus.Cancelled, payment.PayoutStatus);
        Assert.False(payment.IsRefundable);
    }

    [Fact]
    public void A_session_already_paid_out_cannot_be_refunded()
    {
        var payment = new Payment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10_000m, "ngn");
        payment.Initialize(PaymentProvider.Paystack, PaymentMethod.Card, "payment-reference");
        payment.MarkSuccessful("transaction-id");
        payment.RequestPayoutApproval();
        payment.ApprovePayout(Guid.NewGuid());
        payment.MarkPayoutPaid("transfer-id");

        Assert.False(payment.IsRefundable);
        Assert.Throws<InvalidOperationException>(() =>
            payment.MarkRefunded(10_000m, RefundReason.AdminDiscretion, null, null, Guid.NewGuid()));
    }

    [Fact]
    public void Counsellor_fees_are_held_to_the_admin_set_bounds()
    {
        var pricing = PlatformPricing.CreateDefault();
        pricing.Update(5_000m, 20_000m, "ngn", Guid.NewGuid());

        Assert.Equal("NGN", pricing.Currency);
        Assert.Null(pricing.Reject(12_000m, "NGN"));
        Assert.NotNull(pricing.Reject(4_999m, "NGN"));
        Assert.NotNull(pricing.Reject(20_001m, "NGN"));
        Assert.NotNull(pricing.Reject(12_000m, "USD"));
        Assert.Throws<InvalidOperationException>(() => pricing.Update(30_000m, 20_000m, "NGN", Guid.NewGuid()));
    }

    [Fact]
    public void Out_of_the_box_no_ceiling_is_imposed_on_counsellor_fees()
    {
        // A number hardcoded here would be wrong as soon as the market moved, so the default
        // bounds are open at both ends and only an admin narrows them.
        var pricing = PlatformPricing.CreateDefault();
        Assert.Null(pricing.MinSessionFee);
        Assert.Null(pricing.MaxSessionFee);
        Assert.Null(pricing.Reject(2_000_000m, "NGN"));

        // A floor alone is valid, and still leaves the top open.
        pricing.Update(5_000m, null, "NGN", Guid.NewGuid());
        Assert.Null(pricing.Reject(2_000_000m, "NGN"));
        Assert.NotNull(pricing.Reject(4_000m, "NGN"));
    }

    [Fact]
    public void Match_can_be_closed()
    {
        var closerId = Guid.NewGuid();
        var match = new Match(closerId, Guid.NewGuid());
        match.Close(closerId);
        Assert.Equal(MatchStatus.Closed, match.Status);
        Assert.Equal(closerId, match.ClosedByUserId);
        Assert.NotNull(match.LastActivityAt);
    }

    [Fact]
    public void Couple_chat_opens_match_without_request_handshake()
    {
        var match = new Match(Guid.NewGuid(), Guid.NewGuid());
        match.OpenForCouple();

        Assert.Equal(MatchStatus.Active, match.Status);
        Assert.Null(match.ChatRequestedByUserId);
        Assert.NotNull(match.LastActivityAt);
    }

    [Fact]
    public void Blocked_match_cannot_be_reopened_for_couple_chat()
    {
        var match = new Match(Guid.NewGuid(), Guid.NewGuid());
        match.Block(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(match.OpenForCouple);
        Assert.Equal(MatchStatus.Blocked, match.Status);
    }

    [Fact]
    public void Blocker_can_unblock_and_restore_the_previous_match_status()
    {
        var blockerId = Guid.NewGuid();
        var match = new Match(blockerId, Guid.NewGuid());
        match.OpenForCouple();
        match.Block(blockerId);

        match.Unblock(blockerId);

        Assert.Equal(MatchStatus.Active, match.Status);
        Assert.Null(match.BlockedByUserId);
        Assert.Null(match.StatusBeforeBlock);
    }

    [Fact]
    public void Other_participant_cannot_unblock_a_match()
    {
        var blockerId = Guid.NewGuid();
        var match = new Match(blockerId, Guid.NewGuid());
        match.Block(blockerId);

        Assert.Throws<InvalidOperationException>(() => match.Unblock(Guid.NewGuid()));
        Assert.Equal(MatchStatus.Blocked, match.Status);
    }

    [Fact]
    public void Profile_vote_rejects_self_vote()
    {
        var userId = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => new ProfileVote(userId, userId, 1));
    }

    [Fact]
    public void Profile_vote_value_can_change()
    {
        var vote = new ProfileVote(Guid.NewGuid(), Guid.NewGuid(), 1);
        vote.ChangeValue(-1);
        Assert.Equal(-1, vote.Value);
    }

    [Fact]
    public void Couple_friendship_starts_active_and_can_end()
    {
        var friendship = new CoupleFriendship(Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(CoupleFriendshipStatus.Active, friendship.Status);
        friendship.End();
        Assert.Equal(CoupleFriendshipStatus.Ended, friendship.Status);
        Assert.NotNull(friendship.EndedAt);
    }

    [Fact]
    public void Ended_couple_friendship_cannot_be_ended_again()
    {
        var friendship = new CoupleFriendship(Guid.NewGuid(), Guid.NewGuid());
        friendship.End();
        Assert.Throws<InvalidOperationException>(friendship.End);
    }

    [Fact]
    public void Couple_friendship_can_reactivate_after_ending()
    {
        var friendship = new CoupleFriendship(Guid.NewGuid(), Guid.NewGuid());
        friendship.End();
        friendship.Reactivate();
        Assert.Equal(CoupleFriendshipStatus.Active, friendship.Status);
        Assert.Null(friendship.EndedAt);
    }

    [Fact]
    public void Active_couple_friendship_cannot_reactivate()
    {
        var friendship = new CoupleFriendship(Guid.NewGuid(), Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(friendship.Reactivate);
    }

    [Fact]
    public void Couple_friend_message_trims_content_and_requires_image_attachment()
    {
        var message = new CoupleFriendMessage(Guid.NewGuid(), Guid.NewGuid(), "  hello  ");
        Assert.Equal("hello", message.Content);
        Assert.Throws<ArgumentException>(() =>
            new CoupleFriendMessage(Guid.NewGuid(), Guid.NewGuid(), "pic", MessageType.Image, null));
    }

    [Fact]
    public void Recommendation_can_be_revoked()
    {
        var recommendation = new Recommendation(Guid.NewGuid(), Guid.NewGuid(), null, "Trusted member");
        recommendation.Revoke();
        Assert.Equal(RecommendationStatus.Revoked, recommendation.Status);
    }

    [Fact]
    public void Completed_date_request_cannot_be_cancelled()
    {
        var request = new DateRequest(Guid.NewGuid(), "Coffee", DateTimeOffset.UtcNow.AddDays(1),
            DateTimeOffset.UtcNow.AddDays(1).AddHours(1), "Lagos", null);
        request.Cancel();
        Assert.Equal(DateRequestStatus.Cancelled, request.Status);
        Assert.Throws<InvalidOperationException>(request.Cancel);
    }

    [Fact]
    public void Single_capacity_select_matches_legacy_one_to_one_behavior()
    {
        var winner = Guid.NewGuid();
        var loser = Guid.NewGuid();
        var request = new DateRequest(Guid.NewGuid(), "Coffee", DateTimeOffset.UtcNow.AddDays(1),
            DateTimeOffset.UtcNow.AddDays(1).AddHours(1), "Lagos", null);
        request.Acceptances.Add(new DateRequestAcceptance(request.Id, winner));
        request.Acceptances.Add(new DateRequestAcceptance(request.Id, loser));

        request.Select(winner);

        Assert.Equal(DateRequestStatus.Confirmed, request.Status);
        Assert.Equal(winner, request.SelectedUserId);
        Assert.Equal(DateAcceptanceStatus.Selected, request.Acceptances.Single(x => x.AcceptorUserId == winner).Status);
        Assert.Equal(DateAcceptanceStatus.Declined, request.Acceptances.Single(x => x.AcceptorUserId == loser).Status);
    }

    [Fact]
    public void Group_gathering_confirms_once_capacity_is_filled_and_declines_the_rest()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        var request = new DateRequest(Guid.NewGuid(), "Picnic", DateTimeOffset.UtcNow.AddDays(1),
            DateTimeOffset.UtcNow.AddDays(1).AddHours(2), "Lagos", null,
            SectionCategory.Friendship, capacity: 2, itemsToBring: "Drinks");
        request.Acceptances.Add(new DateRequestAcceptance(request.Id, first));
        request.Acceptances.Add(new DateRequestAcceptance(request.Id, second));
        request.Acceptances.Add(new DateRequestAcceptance(request.Id, third));

        request.Select(first);
        Assert.Equal(DateRequestStatus.Open, request.Status);

        request.Select(second);

        Assert.Equal(DateRequestStatus.Confirmed, request.Status);
        Assert.Equal(DateAcceptanceStatus.Selected, request.Acceptances.Single(x => x.AcceptorUserId == first).Status);
        Assert.Equal(DateAcceptanceStatus.Selected, request.Acceptances.Single(x => x.AcceptorUserId == second).Status);
        Assert.Equal(DateAcceptanceStatus.Declined, request.Acceptances.Single(x => x.AcceptorUserId == third).Status);
        Assert.Throws<InvalidOperationException>(() => request.Select(third));
    }

    [Fact]
    public void Capacity_must_be_at_least_one()
    {
        Assert.Throws<ArgumentException>(() =>
            new DateRequest(Guid.NewGuid(), "Coffee", DateTimeOffset.UtcNow.AddDays(1),
                DateTimeOffset.UtcNow.AddDays(1).AddHours(1), "Lagos", null, capacity: 0));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void Profile_visit_reveals_only_the_first_ten_distinct_visitors(int ordinal, bool expected)
    {
        var visit = new ProfileVisit(Guid.NewGuid(), Guid.NewGuid(), ordinal);

        Assert.Equal(expected, visit.IsIdentityRevealed);
    }

    [Fact]
    public void Return_profile_visit_refreshes_activity_without_changing_reveal_quota()
    {
        var visit = new ProfileVisit(Guid.NewGuid(), Guid.NewGuid(), 7);
        var previousVisit = visit.LastVisitedAt;

        visit.RecordReturnVisit();

        Assert.Equal(7, visit.RevealOrdinal);
        Assert.True(visit.IsIdentityRevealed);
        Assert.True(visit.LastVisitedAt >= previousVisit);
    }

    [Theory]
    [InlineData(RelationshipStatus.Married, RelationshipStatus.Single)]
    [InlineData(RelationshipStatus.Single, RelationshipStatus.Married)]
    [InlineData(RelationshipStatus.Married, RelationshipStatus.Married)]
    public void Profile_visit_notifications_are_disabled_when_either_person_is_married(
        RelationshipStatus visitorStatus,
        RelationshipStatus profileStatus)
    {
        Assert.False(ProfileVisit.ShouldNotify(
            Sex.Male, visitorStatus, Sex.Female, profileStatus));
    }

    [Fact]
    public void Opposite_sex_unmarried_profile_visit_can_notify()
    {
        Assert.True(ProfileVisit.ShouldNotify(
            Sex.Male, RelationshipStatus.Single,
            Sex.Female, RelationshipStatus.Single));
    }

    [Fact]
    public void Same_sex_profile_visit_does_not_notify()
    {
        Assert.False(ProfileVisit.ShouldNotify(
            Sex.Female, RelationshipStatus.Single,
            Sex.Female, RelationshipStatus.Single));
    }

    [Fact]
    public void Testimonial_cannot_tag_its_author()
    {
        var authorId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() =>
            new Testimonial(authorId, "How grace found us", new string('a', 100), taggedUserId: authorId));
    }

    [Fact]
    public void Testimonial_preserves_partner_tag_and_story_content()
    {
        var partnerId = Guid.NewGuid();
        var story = new Testimonial(Guid.NewGuid(), "  How grace found us  ",
            $"  {new string('a', 100)}  ", "https://images.example/story.jpg", partnerId);

        Assert.Equal("How grace found us", story.Title);
        Assert.Equal(new string('a', 100), story.Body);
        Assert.Equal(partnerId, story.TaggedUserId);
    }

    [Fact]
    public void Testimonial_exposes_three_images_in_thumbnail_order()
    {
        var story = new Testimonial(Guid.NewGuid(), "How grace found us", new string('a', 100),
            "one.jpg", imageUrl2: "two.jpg", imageUrl3: "three.jpg");

        Assert.Equal(["one.jpg", "two.jpg", "three.jpg"], story.ImageUrls);
        Assert.Equal("one.jpg", story.ImageUrl);
    }

    [Fact]
    public void Community_post_exposes_three_images_in_thumbnail_order()
    {
        var post = new CommunityPost(Guid.NewGuid(), Guid.NewGuid(), "Our community update",
            "one.jpg", "two.jpg", "three.jpg");

        Assert.Equal(["one.jpg", "two.jpg", "three.jpg"], post.ImageUrls);
    }

    [Fact]
    public void Community_member_defaults_to_approved()
    {
        var member = new CommunityMember(Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(CommunityMemberStatus.Approved, member.Status);
    }

    [Fact]
    public void Community_member_can_be_created_pending_for_approval_gated_communities()
    {
        var member = new CommunityMember(Guid.NewGuid(), Guid.NewGuid(), status: CommunityMemberStatus.Pending);
        Assert.Equal(CommunityMemberStatus.Pending, member.Status);
    }

    [Fact]
    public void Community_member_approve_and_reject_update_status()
    {
        var member = new CommunityMember(Guid.NewGuid(), Guid.NewGuid(), status: CommunityMemberStatus.Pending);

        member.Approve();
        Assert.Equal(CommunityMemberStatus.Approved, member.Status);

        member.Reject();
        Assert.Equal(CommunityMemberStatus.Rejected, member.Status);
    }

    [Fact]
    public void Removing_a_community_member_marks_them_removed_and_left()
    {
        var member = new CommunityMember(Guid.NewGuid(), Guid.NewGuid());

        member.Remove();

        Assert.Equal(CommunityMemberStatus.Removed, member.Status);
        Assert.NotNull(member.LeftAt);
    }

    [Fact]
    public void Rejoining_a_community_resets_left_at_and_applies_the_given_status()
    {
        var member = new CommunityMember(Guid.NewGuid(), Guid.NewGuid());
        member.Leave();

        member.Rejoin(CommunityMemberStatus.Pending);

        Assert.Null(member.LeftAt);
        Assert.Equal(CommunityMemberStatus.Pending, member.Status);
    }

    [Fact]
    public void Organisation_and_community_default_to_auto_join()
    {
        var org = new Organisation(Guid.NewGuid(), "Grace Church", "Baptist", "Nigeria", "REG-1");
        var community = new Community(Guid.NewGuid(), "Grace Fellowship", "General", "A community");

        Assert.False(org.RequireApproval);
        Assert.False(community.RequireApproval);

        org.SetRequireApproval(true);
        community.SetRequireApproval(true);

        Assert.True(org.RequireApproval);
        Assert.True(community.RequireApproval);
    }
}
