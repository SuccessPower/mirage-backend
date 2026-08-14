namespace Mirage.Api.Endpoints;

public static class MirageEndpointExtensions
{
    public static IEndpointRouteBuilder MapMirageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1");
        api.MapPublicEndpoints();
        api.MapContactEndpoints();
        api.MapSearchEndpoints();
        api.MapAuthEndpoints();
        api.MapProfileEndpoints();
        api.MapOrganisationEndpoints();
        api.MapEventEndpoints();
        api.MapCommunityEndpoints();
        api.MapHearthEndpoints();
        api.MapTestimonialEndpoints();
        api.MapCelebrationEndpoints();
        api.MapMatchingEndpoints();
        api.MapChatEncryptionEndpoints();
        api.MapDateRequestEndpoints();
        api.MapGatheringInviteEndpoints();
        api.MapCounsellingEndpoints();
        api.MapMentorEndpoints();
        api.MapCoupleEndpoints();
        api.MapCoupleFriendshipEndpoints();
        api.MapCalendarEndpoints();
        api.MapNotificationEndpoints();
        api.MapMilestoneEndpoints();
        api.MapUploadEndpoints();
        api.MapGifEndpoints();
        api.MapPaymentEndpoints();
        api.MapPricingEndpoints();
        api.MapAdminEndpoints();
        api.MapAdminAnalyticsEndpoints();
        api.MapVendorEndpoints();
        api.MapCompanionEndpoints();
        api.MapNewsletterEndpoints();
        return endpoints;
    }
}
