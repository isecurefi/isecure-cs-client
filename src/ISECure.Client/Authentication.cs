using System.Globalization;
using System.Text.Json;
using ISECure.Authentication;
using ISECure.Internal;
using ISECure.Models;
namespace ISECure;

public sealed partial class ISECureClient
{
    private readonly object _authSync = new();
    private string? _mfaSession;
    private string? _challengeName;
    private string? _verificationAccessToken;
    private AuthResult _auth = new(AuthStatus.Unauthenticated);
    // Auth state is consumed through returned immutable result objects. No secret session snapshot is retained publicly.

    public Task<RegisterResp> RegisterAsync(string password, CancellationToken cancellationToken = default) => ExclusiveAsync(async () =>
    {
        ResetAuthentication();
        var challenge = await ChallengeAsync("InitRegister", AccountPath, cancellationToken).ConfigureAwait(false);
        var response = await _transport.SendAsync("Register", HttpMethod.Put, AccountPath,
            new RegisterReq { ApiKey = _apiKey, ChResp = challenge, Company = _options.Company, Name = _options.Name,
                Phone = _options.Phone, Encrypted = ChallengeEncryption.Encrypt(_options.PublicKeyPem, challenge, password) },
            _apiKey, null, cancellationToken).ConfigureAwait(false);
        var registered = ApiTransport.Deserialize<RegisterResp>(response, "Register");
        ValidateTenant(registered.ApiKey, "Register");
        if (_apiKey != "0" && registered.ApiKey != _apiKey) throw new ISecureProtocolException("Register");
        _apiKey = registered.ApiKey;
        return registered;
    }, cancellationToken);

