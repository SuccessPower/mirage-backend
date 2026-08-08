using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;

namespace Mirage.Api.Endpoints;

internal static class ChatEncryptionEndpoints
{
    public static RouteGroupBuilder MapChatEncryptionEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/chat-encryption").WithTags("Chat encryption").RequireAuthorization();
        group.MapGet("/identity", GetIdentity);
        group.MapPut("/identity", PutIdentity);
        group.MapGet("/matches/{matchId:guid}/peer-key", GetPeerKey);
        group.MapPost("/device-links", CreateDeviceLink).RequireRateLimiting("device-link");
        group.MapGet("/device-links/pending", ListPendingLinks);
        group.MapGet("/device-links/{id:guid}", GetDeviceLink);
        group.MapPut("/device-links/{id:guid}/complete", CompleteDeviceLink).RequireRateLimiting("device-link");
        group.MapPost("/device-links/{id:guid}/claim", ClaimDeviceLink);
        return group;
    }

    private static async Task<IResult> GetIdentity(HttpContext context, IMirageDbContext db, CancellationToken ct)
    {
        var userId = context.User.GetUserId();
        var identity = await db.ChatEncryptionIdentities.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        return identity is null
            ? Results.NotFound()
            : ApiResults.Ok(context, IdentityResponse(identity), "Encryption identity retrieved.");
    }

    private static async Task<IResult> PutIdentity(UpsertEncryptionIdentity request, HttpContext context,
        IMirageDbContext db, CancellationToken ct)
    {
        if (request.KdfIterations < 310_000)
            return EndpointHelpers.ValidationProblem(context, ("kdfIterations", "Recovery protection is too weak."));
        if (!IsP256PublicJwk(request.PublicKeyJwk))
            return EndpointHelpers.ValidationProblem(context, ("publicKeyJwk", "A valid P-256 public key is required."));
        if (!IsBase64(request.EncryptedPrivateKey) || !IsBase64(request.PrivateKeyNonce) || !IsBase64(request.RecoverySalt))
            return EndpointHelpers.ValidationProblem(context, ("identity", "Encrypted identity fields must use valid base64 encoding."));
        var userId = context.User.GetUserId();
        var identity = await db.ChatEncryptionIdentities.SingleOrDefaultAsync(x => x.UserId == userId, ct);
        try
        {
            if (identity is null)
            {
                identity = new ChatEncryptionIdentity(userId, request.PublicKeyJwk, request.EncryptedPrivateKey,
                    request.PrivateKeyNonce, request.RecoverySalt, request.KdfIterations);
                db.ChatEncryptionIdentities.Add(identity);
            }
            else if (!CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(identity.PublicKeyJwk)),
                SHA256.HashData(Encoding.UTF8.GetBytes(request.PublicKeyJwk.Trim()))))
            {
                return EndpointHelpers.Conflict(context,
                    "The encryption identity cannot be replaced from a signed-in session. Use trusted-device linking or recovery.");
            }
            else if (identity.EncryptedPrivateKey != request.EncryptedPrivateKey.Trim()
                || identity.PrivateKeyNonce != request.PrivateKeyNonce.Trim()
                || identity.RecoverySalt != request.RecoverySalt.Trim()
                || identity.KdfIterations != request.KdfIterations)
            {
                return EndpointHelpers.Conflict(context,
                    "The recovery backup cannot be replaced from a signed-in session. Unlock the existing identity instead.");
            }
        }
        catch (ArgumentException exception) { return EndpointHelpers.ValidationProblem(context, ("identity", exception.Message)); }
        await db.SaveChangesAsync(ct);
        return ApiResults.Ok(context, IdentityResponse(identity), "Encryption identity saved.");
    }

    private static async Task<IResult> GetPeerKey(Guid matchId, HttpContext context, IMirageDbContext db,
        CancellationToken ct)
    {
        var userId = context.User.GetUserId();
        var match = await db.Matches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == matchId
            && (x.User1Id == userId || x.User2Id == userId), ct);
        if (match is null) return EndpointHelpers.NotFound(context, "Match was not found.");
        var peerId = match.User1Id == userId ? match.User2Id : match.User1Id;
        var key = await db.ChatEncryptionIdentities.AsNoTracking().Where(x => x.UserId == peerId)
            .Select(x => new { x.UserId, x.PublicKeyJwk, x.Version }).SingleOrDefaultAsync(ct);
        return key is null ? Results.NotFound() : ApiResults.Ok(context, key, "Peer encryption key retrieved.");
    }

    private static async Task<IResult> CreateDeviceLink(CreateDeviceLinkRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RequesterPublicKeyJwk) || request.RequesterPublicKeyJwk.Length > 4000
            || !IsP256PublicJwk(request.RequesterPublicKeyJwk))
            return EndpointHelpers.ValidationProblem(context, ("requesterPublicKeyJwk", "A valid device public key is required."));
        var rawCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
        var link = new ChatDeviceLink(context.User.GetUserId(), Hash(rawCode), request.RequesterPublicKeyJwk.Trim(),
            DateTimeOffset.UtcNow.AddMinutes(10));
        db.ChatDeviceLinks.Add(link);
        await db.SaveChangesAsync(ct);
        return ApiResults.Created(context, $"/api/v1/chat-encryption/device-links/{link.Id}",
            new { link.Id, Code = rawCode, link.ExpiresAt }, "Device link created.");
    }

    private static async Task<IResult> ListPendingLinks(HttpContext context, IMirageDbContext db, CancellationToken ct)
    {
        var userId = context.User.GetUserId(); var now = DateTimeOffset.UtcNow;
        var links = await db.ChatDeviceLinks.AsNoTracking().Where(x => x.UserId == userId && x.ExpiresAt > now
            && x.CompletedAt == null && x.ClaimedAt == null)
            .Select(x => new { x.Id, x.RequesterPublicKeyJwk, x.ExpiresAt }).ToListAsync(ct);
        return ApiResults.Ok(context, links, "Pending device links retrieved.");
    }

    private static async Task<IResult> GetDeviceLink(Guid id, HttpContext context, IMirageDbContext db, CancellationToken ct)
    {
        var userId = context.User.GetUserId();
        var link = await db.ChatDeviceLinks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (link is null || link.ExpiresAt <= DateTimeOffset.UtcNow) return EndpointHelpers.NotFound(context, "Device link expired or was not found.");
        return ApiResults.Ok(context, new { link.Id, link.EncryptedPayload, link.PayloadNonce, link.CompletedAt, link.ExpiresAt }, "Device link retrieved.");
    }

    private static async Task<IResult> CompleteDeviceLink(Guid id, CompleteDeviceLinkRequest request,
        HttpContext context, IMirageDbContext db, CancellationToken ct)
    {
        var link = await db.ChatDeviceLinks.SingleOrDefaultAsync(x => x.Id == id && x.UserId == context.User.GetUserId(), ct);
        if (link is null || link.ExpiresAt <= DateTimeOffset.UtcNow || link.CompletedAt != null)
            return EndpointHelpers.Conflict(context, "Device link is no longer available.");
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length != 8
            || string.IsNullOrWhiteSpace(request.EncryptedPayload) || request.EncryptedPayload.Length > 8000
            || string.IsNullOrWhiteSpace(request.PayloadNonce) || request.PayloadNonce.Length > 100
            || !IsBase64(request.EncryptedPayload) || !IsBase64(request.PayloadNonce))
            return EndpointHelpers.ValidationProblem(context, ("deviceLink", "Invalid encrypted device-link payload."));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(link.CodeHash), Encoding.ASCII.GetBytes(Hash(request.Code))))
            return EndpointHelpers.Forbidden(context, "The device-link code is invalid.");
        link.Complete(request.EncryptedPayload, request.PayloadNonce);
        await db.SaveChangesAsync(ct);
        return ApiResults.Ok(context, new { link.Id }, "Device link approved.");
    }

    private static async Task<IResult> ClaimDeviceLink(Guid id, HttpContext context, IMirageDbContext db, CancellationToken ct)
    {
        var link = await db.ChatDeviceLinks.SingleOrDefaultAsync(x => x.Id == id && x.UserId == context.User.GetUserId(), ct);
        if (link is null || link.CompletedAt == null || link.ClaimedAt != null || link.ExpiresAt <= DateTimeOffset.UtcNow)
            return EndpointHelpers.Conflict(context, "Device link cannot be claimed.");
        link.Claim(); await db.SaveChangesAsync(ct);
        return ApiResults.Ok(context, new { link.Id }, "Device link claimed.");
    }

    private static object IdentityResponse(ChatEncryptionIdentity x) => new
    { x.PublicKeyJwk, x.EncryptedPrivateKey, x.PrivateKeyNonce, x.RecoverySalt, x.KdfIterations, x.Version };
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToUpperInvariant())));
    private static bool IsBase64(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        Span<byte> buffer = stackalloc byte[Math.Min(value.Length, 8192)];
        return Convert.TryFromBase64String(value, buffer, out _);
    }
    private static bool IsP256PublicJwk(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("kty", out var kty) && kty.GetString() == "EC"
                && root.TryGetProperty("crv", out var curve) && curve.GetString() == "P-256"
                && root.TryGetProperty("x", out var x) && !string.IsNullOrWhiteSpace(x.GetString())
                && root.TryGetProperty("y", out var y) && !string.IsNullOrWhiteSpace(y.GetString())
                && !root.TryGetProperty("d", out _);
        }
        catch (JsonException) { return false; }
    }
}

public sealed record UpsertEncryptionIdentity(string PublicKeyJwk, string EncryptedPrivateKey,
    string PrivateKeyNonce, string RecoverySalt, int KdfIterations);
public sealed record CreateDeviceLinkRequest(string RequesterPublicKeyJwk);
public sealed record CompleteDeviceLinkRequest(string Code, string EncryptedPayload, string PayloadNonce);
