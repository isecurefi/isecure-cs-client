# File exchange example

Build with `dotnet build -c Release` from the repository root. The example logs in,
lists existing files, uploads exact bytes and their detached PGP signature, polls for
a new response, downloads it twice to verify byte equality, writes it to disk, and
logs out. Console output contains counts and SHA-256 digests.

Provide these environment variables using your local secret manager or shell:

| Variable (prefix `ISECURE_`) | Value |
| --- | --- |
| `BASE_URL` | Environment's HTTPS API base URL |
| `PUBLIC_KEY_FILE` | Environment's RSA public PEM file |
| `EMAIL`, `MODE`, `API_KEY` | Account, `data` or `admin`, and tenant API key |
| `BANK`, `COMPANY`, `NAME`, `PHONE` | Account and bank configuration |
| `PASSWORD` | Account password |
| `MFA_CODE` | Current SMS/TOTP code, when login requires it |
| `UPLOAD_FILE`, `UPLOAD_TYPE` | Signed source file path and API file type |
| `SIGNATURE_FILE` | Armored detached PGP signature of that exact source file |
| `DOWNLOAD_TYPE`, `DOWNLOAD_FILE` | Expected response type and local output path |

Run:

```sh
dotnet run --project examples/FileExchange -c Release --no-build
```

Use data mode for file uploads/downloads and register the corresponding PGP public key
before running. Sign the raw file bytes, then leave that file unchanged. The example
accepts a caller-created signature so the library has no PGP runtime dependency.
For an autonomous signing and key-registration example, see
[`scripts/qualify-gpgtest.mjs`](../../scripts/qualify-gpgtest.mjs).

Polling assumes this upload produces a new response of `DOWNLOAD_TYPE` within 60 seconds.
Select the appropriate response type for the bank's workflow. A timeout does not
mean the upload failed; check its status before attempting another upload.

`--driver` is the qualification-only JSON-lines interface. It accepts credentials on
stdin and returns structured results on stdout. Do not capture its raw output as a
public log: authentication enrollment results and downloaded payloads can be sensitive.
