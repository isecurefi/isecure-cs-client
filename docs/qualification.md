# Autonomous gpgtest qualification

The developer-only Node.js runner provisions two synthetic tenants, bootstraps TOTP
without SMS delivery, then exercises the actual C# console and SDK. Node dependencies
are not dependencies of the .NET library.

Prerequisites: pinned .NET SDK, Node.js 24, `npm ci --ignore-scripts`, AWS operator
credentials for account `589434896614`, and the ws-channel-api checkout at
`../aws/ws-channel-api` (override with `ISECURE_WSAPI_ROOT`). That checkout must have
its guarded `yarn simbank:access` command installed. Region is pinned to `eu-west-1`,
the domain mapping must resolve to API `5v82uu7087`, stage `gpgtest`, and its REST alias
must contain the simulator-enabled artifact. The runner never changes deployment aliases.

```sh
dotnet build -c Release
npm ci --ignore-scripts
npm run qualify
```

The runner performs these checks:

- C# TOTP verification, factor selection when offered, and admin/data login for two tenants.
- The README quickstart logs into a fresh account and succeeds with zero bank certificates.
- Missing entitlement denial; enabling one tenant does not authorize the other.
- Guarded enable with read-back, followed by expiry of the 60-second denial cache.
- Operator certificate setup, then C# certificate discovery and initial statement downloads.
- Independent HTTP versus C# download/replay byte equality and SHA-256 digests.
- Cross-tenant file-reference denial.
- Public admin key-registration command with autonomous MFA.
- The standalone C# signing example produces an independently verified detached signature;
  signed upload succeeds and modified bytes carrying the original signature are rejected.
- Listing and exact download of payment status, debit notification and statement feedback.
- Simulator renewal denies missing/suspended entitlements and renews an enabled tenant's
  synthetic certificate, observed through C# certificate discovery.
- The ordinary console's login/list/upload/download/logout path with a second unique payment.
- Guarded suspension, expiry of the positive cache, denial for the suspended tenant,
  and continued access for the other tenant. Logout prevents further local requests.

It uses only synthetic `.invalid` users and creates gpgtest-only API Gateway usage plans.
Entitlements use only the existing guarded operator command. Enrollment uses the normal
API as fixture setup; the C# library contains no simulator-specific logic.

Checkpoint credentials and generated PGP private keys are stored under ignored
`.private/<run-id>/` with restricted permissions. Sanitized evidence (checks, artifact
version, source digest, byte lengths/hashes, and teardown results) is stored under
ignored `artifacts/`. Keep checkpoint files private. Do not commit generated keys,
tokens, passwords or downloaded customer files.

The `finally` block suspends any retained synthetic entitlement and confirms its stored
state. A missing item continues to deny access. If a process is killed or teardown
fails, recover using its existing checkpoint:

```sh
npm run qualify -- --teardown=.private/<run-id>/checkpoint.json
```

Recovery checks account and synthetic checkpoint identity, performs exact reads, and
suspends existing items through the guarded command. It does not require a working
API deployment. Recovery evidence has its own filename so it cannot replace a live
qualification result. Synthetic identities and usage-plan resources remain available
for diagnosis; suspending their entitlement is mandatory.

The runner waits briefly after a public example logs out before logging that same
account/role in again. This avoids issuing a fresh token inside the server global-logout
revocation window. The SDK itself adds no automatic login or write retries.

CI runs offline contract/build/unit, documentation and independent signing checks, and
creates a local preview package. It has
no AWS credentials and never provisions tenants or runs live qualification.

## Recorded result

[`qualification-evidence.json`](qualification-evidence.json) records the successful
2026-09-19 DX qualification run `cs-231cedc0553ca2c9` and confirmed suspension of both
tenants. `sourceRevision` is the HEAD when the run started; the DX improvements were
then in the working tree. `runtimeSourceSha256` binds
the exact library, console, contract, fixtures, runner and pinned dependency inputs
executed in that run, including those working-tree changes. Documentation and unit
test files are outside that runtime digest.

The original SDK qualification exposed a deployment regression: the general WS API deployer had
replaced simulator-enabled REST/renewal packages with simulator-free packages. The
test aliases were restored to retained simulator releases (REST 628, renewal 109),
and guards were added locally in the backend checkout to reject replacement of those
gpgtest aliases through the general deployment entry points. Those backend changes
are separate from this SDK repository. The successful run also verified enabled renewal and both denial cases.
