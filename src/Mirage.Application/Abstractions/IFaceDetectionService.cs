namespace Mirage.Application.Abstractions;

// Distinguishes "we looked and found no face" (a real rejection) from "we couldn't run the
// check" (a detector/infra failure) — the latter must never be treated as the former, or a
// broken detector silently blocks every single upload and blames the user's photo for it.
public enum FaceDetectionResult
{
    Detected,
    NotDetected,
    Unavailable
}

// Registration/profile-photo gate: rejects uploads (cartoons, screenshots, blank images) that
// don't show a real human face, so members can't sign up hiding behind a caricature.
public interface IFaceDetectionService
{
    Task<FaceDetectionResult> ContainsHumanFaceAsync(byte[] imageBytes, CancellationToken cancellationToken = default);
}
