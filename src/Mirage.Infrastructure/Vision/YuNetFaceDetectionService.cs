using Microsoft.Extensions.Logging;
using Mirage.Application.Abstractions;
using OpenCvSharp;

namespace Mirage.Infrastructure.Vision;

// Self-hosted, free alternative to a paid moderation API: OpenCV's YuNet detector (MIT-licensed,
// bundled as an ONNX file under Vision/Models) runs entirely in-process, so there's no per-image
// cost or usage cap. FaceDetectorYN must be sized to the exact input image, and the model init is
// cheap (a 232KB graph), so a fresh detector is created per call rather than cached across sizes.
public sealed class YuNetFaceDetectionService : IFaceDetectionService
{
    private const float ScoreThreshold = 0.7f;

    private readonly string _modelPath;
    private readonly ILogger<YuNetFaceDetectionService> _logger;

    public YuNetFaceDetectionService(ILogger<YuNetFaceDetectionService> logger)
    {
        _logger = logger;
        _modelPath = Path.Combine(AppContext.BaseDirectory, "Vision", "Models", "face_detection_yunet_2023mar.onnx");
    }

    public Task<FaceDetectionResult> ContainsHumanFaceAsync(byte[] imageBytes, CancellationToken cancellationToken = default)
    {
        try
        {
            using var image = Cv2.ImDecode(imageBytes, ImreadModes.Color);
            if (image.Empty()) return Task.FromResult(FaceDetectionResult.NotDetected);

            using var detector = FaceDetectorYN.Create(_modelPath, string.Empty, image.Size(), ScoreThreshold);
            using var faces = new Mat();
            detector.Detect(image, faces);
            return Task.FromResult(faces.Rows > 0 ? FaceDetectionResult.Detected : FaceDetectionResult.NotDetected);
        }
        catch (Exception ex)
        {
            // A detector/runtime failure (e.g. the native OpenCV model failing to load) is not
            // the same as "no face in this photo" — surfaced separately from NotDetected so callers
            // can log it distinctly, though it's still treated as a rejection since this should
            // never happen in a healthy deployment.
            _logger.LogError(ex, "Face detection service failed.");
            return Task.FromResult(FaceDetectionResult.Unavailable);
        }
    }
}
