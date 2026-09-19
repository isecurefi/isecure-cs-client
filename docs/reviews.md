# Implementation review record

All reviews are inline self-reviews, performed sequentially without sub-agents.

## Step 1 — scope

### Pass 1: scope and contract
Compared the attached five-step deliverable table with the API 2.10.0 operation list
and TS authentication implementation. Included registration, all MFA/verification
transitions, logout, certificate listing, PGP upload and file list/upload/download.
Finding fixed: TOTP enrollment requires both LoginMFA.SetupTOTP and VerifyTOTP;
it is now explicit rather than hidden under generic “authentication”.

### Pass 2: authority, security and isolation
Read ws-channel-api/AGENTS.md and docs/tenant-product-entitlements.md; inspected
qualify-hosted-simulator.mjs autonomous TOTP bootstrap and teardown.
Finding fixed: certificate enrollment belongs to external qualification setup, not a
simulator-specific SDK branch. Added exact account/region, guarded entitlement writes,
missing/suspended denial, cache/tenant isolation and mandatory suspension at teardown.

### Pass 3: acceptance and maintainability
Checked every requested step has an observable gate, and separated API binding/schema
availability from the supported facade. Added clean-checkout generation/build, negative
auth/transport tests, independent byte/signature evidence and retained-secret exclusions.
Outcome: step 1 complete; no unsupported production-readiness or publication claim.

## Step 2 — project and pinned generation

### Pass 1: contract and project correctness
Built the library, separate console and xUnit project on pinned SDK 10.0.302.
Generated all schema DTOs using pinned NSwag 14.7.1 and verified that the login
Account.Features field survives System.Text.Json deserialization. Contract metadata
pins upstream b3f8ea5a41e8ece23bc82efac22fbc21829a59a3 and both input/output digests.
No public generated HTTP client can bypass the forthcoming single SDK transport.

### Pass 2: supply chain and information exposure
Reviewed vendored input: documentation/examples are removed while operation and
schema definitions remain. Credentials/private keys are absent. CI has read-only
GitHub permissions, no AWS credentials, exact SDK/tool versions and package lockfiles.
Only explicit contract updates may replace the pinned input.

### Pass 3: clean-checkout reproduction
Finding fixed: NSwag emitted trailing spaces. Generation now deterministically strips
trailing whitespace. A fresh --no-local clone regenerated with an empty git diff,
restored with --locked-mode, built Release with zero warnings/errors and passed the
schema smoke test. The console is intentionally scaffolding until step 5.
Outcome: step 2 complete. Commands: bash scripts/generate.sh; git diff --exit-code;
dotnet restore --locked-mode; dotnet build --no-restore -c Release;
dotnet test --no-build -c Release.

## Step 3 — client foundations

### Pass 1: transport correctness
Reviewed request-scoped headers, escaped relative paths, JSON envelope classification,
non-2xx/gateway responses and cancellation ownership. HTTP/API/protocol/network/timeout
failures are distinct; no write retry loop exists. Tests exercise 200 logical failures,
400 API errors, 403 gateway JSON, 502 non-JSON, 302 redirects and malformed success.
Finding fixed: redirect detection now compares against the original immutable URI,
not a possibly mutated request object.

### Pass 2: isolation, secrets and lifecycle
Shared-HttpClient tests prove simultaneous tenant A/B requests keep their own tokens
and API keys and do not mutate default headers. Default headers are rejected. Owned
clients disable redirects/cookies; injected transport's no-redirect/no-retry obligation
is explicit. Exception strings and operation-only diagnostics exclude secret body/header
values; optional diagnostic sink errors cannot alter request outcomes.
Finding fixed: moved tokens into an independently locked per-instance session holder;
disposal now prevents session resurrection, with exact-expiry and independent-clear tests.

### Pass 3: test strength and maintainability
Ran dotnet test -c Release: 22 tests passed, including shared transport isolation,
expiry, disposal, cancellation versus timeout, response limits, URI validation and
no retry after failed upload. The single transport bounds response reads and owns all
HTTP error mapping; auth orchestration follows in step 4. No AWS/SDK runtime packages
or simulator branches were introduced. Outcome: step 3 complete.

