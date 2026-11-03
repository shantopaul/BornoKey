using System.Security.Cryptography;
using System.Text;

namespace BengaliKeyboard.Storage.Security;

/// <summary>
/// Provides Windows DPAPI (CurrentUser scope) encryption and keyed HMAC-SHA256 hashing
/// for protecting sensitive local data such as ClipboardHistory at rest (TM-01, SEC-01, SEC-02, CHK-01).
/// </summary>
public static class LocalDataProtector
{
    private static readonly byte[] ApplicationEntropy =
        "BornoKey.LocalDataProtector.v1"u8.ToArray();

    /// <summary>
    /// Encrypts plaintext UTF-8 text into a Base64-encoded DPAPI blob tied to the current Windows user.
    /// </summary>
    public static string ProtectString(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        if (plaintext.Length == 0)
        {
            return string.Empty;
        }

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = ProtectedData.Protect(
            plainBytes,
            ApplicationEntropy,
            DataProtectionScope.CurrentUser);

        return Convert.ToBase64String(cipherBytes);
    }

    /// <summary>
    /// Decrypts a Base64-encoded DPAPI blob back into plaintext UTF-8 text.
    /// </summary>
    public static string UnprotectString(string protectedBase64)
    {
        ArgumentNullException.ThrowIfNull(protectedBase64);
        if (protectedBase64.Length == 0)
        {
            return string.Empty;
        }

        var cipherBytes = Convert.FromBase64String(protectedBase64);
        var plainBytes = ProtectedData.Unprotect(
            cipherBytes,
            ApplicationEntropy,
            DataProtectionScope.CurrentUser);

        return Encoding.UTF8.GetString(plainBytes);
    }

    /// <summary>
    /// Generates a cryptographically random 256-bit HMAC key and returns it encrypted via DPAPI.
    /// </summary>
    public static string GenerateProtectedHmacKey()
    {
        Span<byte> rawKey = stackalloc byte[32];
        RandomNumberGenerator.Fill(rawKey);

        var protectedKey = ProtectedData.Protect(
            rawKey.ToArray(),
            ApplicationEntropy,
            DataProtectionScope.CurrentUser);

        return Convert.ToBase64String(protectedKey);
    }

    /// <summary>
    /// Unwraps a DPAPI-protected 256-bit HMAC key and computes a hex-encoded HMAC-SHA256 digest.
    /// </summary>
    public static string ComputeKeyedHash(string content, string protectedHmacKeyBase64)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedHmacKeyBase64);

        var protectedKeyBytes = Convert.FromBase64String(protectedHmacKeyBase64);
        var rawKey = ProtectedData.Unprotect(
            protectedKeyBytes,
            ApplicationEntropy,
            DataProtectionScope.CurrentUser);

        try
        {
            var contentBytes = Encoding.UTF8.GetBytes(content);
            var hashBytes = HMACSHA256.HashData(rawKey, contentBytes);
            return Convert.ToHexString(hashBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rawKey);
        }
    }
}
