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

// A comparison can fail for reasons that have nothing to do with the two people being different,
// and telling someone their own face isn't theirs is the worst way to get it wrong. Each photo can
// individually be unusable (no detectable face, or several faces so there's no single subject to
// match), and which of the two failed decides who is asked to fix what — blaming a new upload for a
// years-old photo that no longer analyses cleanly just leaves the member stuck.
public enum FaceComparisonResult
{
    SamePerson,
    DifferentPerson,
    Unavailable,
    NoFaceInFirstPhoto,
    MultipleFacesInFirstPhoto,
    NoFaceInSecondPhoto,
    MultipleFacesInSecondPhoto
}

// Registration/profile-photo gate: rejects uploads (cartoons, screenshots, blank images) that
// don't show a real human face, so members can't sign up hiding behind a caricature.
public interface IFaceDetectionService
{
    Task<FaceDetectionResult> ContainsHumanFaceAsync(byte[] imageBytes, CancellationToken cancellationToken = default);
    Task<FaceComparisonResult> IsSamePersonAsync(byte[] firstImageBytes, byte[] secondImageBytes,
        CancellationToken cancellationToken = default);
}
