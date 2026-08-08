using Mirage.Domain.Common;

namespace Mirage.Domain.Entities;

/// <summary>
/// Public ECDH identity with legacy recovery protection and an optional AWS KMS escrow backup.
/// KMS escrow enables transparent recovery on authenticated devices without storing plaintext key material.
/// </summary>
public sealed class ChatEncryptionIdentity : Entity
{
    private ChatEncryptionIdentity() { }

    public ChatEncryptionIdentity(Guid userId, string publicKeyJwk, string encryptedPrivateKey,
        string privateKeyNonce, string recoverySalt, int kdfIterations)
    {
        UserId = userId;
        Update(publicKeyJwk, encryptedPrivateKey, privateKeyNonce, recoverySalt, kdfIterations);
    }

    public Guid UserId { get; private set; }
    public string PublicKeyJwk { get; private set; } = string.Empty;
    public string EncryptedPrivateKey { get; private set; } = string.Empty;
    public string PrivateKeyNonce { get; private set; } = string.Empty;
    public string RecoverySalt { get; private set; } = string.Empty;
    public int KdfIterations { get; private set; }
    public string? KmsEncryptedPrivateKey { get; private set; }
    public int Version { get; private set; } = 1;

    public void SetKmsEscrow(string encryptedPrivateKey)
    {
        KmsEncryptedPrivateKey = Required(encryptedPrivateKey, 8000, nameof(encryptedPrivateKey));
        Touch();
    }

    public void Update(string publicKeyJwk, string encryptedPrivateKey, string privateKeyNonce,
        string recoverySalt, int kdfIterations)
    {
        if (kdfIterations < 310_000) throw new ArgumentOutOfRangeException(nameof(kdfIterations));
        PublicKeyJwk = Required(publicKeyJwk, 4000, nameof(publicKeyJwk));
        EncryptedPrivateKey = Required(encryptedPrivateKey, 8000, nameof(encryptedPrivateKey));
        PrivateKeyNonce = Required(privateKeyNonce, 100, nameof(privateKeyNonce));
        RecoverySalt = Required(recoverySalt, 100, nameof(recoverySalt));
        KdfIterations = kdfIterations;
        Touch();
    }

    private static string Required(string value, int maxLength, string name)
    {
        value = value?.Trim() ?? string.Empty;
        if (value.Length is 0 || value.Length > maxLength) throw new ArgumentException("Invalid encrypted key material.", name);
        return value;
    }
}

/// <summary>Short-lived ECDH handoff. Payload is encrypted by a trusted device for the requester.</summary>
public sealed class ChatDeviceLink : Entity
{
    private ChatDeviceLink() { }
    public ChatDeviceLink(Guid userId, string codeHash, string requesterPublicKeyJwk, DateTimeOffset expiresAt)
    {
        UserId = userId; CodeHash = codeHash; RequesterPublicKeyJwk = requesterPublicKeyJwk;
        ExpiresAt = expiresAt;
    }
    public Guid UserId { get; private set; }
    public string CodeHash { get; private set; } = string.Empty;
    public string RequesterPublicKeyJwk { get; private set; } = string.Empty;
    public string? EncryptedPayload { get; private set; }
    public string? PayloadNonce { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? ClaimedAt { get; private set; }
    public void Complete(string payload, string nonce) { EncryptedPayload = payload; PayloadNonce = nonce; CompletedAt = DateTimeOffset.UtcNow; Touch(); }
    public void Claim() { ClaimedAt = DateTimeOffset.UtcNow; Touch(); }
}

/// <summary>A counselling session key encrypted client-side for one authorized participant.</summary>
public sealed class CounsellingKeyEnvelope : Entity
{
    private CounsellingKeyEnvelope() { }
    public CounsellingKeyEnvelope(Guid sessionId, Guid recipientUserId, Guid senderUserId,
        string ciphertext, string nonce)
    {
        SessionId = sessionId;
        RecipientUserId = recipientUserId;
        SenderUserId = senderUserId;
        Ciphertext = Required(ciphertext, 1000, nameof(ciphertext));
        Nonce = Required(nonce, 100, nameof(nonce));
    }
    public Guid SessionId { get; private set; }
    public Guid RecipientUserId { get; private set; }
    public Guid SenderUserId { get; private set; }
    public string Ciphertext { get; private set; } = string.Empty;
    public string Nonce { get; private set; } = string.Empty;
    public int Version { get; private set; } = 1;
    private static string Required(string value, int max, string name)
    {
        value = value?.Trim() ?? string.Empty;
        if (value.Length is 0 || value.Length > max) throw new ArgumentException("Invalid encrypted key envelope.", name);
        return value;
    }
}
