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
    private const int LandmarkCount = 5;

    // SFace's canonical 112x112 landmark template, as interleaved x/y pairs.
    private static readonly double[] AlignmentTemplate =
    [
        38.2946, 51.6963,
        73.5318, 51.5014,
        56.0252, 71.7366,
        41.5493, 92.3655,
        70.7299, 92.2041
    ];

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
            // An undecodable image is a broken/unsupported file, not evidence about who is in it.
            if (first.Empty()) return Task.FromResult(FaceComparisonResult.NoFaceInFirstPhoto);
            if (second.Empty()) return Task.FromResult(FaceComparisonResult.NoFaceInSecondPhoto);

            using var firstFace = DetectSingleFace(first, out var firstFaceCount);
            if (firstFace is null)
                return Task.FromResult(firstFaceCount == 0
                    ? FaceComparisonResult.NoFaceInFirstPhoto
                    : FaceComparisonResult.MultipleFacesInFirstPhoto);
            using var secondFace = DetectSingleFace(second, out var secondFaceCount);
            if (secondFace is null)
                return Task.FromResult(secondFaceCount == 0
                    ? FaceComparisonResult.NoFaceInSecondPhoto
                    : FaceComparisonResult.MultipleFacesInSecondPhoto);

            using var firstCrop = AlignFace(first, firstFace);
            using var secondCrop = AlignFace(second, secondFace);
            using var net = CvDnn.ReadNetFromOnnx(_recognitionModelPath);
            if (net is null) return Task.FromResult(FaceComparisonResult.Unavailable);
            using var firstFeature = ExtractFeature(net, firstCrop);
            using var secondFeature = ExtractFeature(net, secondCrop);
            var denominator = Cv2.Norm(firstFeature) * Cv2.Norm(secondFeature);
            // A zero-norm embedding means the model produced nothing usable — a failure of the
            // check, not a verdict on the two faces.
            if (denominator == 0) return Task.FromResult(FaceComparisonResult.Unavailable);
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

    // Null when there isn't exactly one subject to match; faceCount lets the caller say which of the
    // two problems it was rather than lumping both under a generic rejection.
    private Mat? DetectSingleFace(Mat image, out int faceCount)
    {
        using var detector = FaceDetectorYN.Create(_modelPath, string.Empty, image.Size(), ScoreThreshold);
        using var faces = new Mat();
        detector.Detect(image, faces);
        faceCount = faces.Rows;
        if (faces.Rows != 1) return null;
        return faces.Row(0).Clone();
    }

    private static Mat AlignFace(Mat image, Mat face)
    {
        // Closed-form least-squares similarity transform (uniform scale + rotation + translation)
        // mapping the five detected landmarks onto SFace's canonical template. Cv2.EstimateAffinePartial2D
        // computes the same thing, but its RANSAC pass over only five points asserts inside native
        // OpenCV on ordinary photos, which surfaced to users as "verification temporarily unavailable".
        // Solving it directly is deterministic, allocation-free and can only fail on degenerate points.
        double sourceMeanX = 0, sourceMeanY = 0, targetMeanX = 0, targetMeanY = 0;
        Span<double> sourceX = stackalloc double[LandmarkCount];
        Span<double> sourceY = stackalloc double[LandmarkCount];
        for (var i = 0; i < LandmarkCount; i++)
        {
            // YuNet emits the landmarks (right eye, left eye, nose tip, right and left mouth corner)
            // as interleaved x/y pairs in columns 4..13 of the detection row.
            sourceX[i] = face.At<float>(0, 4 + i * 2);
            sourceY[i] = face.At<float>(0, 5 + i * 2);
            sourceMeanX += sourceX[i];
            sourceMeanY += sourceY[i];
            targetMeanX += AlignmentTemplate[i * 2];
            targetMeanY += AlignmentTemplate[i * 2 + 1];
        }
        sourceMeanX /= LandmarkCount;
        sourceMeanY /= LandmarkCount;
        targetMeanX /= LandmarkCount;
        targetMeanY /= LandmarkCount;

        double dot = 0, cross = 0, sourceVariance = 0;
        for (var i = 0; i < LandmarkCount; i++)
        {
            var sx = sourceX[i] - sourceMeanX;
            var sy = sourceY[i] - sourceMeanY;
            var tx = AlignmentTemplate[i * 2] - targetMeanX;
            var ty = AlignmentTemplate[i * 2 + 1] - targetMeanY;
            dot += sx * tx + sy * ty;
            cross += sx * ty - sy * tx;
            sourceVariance += sx * sx + sy * sy;
        }
        if (sourceVariance <= double.Epsilon || !double.IsFinite(dot) || !double.IsFinite(cross))
            throw new InvalidOperationException("Could not align the detected face.");

        var scaledCos = dot / sourceVariance;
        var scaledSin = cross / sourceVariance;
        using var transform = new Mat(2, 3, MatType.CV_64FC1);
        transform.SetArray(
            scaledCos, -scaledSin, targetMeanX - (scaledCos * sourceMeanX - scaledSin * sourceMeanY),
            scaledSin, scaledCos, targetMeanY - (scaledSin * sourceMeanX + scaledCos * sourceMeanY));
        var aligned = new Mat();
        Cv2.WarpAffine(image, aligned, transform, new Size(112, 112));
        return aligned;
    }

    private static Mat ExtractFeature(Net net, Mat face)
    {
        // SFace was trained through OpenCV's own FaceRecognizerSF::feature, which feeds the aligned
        // 112x112 crop in as-is: raw 0-255 BGR, no scaling and no mean subtraction. Normalising to
        // [-1,1] or swapping to RGB here produces embeddings off the manifold the model learned, so
        // cosine distances between two photos of the same person land well below the 0.363 threshold
        // that OpenCV publishes for this model — i.e. it rejects the same face.
        using var blob = CvDnn.BlobFromImage(face, 1d, new Size(112, 112),
            new Scalar(0, 0, 0), swapRB: false, crop: false);
        net.SetInput(blob);
        return net.Forward().Clone();
    }
}
