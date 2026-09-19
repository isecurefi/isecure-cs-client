# Compiled application recipes

These are application examples, not additional SDK methods. CI builds these files
and checks that documentation snippets exactly match their named source regions.

- [AuthenticationExamples.cs](AuthenticationExamples.cs): registration, SMS/TOTP,
  factor selection, email/phone verification and authenticator enrollment.
- [FileExamples.cs](FileExamples.cs): admin PGP registration, signed upload, exact
  download and error handling without automatic write retries.
- [PgpSigning.cs](PgpSigning.cs): detached SHA-256 signatures using the example-only
  Bouncy Castle dependency.

Methods take their inputs as explicit parameters. Authentication UI and code delivery
are callbacks owned by the calling application. Returned authentication states must
be inspected before proceeding. The [guides](../../README.md#choose-an-example)
explain each method and its preconditions.

Copy the recipes you need into your application, or reference `Recipes.csproj` and
import `ISECure.Examples` while exploring. To reuse `PgpSigning.cs`, also add the exact
`BouncyCastle.Cryptography` version pinned in this project. The SDK itself has no PGP
or AWS runtime dependency.
