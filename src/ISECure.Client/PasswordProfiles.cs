namespace ISECure;

/// <summary>Explicit password transport profiles; existing clients retain the legacy default.</summary>
public static class PasswordProfiles
{
    /// <summary>Direct RSA-OAEP with SHA-256/MGF1-SHA-256 and a challenge-bound binary envelope.</summary>
    public const string RsaOaepSha256V1 = "password-rsa-oaep-sha256-v1";
}
