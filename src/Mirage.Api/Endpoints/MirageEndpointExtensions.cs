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
        return endpoints;
    }
}
