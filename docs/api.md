# API at a glance

The public facade is `ISECure.ISECureClient`. It has one isolated session and async
methods with cancellation. Methods on a single instance run serially; use separate
instances for independent accounts/roles. Passwords are provided at login/registration,
not stored in configuration. Public members ship with XML IntelliSense documentation.
Separate instances isolate local state; server logout can still affect other sessions
using the same account/role. Coordinate logout between such workers.

## Configuration

Prefer named arguments, as shown in the [complete quickstart](../examples/Quickstart/Program.cs).

| `ClientOptions` argument | Meaning |
| --- | --- |
| `baseUri` | Environment's HTTPS API base URL, without query/fragment/credentials. |
| `publicKeyPem` | That environment's RSA public key for login challenge encryption. |
| `email`, `mode`, `apiKey` | Account email, `AccountMode.Admin`/`Data`, tenant identifier. |
| `bank` | Bank identifier used in file-operation routes. |
| `company`, `name` | Profile values required by the current constructor and sent on registration. |
| `phone` | Profile phone, also used for phone verification; include the country code. |
| `requestTimeout` | Optional per-request timeout, default 30 seconds, maximum five minutes. |
| `maximumResponseBytes` | Encoded JSON limit, default 8 MiB, allowed 1 KiB–64 MiB. |

Profile values are required even when logging in to an existing account. Keep them in
one application configuration section. Options stay immutable; registration's returned
API key must be saved separately for new client instances. RSA login keys and PGP
signing keys serve different purposes and are not interchangeable.

## Methods

| Method | Input/result | Role and state |
| --- | --- | --- |
| `RegisterAsync` | Password → registration response with API key | New configured account; does not log in |
| `LoginAsync` | Password → `AuthResult` | Clears the prior session when an attempt begins |
| `SelectMfaTypeAsync` | Offered `MfaMethod` → next auth state | `NeedsMfaSelection` |
| `SubmitMfaCodeAsync` | Code; optional TOTP setup request → next auth state | `NeedsMfa` |
| `VerifyEmailAsync`, `VerifyPhoneAsync` | Code → verification result | Corresponding pending verification; fresh login after success |
| `VerifyTotpAsync` | Enrollment access token and code → verification result | Confirms an authenticator; does not create a new login |
| `LogoutAsync` | API acknowledgement or local success | Always clears local state; no login required |
| `ListCertificatesAsync` | Certificate/connection metadata | Authenticated account |
| `UploadPgpKeyAsync` | Armored public key and `PgpKeyPurpose` → acknowledgement | Authenticated admin |
| `ListFilesAsync` | Optional file type/status → descriptors | Authenticated account with bank access |
| `UploadFileAsync` | Exact bytes, name, type, detached signature → acknowledgement | Authenticated data account |
| `DownloadFileAsync` | Descriptor's type/reference → `DownloadedFile.Bytes` | Authenticated data account |

Response DTOs are in `ISECure.Models`. File/status values follow the bank's API contract;
for example `NEW` lists new files and `ALL` includes previously downloaded files.
Generated types outside the facade's supported operations are not promises of SDK
coverage. See [preview scope](preview-scope.md) for explicit exclusions.

## HTTP ownership

The SDK creates a handler with redirects and cookies disabled. Dispose the SDK client
when finished. Call `LogoutAsync` first when you need server logout.

For dependency injection, pass a caller-owned `HttpClient`. Configure its handler with
redirects/cookies/default credentials disabled, leave default headers empty, and do not
attach automatic write retries. The SDK rejects pre-existing default headers and adds
account headers only to each request. Do not mutate shared client settings afterwards.
The SDK never disposes an injected client. An optional `TimeProvider` supports clock
control in application tests.

See [authentication](authentication.md), [error handling](errors.md) and
[file exchange](../examples/FileExchange/README.md) for complete compiled recipes.
