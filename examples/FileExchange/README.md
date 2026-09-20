# File exchange walkthrough

First run the [read-only quickstart](../../README.md#tldr-make-your-first-api-call).
File exchange additionally requires your bank connection/certificates and access,
an OpenPGP signing key pair, and a file type accepted by that bank. Registration alone
does not enroll bank certificates. Enroll the connection using the admin workflow below,
or use an existing connection made available through your normal ISECure onboarding process.

Run commands below from the repository root after `dotnet build -c Release`. Use your
account settings from `.private/environment.sh`. All steps here use C# and .NET only.

## Enroll the bank certificate

Use an authenticated **admin** account and configure the desired bank. Supply the company's
bank-agreement name in `ISECURE_COMPANY`, its Web Services user ID in `ISECURE_WS_USER_ID`,
and the bank-issued enrollment PIN in `ISECURE_ENROLLMENT_CODE`. Provide the admin password
and requested MFA code as described below; never commit or log enrollment credentials.

```sh
ISECURE_MODE=admin dotnet run --project examples/FileExchange -c Release --no-build -- \
  --enroll-certificate
```

This command logs in, enrolls once, and logs out. It does not upload a payment.
Check the connection with the quickstart or `ListCertificatesAsync` afterwards.
If a response is lost or the request times out, check the certificates before retrying;
the first attempt may have succeeded, and bank PINs may have limited attempts.

In your own application, after completing admin MFA:

<!-- snippet: examples/Recipes/FileExamples.cs#enroll-certificate -->
```csharp
public static async Task EnrollBankAsync(
    ISECureClient authenticatedAdmin, string company, string wsUserId, string code,
    CancellationToken cancellationToken = default)
{
    await authenticatedAdmin.EnrollCertificateAsync(company, wsUserId, code, cancellationToken);
    // ListCertificatesAsync discovers the connection after successful enrollment.
    // If the response is lost, check certificates before retrying this write.
}
```
<!-- /snippet -->

### Test simulator enrollment

Bank Simulator is a separately enabled paid test product. Registration alone does not
grant access. Configure `ISECURE_BASE_URL=https://ws-api.test.isecure.fi/v2` and
`ISECURE_BANK=simulator`. Use your registered test company's name. Unlike a real bank,
generate your own `WsUserId` (1–16 printable ASCII bytes) and fresh `Code` (16–32 printable
ASCII bytes without whitespace); no bank-issued PIN is needed. This compiled C# recipe
creates suitable values and uses the same enrollment method:

<!-- snippet: examples/Recipes/FileExamples.cs#enroll-simulator -->
```csharp
public static Task EnrollSimulatorAsync(
    ISECureClient authenticatedTestAdmin, string registeredCompany,
    CancellationToken cancellationToken = default)
{
    // The caller configured the test API and bank "simulator" and has enabled access.
    var suffix = Guid.NewGuid().ToString("N");
    return authenticatedTestAdmin.EnrollCertificateAsync(
        registeredCompany, "SIM-" + suffix[..12], "SIM-" + suffix[..24], cancellationToken);
}
```
<!-- /snippet -->

After successful enrollment, use an authenticated data client to list
`ListFilesAsync("camt.053.001.02", "NEW")`. A fresh enrollment creates one synthetic
statement; pass its descriptor to the download recipe below. The library contains no
simulator-specific enrollment or access logic. See the
[Bank Simulator guide](https://www.isecure.fi/en/bank-simulator/) for access and file formats.

## Register your PGP public key

Register the public half of your signing key using the **admin** role. Supply the admin
password in `ISECURE_PASSWORD` and the current SMS/authenticator code in
`ISECURE_MFA_CODE` if required by that account. The command selects TOTP if offered,
otherwise the offered SMS factor. The code must match the selected method.

```sh
ISECURE_MODE=admin dotnet run --project examples/FileExchange -c Release --no-build -- \
  --register-key .private/signing-public.asc
```

This logs in, registers the key for signature verification, and logs out. It does not
upload a file. Use an exported armored public key, never the RSA login key or a private
PGP key. In an application, use this complete recipe with an authenticated admin client:

<!-- snippet: examples/Recipes/FileExamples.cs#upload-key -->
```csharp
public static async Task RegisterSigningKeyAsync(
    ISECureClient authenticatedAdmin, string publicKeyFile,
    CancellationToken cancellationToken = default)
{
    var publicKey = await File.ReadAllTextAsync(publicKeyFile, cancellationToken);
    await authenticatedAdmin.UploadPgpKeyAsync(publicKey, PgpKeyPurpose.Authorize, cancellationToken);
}
```
<!-- /snippet -->

Imports for the recipes on this page: `using ISECure;` and `using ISECure.Models;`.
Copy the methods into an application class, or see [FileExamples.cs](../Recipes/FileExamples.cs).

## Sign your input file

Use the [C# signing example](../SignFile/README.md) or your existing signer:

```sh
dotnet run --project examples/SignFile -c Release --no-build -- \
  .private/payment.xml .private/signing-private.asc .private/payment.sig.asc
```

The corresponding public key must have been registered above. Configure the signing
key fingerprint/passphrase as described in the signing guide. Sign the raw bytes;
do not edit the file or normalize its line endings afterwards.

## Upload and download feedback

Use **data** credentials for the same account/bank connection. For a bank supporting
these ISO 20022 types, configure:

```sh
export ISECURE_MODE='data'
export ISECURE_UPLOAD_FILE='.private/payment.xml'
export ISECURE_SIGNATURE_FILE='.private/payment.sig.asc'
export ISECURE_UPLOAD_TYPE='pain.001.001.09'
export ISECURE_DOWNLOAD_TYPE='pain.002.001.10'
export ISECURE_DOWNLOAD_FILE='.private/payment-status.xml'
dotnet run --project examples/FileExchange -c Release --no-build
```

Set `ISECURE_PASSWORD` to the **data-role** password before running, and provide a
current `ISECURE_MFA_CODE` if that role requires one. The example does not prompt for
these secrets. PowerShell users can set the same variable names via `$env:NAME = 'value'`.
The output path must not already exist. Substitute types appropriate to your bank;
these example values do not claim support for every bank.

The example logs in, lists existing files, uploads once, polls for a new response,
downloads it twice to compare bytes, writes the result, and logs out. It prints only
counts and SHA-256 digests. Polling expects a new response of `DOWNLOAD_TYPE` within
60 seconds and is intended for an isolated example run. It selects the first new
reference; an application processing concurrent uploads must correlate feedback with
its own payment identifiers and the bank's processing rules.

A polling timeout does **not** mean the upload failed. Check status before submitting
it again. See [error handling](../../docs/errors.md) for uncertain writes and cancellation.

## Download exact bytes in your application

Pass a descriptor from `ListFilesAsync` and choose a local output path yourself:

<!-- snippet: examples/Recipes/FileExamples.cs#download -->
```csharp
public static async Task DownloadAsync(
    ISECureClient authenticatedData, FileDescriptor descriptor, string outputFile,
    CancellationToken cancellationToken = default)
{
    var file = await authenticatedData.DownloadFileAsync(
        descriptor.FileType, descriptor.FileReference, cancellationToken);
    // Caller chooses the local path. Never use a remote reference directly as a path.
    await using var output = new FileStream(outputFile, FileMode.CreateNew, FileAccess.Write);
    await output.WriteAsync(file.Bytes, cancellationToken);
}
```
<!-- /snippet -->

## Configuration reference

| Variable (prefix `ISECURE_`) | Purpose |
| --- | --- |
| `BASE_URL`, `PUBLIC_KEY_FILE` | API environment and matching RSA public PEM file |
| `EMAIL`, `API_KEY`, `MODE` | Account, tenant key and `admin`/`data` role |
| `BANK`, `COMPANY`, `NAME`, `PHONE` | Bank identifier and account profile |
| `PASSWORD`, `MFA_CODE` | Current role's password and code if requested |
| `UPLOAD_FILE`, `UPLOAD_TYPE` | Input file path and bank-supported type |
| `SIGNATURE_FILE` | Detached armored signature of the exact input bytes |
| `DOWNLOAD_TYPE`, `DOWNLOAD_FILE` | Expected response type and new local output path |

The `--register-key` command only needs the account settings and its public-key path;
it does not require any upload/download variables. `--help` requires no credentials.
`--driver` is an internal JSON-lines interface for live qualification; its output can
contain enrollment secrets or file payloads and is not a public log format.