    public Task<AuthResult> LoginAsync(string password, CancellationToken cancellationToken = default) => ExclusiveAsync(async () =>
    {
        ResetAuthentication();
        return await AuthenticationAttemptAsync(async () =>
        {
            var challenge = await ChallengeAsync("InitLogin", SessionPath, cancellationToken).ConfigureAwait(false);
            return await _transport.SendAsync("Login", HttpMethod.Post, SessionPath,
                new LoginReq { ChResp = challenge, Encrypted = ChallengeEncryption.Encrypt(_options.PublicKeyPem, challenge, password) },
                _apiKey, null, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }, cancellationToken);

    public Task<AuthResult> SubmitMfaCodeAsync(string code, bool setupTotp = false, CancellationToken cancellationToken = default) => ExclusiveAsync(async () =>
    {
        ValidateCode(code);
        if (_auth.Status != AuthStatus.NeedsMfa || string.IsNullOrEmpty(_mfaSession)) throw new ISecureAuthException("LoginMFA");
        var request = new LoginMFAReq { Code = code, Session = _mfaSession, ChallengeName = _challengeName, SetupTOTP = setupTotp ? true : null };
        return await AuthenticationAttemptAsync(() => _transport.SendAsync("LoginMFA", HttpMethod.Put, SessionPath + "/mfacode", request, _apiKey, null, cancellationToken)).ConfigureAwait(false);
    }, cancellationToken);

    public Task<AuthResult> SelectMfaTypeAsync(MfaMethod method, CancellationToken cancellationToken = default) => ExclusiveAsync(async () =>
    {
        if (_auth.Status != AuthStatus.NeedsMfaSelection || !_auth.Methods.Contains(method) || string.IsNullOrEmpty(_mfaSession))
            throw new ISecureAuthException("SelectMFA");
        var request = new SelectMFAReq { MfaType = method == MfaMethod.Totp ? "SOFTWARE_TOKEN_MFA" : "SMS_MFA", Session = _mfaSession };
        return await AuthenticationAttemptAsync(() => _transport.SendAsync("SelectMFA", HttpMethod.Put, SessionPath + "/selectmfa", request, _apiKey, null, cancellationToken)).ConfigureAwait(false);
    }, cancellationToken);

    public Task<AuthResult> VerifyEmailAsync(string code, CancellationToken cancellationToken = default) => ExclusiveAsync(async () =>
    {
        ValidateCode(code);
        if (_auth.Status != AuthStatus.NeedsEmailVerification || string.IsNullOrEmpty(_verificationAccessToken)) throw new ISecureAuthException("VerifyEmail");
        return await VerifyAsync("VerifyEmail", HttpMethod.Post, AccountPath,
            new VerifyEmailReq { Code = code, AccessToken = _verificationAccessToken }, VerificationKind.Email, cancellationToken).ConfigureAwait(false);
    }, cancellationToken);

    public Task<AuthResult> VerifyPhoneAsync(string code, CancellationToken cancellationToken = default) => ExclusiveAsync(async () =>
    {
        ValidateCode(code);
        if (_auth.Status != AuthStatus.NeedsPhoneVerification) throw new ISecureAuthException("VerifyPhone");
        return await VerifyAsync("VerifyPhone", HttpMethod.Post, AccountPath + "/" + Segment(_options.Phone),
            new VerifyPhoneReq { Code = code }, VerificationKind.Phone, cancellationToken).ConfigureAwait(false);
    }, cancellationToken);

    // Also supports autonomous synthetic enrollment with a caller-supplied, short-lived access token.
    public Task<AuthResult> VerifyTotpAsync(string accessToken, string code, CancellationToken cancellationToken = default) => ExclusiveAsync(async () =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken); ValidateCode(code);
        return await VerifyAsync("VerifyTOTP", HttpMethod.Put, SessionPath + "/verifytotp",
            new VerifyTOTPReq { AccessToken = accessToken, Code = code }, VerificationKind.Totp, cancellationToken).ConfigureAwait(false);
    }, cancellationToken);

    public Task<Response> LogoutAsync(CancellationToken cancellationToken = default) => ExclusiveAsync(async () =>
    {
        var credentials = _authenticated.TakeForLogout();
        ResetAuthentication();
        if (credentials is null) return new Response { ResponseCode = "00", ResponseText = "Local session cleared." };
        var data = await _transport.SendAsync("Logout", HttpMethod.Delete, SessionPath, null,
            credentials.Value.ApiKey, credentials.Value.IdToken, cancellationToken).ConfigureAwait(false);
        return ApiTransport.Deserialize<Response>(data, "Logout");
    }, CancellationToken.None);

    private async Task<string> ChallengeAsync(string operation, string path, CancellationToken cancellationToken)
    {
        var data = await _transport.SendAsync(operation, HttpMethod.Get, path, null, _apiKey, null, cancellationToken).ConfigureAwait(false);
        return ApiTransport.String(data, "Challenge") ?? throw new ISecureProtocolException(operation);
    }

    private async Task<AuthResult> AuthenticationAttemptAsync(Func<Task<JsonElement>> action)
    {
        // Retain the MFA request's already-built body, but never retain previous authority while it is in flight.
        ResetAuthentication();
        try
        {
            var data = await action().ConfigureAwait(false);
            lock (_authSync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var result = ClassifyAuthentication(data);
                // Enrollment secrets belong only to the caller's result, not to retained SDK state.
                _auth = new(result.Status, result.Method, result.Methods, result.Verification, result.FailureReason, result.ResponseCode);
                return result;
            }
        }
        catch (ISecureApiException error) { ResetAuthentication(); return Failure(error); }
        catch { ResetAuthentication(); throw; }
    }

    private AuthResult ClassifyAuthentication(JsonElement data)
    {
        var text = ApiTransport.String(data, "ResponseText")!;
        var idToken = ApiTransport.String(data, "IdToken");
        var challenge = ApiTransport.String(data, "ChallengeName");
        var accessToken = ApiTransport.String(data, "AccessToken");
        if (!string.IsNullOrWhiteSpace(idToken))
        {
            if (idToken.Any(c => c < 33 || c > 126)) throw new ISecureProtocolException("Login");
            if (!string.IsNullOrEmpty(challenge) || text.Contains("verify ", StringComparison.OrdinalIgnoreCase)) throw new ISecureProtocolException("Login");
            var key = ApiTransport.String(data, "ApiKey");
            ValidateTenant(key, "Login");
            if (_apiKey != "0" && key != _apiKey) throw new ISecureProtocolException("Login");
            // Cognito returns an integer while the published Swagger schema describes a string.
            var seconds = data.TryGetProperty("ExpiresIn", out var expiry) && expiry.ValueKind == JsonValueKind.Number && expiry.TryGetInt32(out var number)
                ? number : int.TryParse(ApiTransport.String(data, "ExpiresIn"), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
            if (seconds <= 0 || seconds > 86400) throw new ISecureProtocolException("Login");
            SessionAccountDescriptor? account = data.TryGetProperty("Account", out var a) && a.ValueKind != JsonValueKind.Null ? ApiTransport.Deserialize<SessionAccountDescriptor>(a, "Login") : null;
            TotpEnrollment? enrollment = null;
            var secret = ApiTransport.String(data, "SecretCode"); var uri = ApiTransport.String(data, "OtpauthUri");
            if (secret is not null || uri is not null)
            {
                if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(uri) || string.IsNullOrWhiteSpace(accessToken)) throw new ISecureProtocolException("LoginMFA");
                enrollment = new(secret, uri, accessToken);
            }
            _authenticated.Establish(key!, idToken, TimeSpan.FromSeconds(seconds));
            _apiKey = key!;
            return new(AuthStatus.Authenticated, account: account, enrollment: enrollment);
        }
        if (text.Contains("verify phone", StringComparison.OrdinalIgnoreCase)) return new(AuthStatus.NeedsPhoneVerification);
        if (!string.IsNullOrWhiteSpace(accessToken)) { _verificationAccessToken = accessToken; return new(AuthStatus.NeedsEmailVerification); }
        if (text.Contains("verify email", StringComparison.OrdinalIgnoreCase)) throw new ISecureProtocolException("Login");
        _mfaSession = ApiTransport.String(data, "Session");
        if (string.IsNullOrWhiteSpace(_mfaSession)) throw new ISecureProtocolException("Login");
        if (challenge == "SELECT_MFA_TYPE")
        {
            if (!data.TryGetProperty("MfaOptions", out var list) || list.ValueKind != JsonValueKind.Array) throw new ISecureProtocolException("SelectMFA");
            var methods = list.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? ParseMethod(x.GetString()) : null).ToArray();
            if (methods.Length == 0 || methods.Any(x => x is null)) throw new ISecureProtocolException("SelectMFA");
            return new(AuthStatus.NeedsMfaSelection, methods: methods.Select(x => x!.Value).Distinct());
        }
        var method = ParseMethod(challenge);
        if (challenge is null)
            method = text.Contains("authenticator code", StringComparison.OrdinalIgnoreCase) ? MfaMethod.Totp :
                text.Contains("sms code", StringComparison.OrdinalIgnoreCase) ? MfaMethod.Sms : null;
        if (method is null) throw new ISecureProtocolException("Login");
        _challengeName = method == MfaMethod.Totp ? "SOFTWARE_TOKEN_MFA" : "SMS_MFA";
        return new(AuthStatus.NeedsMfa, method: method);
    }

