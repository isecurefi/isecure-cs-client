using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace ISECure.Internal;

internal sealed class PasswordDiscovery
{
    internal required string Challenge { get; init; }
    internal string? EncryptionProfile { get; init; }
    internal string? KeyId { get; init; }
    private string? PublicKey { get; init; }

    internal static PasswordDiscovery Parse(JsonElement data, string? profile)
    {
        var challenge = ApiTransport.String(data, "Challenge") ?? throw new ISecureProtocolException("Challenge");
        if (profile is null) return new() { Challenge = challenge };
        var key = ApiTransport.String(data, "KeyId");
        var pem = ApiTransport.String(data, "PublicKey");
        if (ApiTransport.String(data, "EncryptionProfile") != PasswordProfiles.RsaOaepSha256V1 ||
            key is null || !Regex.IsMatch(key, @"\A[A-Za-z0-9_-]{1,64}\z") ||
            challenge.Length > 3072 || !Regex.IsMatch(challenge, @"\Ap1\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\z") ||
            pem is null || pem.Length > 2048 || !pem.StartsWith("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal))
            throw new ISecureProtocolException("Challenge");
        return new() { Challenge = challenge, EncryptionProfile = profile, KeyId = key, PublicKey = pem };
    }

    internal string Encrypt(string legacyPem, string password)
    {
        if (EncryptionProfile is null) return ChallengeEncryption.Encrypt(legacyPem, Challenge, password);
        var bytes = new UTF8Encoding(false, true).GetBytes(password);
        byte[]? frame = null;
        try
        {
            if (bytes.Length is < 1 or > 283) throw new ArgumentException("Password must contain 1–283 UTF-8 bytes.", nameof(password));
            using var rsa = RSA.Create();
            rsa.ImportFromPem(PublicKey!);
            if (rsa.KeySize is not (3072 or 4096) || !rsa.ExportParameters(false).Exponent!.SequenceEqual(new byte[] { 1, 0, 1 }))
                throw new ISecureProtocolException("Challenge");
            frame = new byte[35 + bytes.Length];
            frame[0] = 1;
            SHA256.HashData(Encoding.ASCII.GetBytes(Challenge)).CopyTo(frame, 1);
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(33, 2), (ushort)bytes.Length);
            bytes.CopyTo(frame, 35);
            // .NET OAEP SHA256 uses MGF1-SHA256 and the empty label.
            return Convert.ToBase64String(rsa.Encrypt(frame, RSAEncryptionPadding.OaepSHA256));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            if (frame is not null) CryptographicOperations.ZeroMemory(frame);
        }
    }
}
