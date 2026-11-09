namespace ISECure;

/// <summary>Password transport profiles; new clients default to the stronger envelope.</summary>
public static class PasswordProfiles
{
    /// <summary>Explicit legacy authentication for grandfathered tenants only.</summary>
    public const string Legacy = "legacy";
    /// <summary>Direct RSA-OAEP with SHA-256/MGF1-SHA-256 and a challenge-bound binary envelope.</summary>
    public const string RsaOaepSha256V1 = "password-rsa-oaep-sha256-v1";
}
