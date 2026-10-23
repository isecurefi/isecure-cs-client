# Inline UploadFile response review — 2026-10-23

## Pass 1: protocol and compatibility

The old facade deserialized UploadFile as the generated `Response`, which contains
only ResponseCode/ResponseText, so System.Text.Json discarded inline Content.
The new `UploadFileResponse : Response` preserves an optional string Content.
The result remains assignable to Response after awaiting. Explicit
`Task<Response>` declarations must change to `Task<UploadFileResponse>`; that
experimental SDK signature change is documented in the API guide and changelog.

Tests cover opaque Base64 bytes, an empty string, an acknowledgement with the
field absent, non-string field rejection, unchanged acknowledgement properties,
and one PUT without an automatic download. Ordinary acknowledgements still omit
Content when serialized. The generated DTOs and pinned API snapshot are unchanged.

## Pass 2: isolation and data handling

The change uses the existing authenticated, serialized transport. Tenant headers,
signature handling, byte snapshots, cancellation and refusal paths remain covered
by the full 84-test suite. Diagnostics still contain only operation/status/outcome;
a new test checks the inline payload is absent. Content stays Base64; decoding is
an explicit caller choice. There is no simulator-specific SDK behavior.

## Pass 3: package and live verification

Locked restore, Release build (zero warnings/errors), all 84 tests, NuGet packing,
contract generation without drift, and documentation snippet/link checks pass.
A separate consumer restored the prepared NuGet package from the local artifact
feed, authenticated to gpgtest, and uploaded a signed camt.060 query. Its Content
was strict Base64 and decoded to an XSD-valid camt.052 report with the same account
and balances returned by the TypeScript SDK and a separately installed Python
wheel. The consumer's before/after file lists matched; the shared fixture's
inventory also remained unchanged across all three SDKs.

Qualification used the existing synthetic test tenant. Guarded cleanup suspended
its entitlement and confirmed the stored item. Accounts and audit streams are
retained. The subsequent cache-expiry denial is recorded in private evidence.
No production bank operation or deployment was performed.

Evidence is in the main repository's `.private/inline-upload-evidence-20261023/`
directory; it contains credentials and must not be published. The package and
documentation remain unpublished; the package version is unchanged for preparation.