## Step 4 — authentication

### Pass 1: protocol and state transitions
Compared requests and state classification to the TS client and login Lambda source.
Registration adopts a new owner API key only when configured with 0. MFA echoes the
original challenge/session without fetching another challenge; selection uses the
newly returned session. Phone/email verification precedes MFA heuristics. Enrollment
secrets are returned to the caller without being retained in the SDK auth snapshot.
Finding fixed: the Lambda emits numeric ExpiresIn although Swagger declares string.
Both integer and digit-string forms now work; invalid/non-positive expiry fails closed.

### Pass 2: secrets, invalidation and concurrency
Tests decrypt generated ciphertext using RSA-OAEP/SHA-1 and prove UTF-8 and both public
PEM formats. Malformed challenges and oversized UTF-8 passwords are rejected. Unknown
MFA factors, missing tokens, conflicting tenants and incomplete responses cannot grant
authority. Re-login clears old authority; failed MFA/verification clears pending secrets.
Findings fixed: synchronize auth publication with disposal; serialize logout even when
its token is already cancelled, so local clearing still happens. Tests cover dispose
mid-login, concurrent login/logout and failed/cancelled logout.

### Pass 3: coverage and maintainability
Ran dotnet test -c Release: 53 tests passed. Coverage includes registration, SMS,
TOTP, selection, enrollment, independent verification calls, failed/expired codes,
phone/email re-login, unknown challenges, tenant mismatch, expiry and session lifecycle.
Pure challenge encryption and small typed state results keep secrets out of ToString
and diagnostics. No prompt loop or AWS/simulator policy entered the library.
Outcome: step 4 complete; live environment qualification follows in step 5.

## Step 5 — file exchange and live qualification

### Pass 1: protocol, interoperability and example correctness
Compared file/key/certificate paths, verbs and DTO fields with the pinned contract and
TS SDK. Fixed the qualification-only enrollment verb to POST. Certificate/file-list
success responses now require their arrays; malformed downloads produce typed protocol
errors. Upload snapshots the caller's bytes before waiting for another operation.
The autonomous gpgtest run passed real C# TOTP verification/login, PGP public-key upload,
signed exact-byte upload, rejection of tampered bytes, independent HTTP/C# download and
replay digest equality for initial statements and three feedback types. The ordinary
console example also passed login/list/signed upload/download/logout with a second payment.

### Pass 2: isolation, retained authority and failure recovery
Reviewed all fixture writes and credential/output paths. AWS account, region and gpgtest
mapping are pinned; entitlements change only through the guarded operator command.
Fixed teardown to avoid AWS access when account validation fails, added private checkpoint
recovery, and gave recovery evidence a separate filename. Upgraded developer-only AWS
tooling to v3; npm audit reports zero vulnerabilities. Neither library nor console has
AWS dependencies or simulator behavior. Credentials/private keys are ignored and evidence
contains only structural checks and digests.
The live run proved missing/suspended denial for file exchange and renewal, both cache
transition windows, cross-tenant reference denial, independent access after another
tenant's suspension, and logout invalidation. Both retained entitlements were confirmed
suspended at revision 2. Local tests: 64 passed, including malformed responses, 401/403
invalidation and queued-upload byte immutability.

### Pass 3: clean-checkout reproduction and evidence audit
Cloned the candidate with --no-local into an empty temporary directory. Pinned DTO
generation produced an empty diff; locked restore, Release build (zero warnings/errors),
all 64 tests, preview package creation, npm ci and runner syntax validation passed.
Audited the package inventory: only the library, README and package metadata are present.
The checked-in live evidence identifies run cs-40f9f03771648a5d and its exact runtime
source digest; independently recomputing that digest matched the final implementation.
Recovery was rerun against the retained checkpoint: both entitlements remained suspended
at revision 2 and the original live evidence was preserved. Reviewed supported scope,
usage/qualification instructions and the private credential exclusions together.
Outcome: step 5 complete, with all three review passes recorded for every requested step.
