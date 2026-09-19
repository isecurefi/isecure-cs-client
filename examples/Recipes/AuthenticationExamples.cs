using ISECure;
using ISECure.Authentication;

namespace ISECure.Examples;

public static class AuthenticationExamples
{
    #region registration
    public static async Task<string> RegisterAccountAsync(
        ClientOptions options, string newPassword, CancellationToken cancellationToken = default)
    {
        using var client = new ISECureClient(options);
        var registered = await client.RegisterAsync(newPassword, cancellationToken);
        // Store this tenant key in application configuration for future client instances.
        // Next, start LoginAsync and follow its verification states; registration is not login.
        return registered.ApiKey;
    }
    #endregion

    // These complete methods are embedded in the documentation and compiled in CI.
    #region login
    public static async Task<AuthResult> LoginWithMfaAsync(
        ISECureClient client,
        string password,
        Func<IReadOnlyList<MfaMethod>, MfaMethod> chooseMethod,
        Func<MfaMethod, CancellationToken, Task<string>> requestCode,
        CancellationToken cancellationToken = default)
    {
        var result = await client.LoginAsync(password, cancellationToken);
        if (result.Status == AuthStatus.NeedsMfaSelection)
            result = await client.SelectMfaTypeAsync(chooseMethod(result.Methods), cancellationToken);
        if (result.Status == AuthStatus.NeedsMfa)
        {
            var code = await requestCode(result.Method!.Value, cancellationToken);
            result = await client.SubmitMfaCodeAsync(code, cancellationToken: cancellationToken);
        }
        // Return incomplete/failed states to the application; do not loop on bad codes.
        return result;
    }
    #endregion

    #region verification
    public static async Task<AuthResult> VerifyAndLoginAsync(
        ISECureClient client, AuthResult pending, string verificationCode,
        string password, CancellationToken cancellationToken = default)
    {
        var verified = pending.Status switch
        {
            AuthStatus.NeedsEmailVerification => await client.VerifyEmailAsync(verificationCode, cancellationToken),
            AuthStatus.NeedsPhoneVerification => await client.VerifyPhoneAsync(verificationCode, cancellationToken),
            _ => throw new ArgumentException("Login must be waiting for email or phone verification.", nameof(pending))
        };
        return verified.Status == AuthStatus.VerificationAccepted
            ? await client.LoginAsync(password, cancellationToken)
            : verified;
        // The fresh login can request another verification step or MFA. Inspect Status again.
    }
    #endregion

    #region enrollment
    public static async Task<AuthResult> EnrollAuthenticatorAsync(
        ISECureClient client, string smsCode,
        Func<TotpEnrollment, CancellationToken, Task<string>> enrollAndRequestFirstCode,
        CancellationToken cancellationToken = default)
    {
        // Call only while an eligible login is waiting for an SMS MFA code.
        var login = await client.SubmitMfaCodeAsync(smsCode, setupTotp: true, cancellationToken);
        if (login.Status != AuthStatus.Authenticated || login.TotpEnrollment is not { } enrollment)
            return login;
        // Present enrollment.OtpauthUri privately to the account owner. Never log it.
        var firstCode = await enrollAndRequestFirstCode(enrollment, cancellationToken);
        return await client.VerifyTotpAsync(enrollment.AccessToken, firstCode, cancellationToken);
    }
    #endregion
}
