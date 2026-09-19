using System.Security.Cryptography;
using System.Text;
namespace ISECure.Internal;

internal static class ChallengeEncryption
{
    internal static string Encrypt(string pem, string challenge, string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        var parts = challenge.Split('|');
        if (parts.Length != 3 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[2]) ||
            parts[1].Length is < 10 or > 16 || !parts[1].All(char.IsAsciiDigit))
            throw new ISecureProtocolException("Challenge");
        if (!pem.Contains("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal) &&
            !pem.Contains("-----BEGIN RSA PUBLIC KEY-----", StringComparison.Ordinal))
            throw new ArgumentException("RSA public key PEM is required.", nameof(pem));
        using var rsa = RSA.Create();
        try { rsa.ImportFromPem(pem); }
        catch (ArgumentException) { throw new ArgumentException("RSA public key PEM is invalid.", nameof(pem)); }
        catch (CryptographicException) { throw new ArgumentException("RSA public key PEM is invalid.", nameof(pem)); }
        if (rsa.KeySize < 2048) throw new ArgumentException("RSA keys must be at least 2048 bits.", nameof(pem));
        var bytes = Encoding.UTF8.GetBytes(password + "||" + parts[1]);
        try
        {
            if (bytes.Length > rsa.KeySize / 8 - 42) throw new ArgumentException("Password exceeds the RSA-OAEP UTF-8 byte limit.", nameof(password));
            return Convert.ToBase64String(rsa.Encrypt(bytes, RSAEncryptionPadding.OaepSHA1));
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
