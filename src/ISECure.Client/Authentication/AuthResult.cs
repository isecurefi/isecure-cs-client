using ISECure.Models;
namespace ISECure.Authentication;

public enum AuthStatus { Unauthenticated, Authenticated, NeedsMfa, NeedsMfaSelection, NeedsEmailVerification, NeedsPhoneVerification, VerificationAccepted, Failed }
public enum MfaMethod { Sms, Totp }
public enum VerificationKind { Email, Phone, Totp }
public enum AuthFailureReason { InvalidCode, ExpiredCode, TooManyAttempts, NotVerified, Unconfirmed, Unknown }

public sealed class TotpEnrollment
{
    public string Secret { get; }
    public string OtpauthUri { get; }
    public string AccessToken { get; }
    internal TotpEnrollment(string secret, string uri, string token) => (Secret, OtpauthUri, AccessToken) = (secret, uri, token);
    public override string ToString() => "TotpEnrollment (redacted)";
}

public sealed class AuthResult
{
    public AuthStatus Status { get; }
    public MfaMethod? Method { get; }
    public IReadOnlyList<MfaMethod> Methods { get; }
    public VerificationKind? Verification { get; }
    public AuthFailureReason? FailureReason { get; }
    public string? ResponseCode { get; }
    public SessionAccountDescriptor? Account { get; }
    public TotpEnrollment? TotpEnrollment { get; }
    internal AuthResult(AuthStatus status, MfaMethod? method = null, IEnumerable<MfaMethod>? methods = null,
        VerificationKind? verification = null, AuthFailureReason? reason = null, string? code = null,
        SessionAccountDescriptor? account = null, TotpEnrollment? enrollment = null)
    {
        Status = status; Method = method; Methods = Array.AsReadOnly(methods?.ToArray() ?? []);
        Verification = verification; FailureReason = reason; ResponseCode = code; Account = account; TotpEnrollment = enrollment;
    }
    public override string ToString() => $"AuthResult ({Status})";
}
