using Microsoft.Extensions.Logging;
using Mirage.Application.Abstractions;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace Mirage.Infrastructure.Vision;

// Self-hosted, free alternative to a paid moderation API: OpenCV's YuNet detector (MIT-licensed,
// bundled as an ONNX file under Vision/Models) runs entirely in-process, so there's no per-image
// cost or usage cap. FaceDetectorYN must be sized to the exact input image, and the model init is
// cheap (a 232KB graph), so a fresh detector is created per call rather than cached across sizes.
public sealed class YuNetFaceDetectionService : IFaceDetectionService
{
    private const float ScoreThreshold = 0.5f;
    private const double CosineSimilarityThreshold = 0.363;

    private readonly string _modelPath;
    private readonly string _recognitionModelPath;
    private readonly ILogger<YuNetFaceDetectionService> _logger;

    public YuNetFaceDetectionService(ILogger<YuNetFaceDetectionService> logger)
    {
        _logger = logger;
        _modelPath = Path.Combine(AppContext.BaseDirectory, "Vision", "Models", "face_detection_yunet_2023mar.onnx");
        _recognitionModelPath = Path.Combine(AppContext.BaseDirectory, "Vision", "Models", "face_recognition_sface_2021dec.onnx");
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

    public Task<FaceComparisonResult> IsSamePersonAsync(byte[] firstImageBytes, byte[] secondImageBytes,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var first = Cv2.ImDecode(firstImageBytes, ImreadModes.Color);
            using var second = Cv2.ImDecode(secondImageBytes, ImreadModes.Color);
            if (first.Empty() || second.Empty()) return Task.FromResult(FaceComparisonResult.DifferentPerson);

            using var firstFace = DetectSingleFace(first);
            using var secondFace = DetectSingleFace(second);
            if (firstFace is null || secondFace is null) return Task.FromResult(FaceComparisonResult.DifferentPerson);

            using var firstCrop = AlignFace(first, firstFace);
            using var secondCrop = AlignFace(second, secondFace);
            using var net = CvDnn.ReadNetFromOnnx(_recognitionModelPath);
            if (net is null) return Task.FromResult(FaceComparisonResult.Unavailable);
            using var firstFeature = ExtractFeature(net, firstCrop);
            using var secondFeature = ExtractFeature(net, secondCrop);
            var denominator = Cv2.Norm(firstFeature) * Cv2.Norm(secondFeature);
            if (denominator == 0) return Task.FromResult(FaceComparisonResult.DifferentPerson);
            var similarity = firstFeature.Dot(secondFeature) / denominator;
            return Task.FromResult(similarity >= CosineSimilarityThreshold
                ? FaceComparisonResult.SamePerson
                : FaceComparisonResult.DifferentPerson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Face comparison service failed.");
            return Task.FromResult(FaceComparisonResult.Unavailable);
        }
    }

    private Mat? DetectSingleFace(Mat image)
    {
        using var detector = FaceDetectorYN.Create(_modelPath, string.Empty, image.Size(), ScoreThreshold);
        using var faces = new Mat();
        detector.Detect(image, faces);
        if (faces.Rows != 1) return null;
        return faces.Row(0).Clone();
    }

    private static Mat AlignFace(Mat image, Mat face)
    {
        using var source = Mat.FromArray(
            face.At<float>(0, 4), face.At<float>(0, 5),
            face.At<float>(0, 6), face.At<float>(0, 7),
            face.At<float>(0, 8), face.At<float>(0, 9),
            face.At<float>(0, 10), face.At<float>(0, 11),
            face.At<float>(0, 12), face.At<float>(0, 13)).Reshape(1, 5);
        using var target = Mat.FromArray(
            38.2946f, 51.6963f, 73.5318f, 51.5014f, 56.0252f,
            71.7366f, 41.5493f, 92.3655f, 70.7299f, 92.2041f).Reshape(1, 5);
        using var inliers = new Mat();
        using var transform = Cv2.EstimateAffinePartial2D(source, target, inliers);
        if (transform is null || transform.Empty())
            throw new InvalidOperationException("Could not align the detected face.");
        var aligned = new Mat();
        Cv2.WarpAffine(image, aligned, transform, new Size(112, 112));
        return aligned;
    }

    private static Mat ExtractFeature(Net net, Mat face)
    {
        using var blob = CvDnn.BlobFromImage(face, 1d / 127.5d, new Size(112, 112),
            new Scalar(127.5, 127.5, 127.5), swapRB: true, crop: false);
        net.SetInput(blob);
        return net.Forward();
    }
}
