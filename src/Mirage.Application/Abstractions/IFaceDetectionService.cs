namespace Mirage.Application.Abstractions;

// Registration/profile-photo gate: rejects uploads (cartoons, screenshots, blank images) that
// don't show a real human face, so members can't sign up hiding behind a caricature.
public interface IFaceDetectionService
{
    Task<bool> ContainsHumanFaceAsync(byte[] imageBytes, CancellationToken cancellationToken = default);
}
