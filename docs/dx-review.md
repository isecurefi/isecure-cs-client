# Public preview developer-experience review

All reviews are inline self-reviews, performed sequentially without sub-agents.
This follows the [initial implementation review](reviews.md).

## Pass 1: onboarding and API clarity

Reviewed the repository from a new integrator's first call through file exchange.
Replaced incomplete setup with a complete named-argument quickstart, editable shell
and PowerShell configuration templates, explicit account/role prerequisites, and a
read-only first call that succeeds without bank certificates. Added focused compiled
registration, MFA selection, verification, enrollment, signing, download and error
recipes. Documentation snippets are checked against their compiled source; nine
snippets and local documentation links pass validation.

Documented every public facade/configuration/result/error member for IntelliSense.
The local NuGet package includes the XML documentation and has no runtime dependencies.
A separate .NET 10 application installed that package, compiled the README quickstart
and ran its help command. Tested the actual terminal password prompt: input does not
echo, and Ctrl+C exits with code 130. Missing settings identify the variable to set.

## Pass 2: example correctness and failure behavior

Reviewed credential handling, cancellation, MFA choice, byte preservation and uncertain
writes. Example failures preserve the original operation error, avoid raw response
logging and never automatically resubmit uploads. File exchange validates local input
before login/upload and refuses to overwrite an existing output file.

The signing example uses a pinned example-only Bouncy Castle dependency, supports
UTF-8 passphrases and explicit full-fingerprint key selection, signs the exact uploaded
bytes, and verifies locally. Independent OpenPGP.js checks cover valid signatures,
tampering, wrong passphrases and overwrite refusal. CLI help and configuration-error
checks pass. The NuGet vulnerability check found no known vulnerabilities in the
example's dependency at review time.

Live run `cs-231cedc0553ca2c9` passed the README quickstart, autonomous MFA, public key
registration command, C# signature acceptance, complete ordinary file-exchange example,
independent download/replay byte comparisons, renewal and tenant/cache isolation.
Both retained synthetic entitlements were confirmed suspended. An earlier attempt
exposed a server global-logout revocation window; the qualification harness now waits
briefly before logging the same account/role in again. Application documentation
explains the shared-session implication. No automatic retry was added to the SDK.
The checked-in evidence's runtime digest was independently recomputed and matched.

## Pass 3: clean-checkout and packaging verification

Cloned candidate `d017ba1` with `--no-local` into an empty temporary directory.
Pinned contract/NSwag generation produced no diff. Locked restore and Release build
passed with zero warnings/errors. All 70 tests, all nine compiled documentation
snippets, local documentation links, runner syntax and independent offline signing/
CLI checks passed. `npm ci --ignore-scripts` reported zero known vulnerabilities.
The checkout remained clean after these checks.

Created and inspected `ISECure.Client.0.1.0-preview.1.nupkg`: it includes the DLL,
README and IntelliSense XML, with no runtime package dependencies or fixture secrets.
The separate consumer application check also passed. The library remains experimental;
unsupported operations and the absence of a published NuGet release are explicit.
Outcome: the identified onboarding, examples and IntelliSense gaps are resolved for
the experimental public preview. Publication remains a separate repository action.
