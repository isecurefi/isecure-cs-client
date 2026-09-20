# Experimental preview: 0.1.0-preview.2

## Deliverables and public operation coverage

| Capability | API operations | Preview facade |
| --- | --- | --- |
| Registration | InitRegister, Register | RegisterAsync |
| Authentication | InitLogin, Login, LoginMFA, SelectMFA | LoginAsync, SubmitMfaCodeAsync, SelectMfaTypeAsync |
| Verification and TOTP enrollment | VerifyPhone, VerifyEmail, VerifyTOTP; LoginMFA with SetupTOTP | VerifyPhoneAsync, VerifyEmailAsync, VerifyTotpAsync; enrollment returned in auth result |
| Logout | Logout | LogoutAsync; local state clears even if request fails |
| Certificate discovery | ListCerts | ListCertificatesAsync |
| Certificate enrollment | EnrollCert | EnrollCertificateAsync |
| PGP authorization key | UploadKey | UploadPgpKeyAsync |
| Files | ListFiles, UploadFile, DownloadFile | ListFilesAsync, UploadFileAsync, DownloadFileAsync |

The contract's remaining operations are not part of the preview facade. Generated
models may represent the full schema without promising operation coverage. Certificate
enrollment uses the same method for any configured bank; simulator behavior remains
server-owned. Password reset, other certificate administration, key deletion, file deletion,
integrator administration and the separate Processing API are later scope.

## Architecture and invariants

- .NET 10 library, separate console example and tests. Pin SDK, NuGet dependencies,
  generator and the exact public OpenAPI revision/digest. Commit generated models;
  generation uses the local contract, never a floating network input.
- A small asynchronous facade uses generated request/response DTOs and one HTTP
  transport. Each instance owns immutable tenant/user/environment options and mutable
  session state. Never mutate HttpClient.DefaultRequestHeaders or global configuration.
- Inject HttpClient and TimeProvider. Support caller cancellation and bounded request
  timeout, typed protocol/API/HTTP/network/timeout/auth-state failures, safe operation-only
  diagnostics, and no automatic retries of writes. Do not follow redirects with secrets.
- Explicit auth states cover authenticated, SMS/TOTP required, factor selection,
  phone/email verification, verification accepted and failure. Unknown/malformed
  challenges fail closed. Each new login invalidates the previous session. Failed
  authentication, logout and expiration prevent stale-token use. Serialize operations
  within an instance to avoid auth races; separate instances remain independent.
- Password challenge encryption uses the server's RSA-OAEP/SHA-1 wire protocol and
  UTF-8, with validated challenge structure and size. Secrets stay in memory and never
  appear in diagnostic events, exception messages or string representations.
- Account.Type, Account.Features and Account.Entitlements are returned session facts;
  they do not grant SDK-side authority. API key identifies tenant and is not a secret.
- Accept bank as configuration; no simulator branches, AWS dependencies or PGP key
  generation in the library. Upload exact bytes with a caller-supplied detached signature;
  download exact bytes. PGP signing belongs in example/qualification tooling.

## Completion gates

1. Scope: coverage above mapped to actual operation IDs and auth contract fields.
2. Project: clean-checkout restore/build/test and byte-identical regeneration with pins.
3. Foundations: tests cover two tenants using one HttpClient, escaping, cancellation,
   timeout, protocol/network/HTTP/API failures, log redaction and write non-retry.
4. Auth: mock HTTP tests cover challenge interoperability (including UTF-8), registration,
   SMS/TOTP/selection, TOTP setup and verification, phone/email steps, bad codes,
   malformed responses, session clearing, expiry and auth-operation races.
5. Live: C# example authenticates via autonomous TOTP against the gpgtest API using
   bank `simulator`, lists files, verifies detached signatures, uploads signed exact bytes,
   downloads feedback and checks exact bytes/digests with independent evidence.
   Prove missing and suspended entitlement denial, the 60-second cache window and tenant
   isolation. A second tenant cannot retrieve primary file references. Bad signatures
   must fail. Retained synthetic tenant entitlements are suspended during teardown.

Qualification is pinned to AWS account 589434896614, eu-west-1 and the gpgtest stage.
Only the existing guarded `yarn simbank:access enable|disable --api-key=...` command
changes access. No email-based access command or per-user product flags. Registration
does not grant simulator access. Use synthetic identities and disposable local keys,
suppress messages during fixture bootstrap, and record evidence without secrets.

Three inline review passes follow EACH step before the next begins:
(1) scope and API correctness, (2) security/isolation/failure behavior, (3) verification
and maintainability. Record concrete findings, fixes and commands in reviews.md.
Step 5 receives the same three passes. No sub-agents.

## Distribution

The [GitHub repository](https://github.com/isecurefi/isecure-cs-client) is public as an
Experimental .NET 10 preview. Follow the [README](../README.md) to install from source
or build a local package; no NuGet release is published. Website links and examples
describe only the preview operations listed above and do not imply full API coverage.
