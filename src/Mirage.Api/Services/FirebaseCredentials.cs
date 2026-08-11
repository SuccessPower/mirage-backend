using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;

namespace Mirage.Api.Services;

// Holds the Firebase service-account credential used to authenticate against FCM's HTTP v1 API.
// Registered as a singleton because ServiceAccountCredential caches its OAuth access token
// internally (~1h) — rebuilding it per request would mean a JWT signature and a token exchange
// on every single push.
//
// Firebase:ServiceAccountJson accepts either the raw service-account JSON or a base64 encoding
// of it; the base64 form exists because the raw JSON's newlines inside "private_key" do not
// survive most hosting providers' environment-variable editors (Render included).
public sealed class FirebaseCredentials
{
    private const string MessagingScope = "https://www.googleapis.com/auth/firebase.messaging";

    private readonly ITokenAccess? _credential;
    private readonly ILogger<FirebaseCredentials> _logger;

    public FirebaseCredentials(IConfiguration configuration, ILogger<FirebaseCredentials> logger)
    {
        _logger = logger;
        var raw = configuration["Firebase:ServiceAccountJson"];
        if (string.IsNullOrWhiteSpace(raw))
        {
            _logger.LogInformation(
                "Firebase:ServiceAccountJson is not configured; push notifications are disabled.");
            return;
        }

        try
        {
            var json = Decode(raw);
            using var document = JsonDocument.Parse(json);
            ProjectId = configuration["Firebase:ProjectId"]
                        ?? document.RootElement.GetProperty("project_id").GetString();
            _credential = GoogleCredential.FromJson(json).CreateScoped(MessagingScope);
        }
        catch (Exception exception)
        {
            // A malformed credential must not take the API down — every other notification
            // channel (in-app, SignalR, email) still works without push.
            _logger.LogError(exception,
                "Firebase:ServiceAccountJson could not be parsed; push notifications are disabled.");
        }
    }

    public string? ProjectId { get; }

    public bool IsConfigured => _credential is not null && !string.IsNullOrWhiteSpace(ProjectId);

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_credential is null) return null;
        try
        {
            return await _credential.GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to obtain a Firebase access token; skipping push delivery.");
            return null;
        }
    }

    private static string Decode(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith('{')
            ? trimmed
            : Encoding.UTF8.GetString(Convert.FromBase64String(trimmed));
    }
}
