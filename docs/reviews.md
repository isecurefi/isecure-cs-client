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
