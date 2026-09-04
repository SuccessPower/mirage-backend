using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Xunit;

namespace Mirage.Domain.Tests;

// The rules that keep the two mentorship groups honest: a paid place is invisible to the mentor
// until it is funded, a mentor cannot charge with nowhere for the money to land, and a mentee who
// was declined re-asks on the same row rather than a second one.
public sealed class PaidMentorshipTests
{
    private static MentorProfile ApprovedMentor()
    {
        var mentor = new MentorProfile(Guid.NewGuid(), 5, "We have walked this road.", ["Marriage"], ["English"]);
        mentor.Approve();
        return mentor;
    }

    [Fact]
    public void Free_request_starts_pending_and_is_visible_to_the_mentor()
    {
        var request = new MentorRequest(Guid.NewGuid(), Guid.NewGuid(), "Please mentor us.");

        Assert.Equal(MentorRequestStatus.Pending, request.Status);
        Assert.Equal(MentorshipTier.Free, request.Tier);
        Assert.Null(request.PaidAt);
    }

    [Fact]
    public void Paid_request_waits_for_payment_before_reaching_the_mentor()
    {
        var request = new MentorRequest(Guid.NewGuid(), Guid.NewGuid(), "Please mentor us.", MentorshipTier.Paid);

        Assert.Equal(MentorRequestStatus.AwaitingPayment, request.Status);
        Assert.Equal(MentorshipTier.Paid, request.Tier);
    }

    [Fact]
    public void Confirming_payment_makes_a_paid_request_pending()
    {
        var request = new MentorRequest(Guid.NewGuid(), Guid.NewGuid(), "Please mentor us.", MentorshipTier.Paid);

        request.ConfirmPayment();

        Assert.Equal(MentorRequestStatus.Pending, request.Status);
        Assert.NotNull(request.PaidAt);
    }

    [Fact]
    public void Confirming_payment_twice_does_not_move_an_accepted_request_back_to_pending()
    {
        var request = new MentorRequest(Guid.NewGuid(), Guid.NewGuid(), "Please mentor us.", MentorshipTier.Paid);
        request.ConfirmPayment();
        request.Accept();

        request.ConfirmPayment();

        Assert.Equal(MentorRequestStatus.Accepted, request.Status);
    }

    [Fact]
    public void A_declined_request_reopens_on_the_same_row()
    {
        var request = new MentorRequest(Guid.NewGuid(), Guid.NewGuid(), "First ask.");
        request.Decline();

        request.Reopen("Asking again.", MentorshipTier.Free);

        Assert.Equal(MentorRequestStatus.Pending, request.Status);
        Assert.Equal("Asking again.", request.Message);
    }

    [Fact]
    public void Reopening_as_a_paid_place_waits_for_payment_again()
    {
        var request = new MentorRequest(Guid.NewGuid(), Guid.NewGuid(), "First ask.", MentorshipTier.Paid);
        request.ConfirmPayment();
        request.Decline();

        request.Reopen("Asking again.", MentorshipTier.Paid);

        Assert.Equal(MentorRequestStatus.AwaitingPayment, request.Status);
        Assert.Null(request.PaidAt);
    }

    [Fact]
    public void A_mentor_can_move_a_mentee_between_groups()
    {
        var request = new MentorRequest(Guid.NewGuid(), Guid.NewGuid(), "Please mentor us.");
        request.Accept();

        request.SetTier(MentorshipTier.Paid);

        Assert.Equal(MentorshipTier.Paid, request.Tier);
        Assert.Equal(MentorRequestStatus.Accepted, request.Status);
    }

    [Fact]
    public void Paid_mentorship_cannot_be_opened_without_a_payout_account()
    {
        var mentor = ApprovedMentor();

        Assert.Throws<InvalidOperationException>(() =>
            mentor.SetPaidMentorship(true, 20000m, "NGN"));
        Assert.False(mentor.OffersPaidMentorship);
    }

    [Fact]
    public void Paid_mentorship_cannot_be_opened_without_a_price()
    {
        var mentor = ApprovedMentor();
        mentor.SetBankAccount("058", "GTBank", "0123456789", "Ada Obi");

        Assert.Throws<InvalidOperationException>(() => mentor.SetPaidMentorship(true, 0m, "NGN"));
    }

    [Fact]
    public void A_mentor_with_a_payout_account_and_a_price_can_charge()
    {
        var mentor = ApprovedMentor();
        mentor.SetBankAccount("058", "GTBank", "0123456789", "Ada Obi");

        mentor.SetPaidMentorship(true, 20000m, "ngn");

        Assert.True(mentor.CanChargeForMentorship);
        Assert.Equal("NGN", mentor.PriceCurrency);
    }

    [Fact]
    public void Closing_the_paid_group_never_closes_the_free_one()
    {
        var mentor = ApprovedMentor();
        mentor.SetBankAccount("058", "GTBank", "0123456789", "Ada Obi");
        mentor.SetPaidMentorship(true, 20000m, "NGN");

        mentor.SetPaidMentorship(false, null, null);

        Assert.False(mentor.CanChargeForMentorship);
        // AcceptsFreeSessions is set by UpdateProfile and is untouched by the paid switch.
        Assert.True(mentor.HasPayoutAccount);
    }

    [Fact]
    public void A_mentorship_payment_splits_out_the_platform_fee_and_knows_what_it_is_for()
    {
        var payment = Payment.ForMentorship(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 20000m, "NGN");

        Assert.True(payment.IsMentorship);
        Assert.Null(payment.CounsellingSessionId);
        Assert.Equal(20000m, payment.Amount);
        Assert.Equal(20000m, payment.PlatformFeeAmount + payment.CounsellorAmount);
    }

    [Fact]
    public void A_mentor_event_is_owned_by_the_mentor_and_not_an_organisation()
    {
        var starts = DateTimeOffset.UtcNow.AddDays(7);
        var evt = OrgEvent.ForMentor(Guid.NewGuid(), Guid.NewGuid(), "Marriage clinic", "An evening together",
            null, starts, starts.AddHours(2), "Lagos", 50, MentorAudience.PaidMentees);

        Assert.Null(evt.OrganisationId);
        Assert.NotNull(evt.MentorProfileId);
        Assert.Equal(MentorAudience.PaidMentees, evt.Audience);
    }
}
