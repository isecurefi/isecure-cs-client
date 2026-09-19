# Authentication

Create `ClientOptions` using the named-argument configuration in the
[quickstart](../README.md#tldr-make-your-first-api-call). Use a separate client for each
email, role and tenant. `LoginAsync` returns an `AuthResult`: always inspect `Status`.

| Status | Next action |
| --- | --- |
| `Authenticated` | Call protected methods; optional `Account` contains server metadata. |
| `NeedsMfaSelection` | Choose a value from `Methods`, then call `SelectMfaTypeAsync`. |
| `NeedsMfa` | Obtain a six-digit code for `Method`, then call `SubmitMfaCodeAsync`. |
| `NeedsEmailVerification` | Call `VerifyEmailAsync`, then start a fresh login. |
| `NeedsPhoneVerification` | Call `VerifyPhoneAsync`, then start a fresh login. |
| `VerificationAccepted` | Email/phone verification needs a fresh login; successful TOTP confirmation preserves an existing session. |
| `Failed` | Inspect `FailureReason`/`ResponseCode`; resolve the cause and start a fresh login before retrying. |
| `Unauthenticated` | No login is active. |

Authentication refusals are returned as `Failed`. Network, timeout and protocol errors
throw [typed exceptions](errors.md). Registration and file-operation API refusals also
throw. The SDK does not display prompts, generate MFA codes or retry bad codes.

## Login with SMS or TOTP

This complete method accepts callbacks supplied by your UI or secret provider. Choose
only an offered factor; `requestCode` receives whether the server needs SMS or TOTP.
Do not call `LoginAsync` again between receiving a challenge and answering it.

Imports: `using ISECure;` and `using ISECure.Authentication;`. Copy these methods into
your application's class, or inspect the compiled
[AuthenticationExamples](../examples/Recipes/AuthenticationExamples.cs).

<!-- snippet: examples/Recipes/AuthenticationExamples.cs#login -->
```csharp
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
```
<!-- /snippet -->

If a code is rejected or expires, the pending SDK session is cleared. Ask the user to
restart authentication rather than repeatedly sending codes against the old challenge.

## Registration

Use the account profile and role supplied by your onboarding process. Set `apiKey: "0"`
only to create a new owner account; otherwise use your assigned tenant key. Registration
does not grant bank certificates or paid-product access, and does not log the client in.

<!-- snippet: examples/Recipes/AuthenticationExamples.cs#registration -->
```csharp
public static async Task<string> RegisterAccountAsync(
    ClientOptions options, string newPassword, CancellationToken cancellationToken = default)
{
    using var client = new ISECureClient(options);
    var registered = await client.RegisterAsync(newPassword, cancellationToken);
    // Store this tenant key in application configuration for future client instances.
    // Next, start LoginAsync and follow its verification states; registration is not login.
    return registered.ApiKey;
}
```
<!-- /snippet -->

The returned API key is the tenant identifier. The client that registered adopts it
internally, but immutable `ClientOptions.ApiKey` remains its original value. Save the
returned key and use it when creating future clients. Follow the server's verification
steps after registration; never assume verification or MFA is complete.

## Email and phone verification

Use the code for the requested verification step. The configured phone must match the
account. Successful verification requires another login, which may request another step:

<!-- snippet: examples/Recipes/AuthenticationExamples.cs#verification -->
```csharp
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
```
<!-- /snippet -->

## Add an authenticator

For an eligible SMS MFA challenge, request enrollment with `setupTotp: true`. Present
the returned authenticator URI privately to the account owner, obtain the first code
from their authenticator, and confirm it. The callback below represents that UI step:

<!-- snippet: examples/Recipes/AuthenticationExamples.cs#enrollment -->
```csharp
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
```
<!-- /snippet -->

`TotpEnrollment` contains credentials. Do not log, serialize into diagnostics, or retain
its secret, URI or access token unnecessarily. A result without enrollment details means
no enrollment was offered. Successful confirmation returns `VerificationAccepted` and
keeps the existing authenticated session; failed confirmation clears it.

## Logout and expiration

Call `LogoutAsync` when finished. It clears local authority before contacting the API,
even if that HTTP request fails or is cancelled. `Dispose` only clears local state and
releases owned HTTP resources; it does not perform server logout. Avoid letting a
logout failure hide an earlier application error (see the quickstart's `finally`).

Server logout can invalidate other sessions for the same account/role. Coordinate it
when multiple workers share an account. A new login immediately after logout can be
refused while revocation takes effect; allow a brief delay before starting another
fresh login. Do not reuse old tokens or automatically loop on authentication failures.

Tokens are kept in memory, isolated per client. They are not exposed, persisted or
refreshed by the SDK. After expiry, protected calls throw `ISecureAuthException` until
a new login completes. User roles, preview feature flags and subscription capabilities
remain separate; the server decides whether an operation is permitted.
