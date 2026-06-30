using CloudinaryDotNet;
using Mirage.Api.Contracts;
using Mirage.Api.Security;

namespace Mirage.Api.Endpoints;

internal static class UploadEndpoints
{
    public static RouteGroupBuilder MapUploadEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/upload").WithTags("Upload").RequireAuthorization();
        group.MapPost("/sign", Sign);
        return api;
    }

    private static IResult Sign(HttpContext context, IConfiguration configuration)
    {
        var cloudName = configuration["Cloudinary:CloudName"]!;
        var apiKey = configuration["Cloudinary:ApiKey"]!;
        var apiSecret = configuration["Cloudinary:ApiSecret"]!;
        var userId = context.User.GetUserId();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var folder = $"mirage/avatars/{userId}";
        var paramsToSign = new SortedDictionary<string, object>
            { { "folder", folder }, { "timestamp", timestamp } };
        var cloudinary = new Cloudinary(new Account(cloudName, apiKey, apiSecret));
        var signature = cloudinary.Api.SignParameters(paramsToSign);
        return ApiResults.Ok(context, new { cloudName, apiKey, timestamp, signature, folder }, "Upload signature generated.");
    }
}