    private async Task<AuthResult> VerifyAsync(string operation, HttpMethod method, string path, object body, VerificationKind kind, CancellationToken cancellationToken)
    {
        try
        {
            await _transport.SendAsync(operation, method, path, body, _apiKey, null, cancellationToken).ConfigureAwait(false);
            if (kind != VerificationKind.Totp) ResetAuthentication();
            return new(AuthStatus.VerificationAccepted, verification: kind);
        }
        catch (ISecureApiException error) { ResetAuthentication(); return Failure(error); }
        catch { ResetAuthentication(); throw; }
    }
    private static MfaMethod? ParseMethod(string? value) => value switch { "SMS_MFA" => MfaMethod.Sms, "SOFTWARE_TOKEN_MFA" => MfaMethod.Totp, _ => null };
    private static void ValidateCode(string code)
    {
        if (code is null || code.Length != 6 || !code.All(char.IsAsciiDigit)) throw new ArgumentException("A six-digit verification code is required.", nameof(code));
    }
    private static void ValidateTenant(string? key, string operation)
    {
        if (string.IsNullOrWhiteSpace(key) || key == "0" || key.Any(c => c < 33 || c > 126)) throw new ISecureProtocolException(operation);
    }
    private static AuthResult Failure(ISecureApiException error)
    {
        var text = error.ResponseText.ToLowerInvariant();
        var reason = text.Contains("expired") ? AuthFailureReason.ExpiredCode :
            text.Contains("too many") || text.Contains("limit exceeded") || text.Contains("attempt limit") ? AuthFailureReason.TooManyAttempts :
            text.Contains("invalid") || text.Contains("incorrect") || text.Contains("mismatch") ? AuthFailureReason.InvalidCode :
            text.Contains("unconfirmed") ? AuthFailureReason.Unconfirmed : text.Contains("not verif") ? AuthFailureReason.NotVerified : AuthFailureReason.Unknown;
        return new(AuthStatus.Failed, reason: reason, code: error.ResponseCode);
    }
    private void ResetAuthentication()
    {
        lock (_authSync)
        {
            ClearSession(); _mfaSession = null; _challengeName = null; _verificationAccessToken = null;
            _auth = new(AuthStatus.Unauthenticated);
        }
    }
}
