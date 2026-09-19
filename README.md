# ISECure C# Client — Experimental

.NET 10 client for the ISECure WS Channel File Exchange API.
Preview version: `0.1.0-preview.1`.

Supports registration, SMS/TOTP authentication and verification, logout, certificate
listing, PGP public-key upload, and file listing/upload/download. See the
[operation coverage](docs/preview-scope.md) and [implementation reviews](docs/reviews.md).

## Build and use

Install the .NET SDK pinned in `global.json`, then run:

```sh
dotnet restore --locked-mode
dotnet build --no-restore -c Release
dotnet test --no-build -c Release
```

Reference `src/ISECure.Client/ISECure.Client.csproj` from your application. Package
creation is available with `dotnet pack src/ISECure.Client -c Release`; this repository
does not publish a NuGet package automatically.

```csharp
using ISECure;
using ISECure.Authentication;

using var client = new ISECureClient(options); // immutable ClientOptions
var result = await client.LoginAsync(password, cancellationToken);
if (result.Status == AuthStatus.NeedsMfaSelection)
    result = await client.SelectMfaTypeAsync(MfaMethod.Totp, cancellationToken);
if (result.Status == AuthStatus.NeedsMfa)
    result = await client.SubmitMfaCodeAsync(code, cancellationToken: cancellationToken);
if (result.Status != AuthStatus.Authenticated)
    throw new InvalidOperationException($"Authentication requires {result.Status}.");

try
{
    var files = await client.ListFilesAsync(fileType, "NEW", cancellationToken);
    foreach (var descriptor in files.FileDescriptors)
    {
        var downloaded = await client.DownloadFileAsync(
            descriptor.FileType, descriptor.FileReference, cancellationToken);
        // downloaded.Bytes contains the exact decoded file bytes.
    }
}
finally { await client.LogoutAsync(); }
```

Use a separate client per user, role and tenant. Configuration includes the HTTPS API
base URL, environment's RSA public key, email, mode, API key, bank, company, name and
phone. Passwords are passed to authentication calls. Authentication exposes explicit
incomplete states; successful phone/email verification requires a fresh login.
`SubmitMfaCodeAsync(..., setupTotp: true)` returns enrollment details only to the caller;
confirm enrollment with `VerifyTotpAsync`.

Every network method accepts cancellation. Requests have a bounded timeout, bounded
response size and no automatic write retries. Typed exceptions distinguish API,
HTTP, protocol, network, timeout and local authentication failures. API exception
`ResponseText` is server-provided data: inspect it deliberately rather than logging it
indiscriminately. Optional diagnostics contain operation, status and outcome only.

The default HTTP handler disables redirects and cookies. If injecting a shared
`HttpClient`, configure it with no default headers/credentials, cookies, redirects or
automatic retries; the SDK applies each session's headers to each request. The caller
owns an injected client. Operations on one SDK instance are serialized.

See the [runnable file exchange example](examples/FileExchange/README.md),
[contract generation](docs/generation.md), and
[autonomous test qualification](docs/qualification.md).
