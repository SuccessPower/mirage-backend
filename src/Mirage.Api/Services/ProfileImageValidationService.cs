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
            var result = await faceDetection.ContainsHumanFaceAsync(bytes, cancellationToken);
            if (result is FaceDetectionResult.Unavailable)
                logger.LogError("Face detection was unavailable for {ImageUrl}; allowing the photo through unchecked.", imageUrl);
            return result is FaceDetectionResult.Detected or FaceDetectionResult.Unavailable;
        }
        catch (Exception ex)
        {
            // Couldn't even download the image to check it — same fail-open reasoning as above:
            // a transient network/CDN hiccup on our side shouldn't reject the user's photo.
            logger.LogError(ex, "Could not download {ImageUrl} for face validation; allowing the photo through unchecked.", imageUrl);
            return true;
        }
    }
}
