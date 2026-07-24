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
            // Phone photos are frequently stored with the pixel buffer in its raw sensor orientation
            // plus an EXIF Orientation tag telling viewers how to rotate it. OpenCV's decoder ignores
            // that tag, so an upright selfie can look sideways/upside-down to the detector. Cloudinary's
            // a_auto transform bakes the EXIF rotation into the pixels before we download them.
            var bytes = await http.GetByteArrayAsync(WithAutoRotation(imageUrl), cancellationToken);
            var result = await faceDetection.ContainsHumanFaceAsync(bytes, cancellationToken);
            if (result is FaceDetectionResult.Unavailable)
                logger.LogError("Face detection was unavailable for {ImageUrl}; rejecting the photo.", imageUrl);
            return result is FaceDetectionResult.Detected;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not download {ImageUrl} for face validation; rejecting the photo.", imageUrl);
            return false;
        }
    }

    private static string WithAutoRotation(string imageUrl)
    {
        const string marker = "/upload/";
        var index = imageUrl.IndexOf(marker, StringComparison.Ordinal);
        return index < 0 ? imageUrl : imageUrl.Insert(index + marker.Length, "a_auto/");
    }
}
