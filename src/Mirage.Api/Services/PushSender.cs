using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Mirage.Api.Services;

// What to deliver. Data values are strings because FCM's data payload is string-to-string only;
// the mobile client reads `type`/`referenceId`/`referenceType`/`route` out of it to deep-link.
public sealed record PushPayload(
    string Title,
    string Body,
    IReadOnlyDictionary<string, string> Data,
    int? Badge = null);

// Talks to FCM's HTTP v1 API (the legacy /fcm/send endpoint was retired in 2024). One request per
// token: v1 has no multicast endpoint, and the batch endpoint it replaced is also gone.
public sealed class PushSender(HttpClient http, FirebaseCredentials credentials, ILogger<PushSender> logger)
{
    // Must match the channel PushNotificationService creates on the Flutter side, otherwise
    // Android drops the notification to default (silent) importance.
    private const string AndroidChannelId = "mirage_activity";

    public bool IsEnabled => credentials.IsConfigured;

    // Returns the tokens FCM rejected as permanently dead, for the caller to revoke. Transient
    // failures (5xx, quota, network) are logged and NOT returned — the token is still good and
    // the next notification will retry naturally.
    public async Task<IReadOnlyCollection<string>> SendAsync(IReadOnlyCollection<string> tokens,
        PushPayload payload, CancellationToken cancellationToken)
    {
        if (tokens.Count == 0 || !credentials.IsConfigured) return [];

        var accessToken = await credentials.GetAccessTokenAsync(cancellationToken);
        if (accessToken is null) return [];

        var endpoint = $"https://fcm.googleapis.com/v1/projects/{credentials.ProjectId}/messages:send";
        var dead = new List<string>();

        foreach (var token in tokens)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = JsonContent.Create(new { message = BuildMessage(token, payload) })
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                using var response = await http.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode) continue;

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (IsTokenDead(response.StatusCode, body))
                {
                    logger.LogInformation("FCM reports a dead device token; revoking it.");
                    dead.Add(token);
                }
                else
                {
                    logger.LogWarning("FCM rejected a push with {Status}: {Body}", response.StatusCode, body);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Never let a push failure surface to the user — the notification itself is
                // already persisted and delivered in-app by this point.
                logger.LogWarning(exception, "Failed to deliver a push notification.");
            }
        }

        return dead;
    }

    private static object BuildMessage(string token, PushPayload payload) => new
    {
        token,
        notification = new { title = payload.Title, body = payload.Body },
        data = payload.Data,
        android = new
        {
            priority = "high",
            notification = new { channel_id = AndroidChannelId, sound = "default" }
        },
        apns = new
        {
            headers = new Dictionary<string, string> { ["apns-priority"] = "10" },
            payload = new
            {
                aps = new
                {
                    sound = "default",
                    badge = payload.Badge,
                    // Lets iOS wake the app for onBackgroundMessage so the badge/inbox stay in
                    // sync even when the notification isn't tapped.
                    content_available = true
                }
            }
        }
    };

    // FCM signals a permanently unusable token two ways: 404 NOT_FOUND with UNREGISTERED (app
    // uninstalled or token rotated) and 400 INVALID_ARGUMENT (malformed token). Everything else —
    // 401, 403, 429, 5xx — is about us or about capacity, not the token.
    private static bool IsTokenDead(HttpStatusCode status, string body)
    {
        if (status == HttpStatusCode.NotFound) return true;
        if (status != HttpStatusCode.BadRequest) return false;

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error", out var error)
                   && error.TryGetProperty("status", out var errorStatus)
                   && errorStatus.GetString() == "INVALID_ARGUMENT";
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
