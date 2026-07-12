using CloudinaryDotNet;
using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Application.Abstractions;
using Mirage.Domain.Enums;

namespace Mirage.Api.Endpoints;

internal static class UploadEndpoints
{
    public static RouteGroupBuilder MapUploadEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/upload").WithTags("Upload").RequireAuthorization();
        group.MapPost("/sign", Sign);
        return api;
    }

    private static async Task<IResult> Sign(HttpContext context, IConfiguration configuration, IMirageDbContext db,
        string? uploadContext, Guid? matchId, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();

        string folder;
        if (uploadContext == "chat")
        {
            if (matchId is null)
                return EndpointHelpers.ValidationProblem(context, ("matchId", "matchId is required for chat uploads."));
            var inMatch = await db.Matches.AsNoTracking().AnyAsync(x => x.Id == matchId
                && (x.User1Id == userId || x.User2Id == userId) && x.Status == MatchStatus.Active, cancellationToken);
            if (!inMatch) return EndpointHelpers.Forbidden(context);
            folder = $"mirage/chat/{matchId}";
        }
        else if (uploadContext == "community-avatar")
        {
            folder = $"mirage/community-avatars/{userId}";
        }
        else if (uploadContext == "community-post")
        {
            folder = $"mirage/community-posts/{userId}";
        }
        else if (uploadContext == "counsellor-verification")
        {
            folder = $"mirage/counsellor-verification/{userId}";
        }
        else if (uploadContext == "profile-photo")
        {
            folder = $"mirage/profile-photos/{userId}";
        }
        else
        {
            folder = $"mirage/avatars/{userId}";
        }

        var cloudName = configuration["Cloudinary:CloudName"]!;
        var apiKey = configuration["Cloudinary:ApiKey"]!;
        var apiSecret = configuration["Cloudinary:ApiSecret"]!;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var paramsToSign = new SortedDictionary<string, object>
            { { "folder", folder }, { "timestamp", timestamp } };
        var cloudinary = new Cloudinary(new Account(cloudName, apiKey, apiSecret));
        var signature = cloudinary.Api.SignParameters(paramsToSign);
        return ApiResults.Ok(context, new { cloudName, apiKey, timestamp, signature, folder }, "Upload signature generated.");
    }
}
