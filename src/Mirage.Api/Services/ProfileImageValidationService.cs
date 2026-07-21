using Microsoft.Extensions.Logging;
using Mirage.Application.Abstractions;

namespace Mirage.Api.Services;

// Downloads a just-uploaded Cloudinary photo and runs it through face detection before its URL is
// allowed onto a profile. This has to happen server-side — the browser uploads straight to
// Cloudinary, so a client-side-only check could simply be skipped by calling the API directly.
public sealed class ProfileImageValidationService(
    HttpClient http, IFaceDetectionService faceDetection, ILogger<ProfileImageValidationService> logger)
{
    public async Task<bool> IsValidHumanPhotoAsync(string imageUrl, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await http.GetByteArrayAsync(imageUrl, cancellationToken);
            return await faceDetection.ContainsHumanFaceAsync(bytes, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not download {ImageUrl} for face validation.", imageUrl);
            return false;
        }
    }
}
