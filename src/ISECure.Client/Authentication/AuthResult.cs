using ISECure.Models;
namespace ISECure.Authentication;

/// <summary>Outcome or next required step in an authentication flow.</summary>
public enum AuthStatus
{
    /// <summary>No login is active.</summary>
    Unauthenticated,
    /// <summary>The client can issue authenticated operations.</summary>
    Authenticated,
    /// <summary>Submit a code for Method with SubmitMfaCodeAsync.</summary>
    NeedsMfa,
    /// <summary>Choose one of Methods using SelectMfaTypeAsync.</summary>
    NeedsMfaSelection,
    /// <summary>Call VerifyEmailAsync, then start a fresh login after success.</summary>
    NeedsEmailVerification,
    /// <summary>Call VerifyPhoneAsync, then start a fresh login after success.</summary>
    NeedsPhoneVerification,
    /// <summary>A verification succeeded; email/phone verification still requires a fresh login.</summary>
    VerificationAccepted,
    /// <summary>Authentication failed and prior authority was cleared; start a fresh login before retrying.</summary>
    Failed
}
/// <summary>Supported second-factor methods.</summary>
public enum MfaMethod
{
    /// <summary>Six-digit SMS code.</summary>
    Sms,
    /// <summary>Six-digit time-based authenticator code.</summary>
    Totp
}
/// <summary>Verification operation that completed.</summary>
public enum VerificationKind
{
    /// <summary>Email ownership verification.</summary>
    Email,
    /// <summary>Phone ownership verification.</summary>
    Phone,
    /// <summary>Authenticator enrollment confirmation.</summary>
    Totp
}
/// <summary>Best-effort classification of the server refusal; use ResponseCode for the original API code.</summary>
public enum AuthFailureReason
{
    /// <summary>The server reported an invalid code or credential.</summary>
    InvalidCode,
    /// <summary>The server reported an expired code.</summary>
    ExpiredCode,
    /// <summary>The server refused further attempts or reported a limit.</summary>
    TooManyAttempts,
    /// <summary>Required verification is incomplete.</summary>
    NotVerified,
    /// <summary>The account is unconfirmed.</summary>
    Unconfirmed,
    /// <summary>No supported refusal classification matched.</summary>
    Unknown
}

/// <summary>Caller-owned secrets for adding an authenticator. Never log or serialize this object into diagnostics.</summary>
public sealed class TotpEnrollment
{
    /// <summary>Base32 enrollment secret for the authenticator. Treat as a credential.</summary>
    public string Secret { get; }
    /// <summary>Authenticator enrollment URI; includes the secret. Display only to the enrolling user.</summary>
    public string OtpauthUri { get; }
    /// <summary>Short-lived token to pass to VerifyTotpAsync.</summary>
    public string AccessToken { get; }
    internal TotpEnrollment(string secret, string uri, string token) => (Secret, OtpauthUri, AccessToken) = (secret, uri, token);
    /// <summary>Returns a structural label without account information or enrollment secrets.</summary>
    public override string ToString() => "TotpEnrollment (redacted)";
}

/// <summary>Immutable authentication outcome. Inspect Status before making a protected request.</summary>
public sealed class AuthResult
{
    /// <summary>Current outcome or next action.</summary>
    public AuthStatus Status { get; }
    /// <summary>Required factor when Status is NeedsMfa; otherwise null.</summary>
    public MfaMethod? Method { get; }
    /// <summary>Factors offered when Status is NeedsMfaSelection; otherwise empty.</summary>
    public IReadOnlyList<MfaMethod> Methods { get; }
    /// <summary>Verification operation that succeeded, when supplied.</summary>
    public VerificationKind? Verification { get; }
    /// <summary>Best-effort server refusal classification when Status is Failed.</summary>
    public AuthFailureReason? FailureReason { get; }
    /// <summary>Original API refusal code when supplied; successful states may leave it null.</summary>
    public string? ResponseCode { get; }
    /// <summary>Optional server account metadata. Features and roles do not grant SDK-side authority.</summary>
    public SessionAccountDescriptor? Account { get; }
    /// <summary>Optional enrollment details returned after requesting setupTotp during MFA completion.</summary>
    public TotpEnrollment? TotpEnrollment { get; }
    internal AuthResult(AuthStatus status, MfaMethod? method = null, IEnumerable<MfaMethod>? methods = null,
        VerificationKind? verification = null, AuthFailureReason? reason = null, string? code = null,
        SessionAccountDescriptor? account = null, TotpEnrollment? enrollment = null)
    {
        Status = status; Method = method; Methods = Array.AsReadOnly(methods?.ToArray() ?? []);
        Verification = verification; FailureReason = reason; ResponseCode = code; Account = account; TotpEnrollment = enrollment;
    }
    /// <summary>Returns a structural label without account information or enrollment secrets.</summary>
    public override string ToString() => $"AuthResult ({Status})";
}
