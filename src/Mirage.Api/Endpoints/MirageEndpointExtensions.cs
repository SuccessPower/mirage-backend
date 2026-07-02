namespace Mirage.Api.Endpoints;

public static class MirageEndpointExtensions
{
    public static IEndpointRouteBuilder MapMirageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1");
        api.MapAuthEndpoints();
        api.MapProfileEndpoints();
        api.MapOrganisationEndpoints();
        api.MapMatchingEndpoints();
        api.MapDateRequestEndpoints();
        api.MapCounsellingEndpoints();
        api.MapMentorEndpoints();
        api.MapCoupleEndpoints();
        api.MapCalendarEndpoints();
        api.MapNotificationEndpoints();
        api.MapMilestoneEndpoints();
        api.MapUploadEndpoints();
        api.MapAdminEndpoints();
        return endpoints;
    }
}
