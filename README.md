# ISECure C# Client — Experimental

**.NET 10 · `0.1.0-preview.1` · MIT**

Register an account, log in with SMS/TOTP, discover certificates, register a PGP public
key, and list, upload or download bank files. Each `ISECureClient` owns one account
session. The SDK has no runtime NuGet dependencies.

## TL;DR: make your first API call

You need an existing ISECure **test account**, its tenant API key and **data-role password**.
For a new account, start with [registration](docs/authentication.md#registration).
Install .NET SDK **10.0.302** (pinned in `global.json`). Run these commands in bash or zsh:

```sh
git clone https://github.com/isecurefi/isecure-cs-client.git
cd isecure-cs-client
dotnet restore --locked-mode
dotnet build --no-restore -c Release
mkdir -p .private
cp examples/environment.example.sh .private/environment.sh
```

Edit `.private/environment.sh`: replace the email, API key, bank and profile values
with your account's values. Keep its test URL and test RSA public-key path together.
Then run:

```sh
source .private/environment.sh
dotnet run --project examples/Quickstart -c Release --no-build
```

The example prompts for the password and any MFA code without echoing them. It logs
in, prints `Connected. N certificate(s) visible.`, and logs out. **Zero certificates is
also a successful connection.** This first call does not upload a payment or require
a configured bank connection. If verification is required, it names the next state
and points to the [authentication guide](docs/authentication.md).

On PowerShell, copy [environment.example.ps1](examples/environment.example.ps1) to
`.private/environment.ps1`, edit it, and load it with `. ./.private/environment.ps1`.
Then run the same `dotnet` commands. For
unattended use, supply `ISECURE_PASSWORD` and, when required, `ISECURE_MFA_CODE` through
your secret manager. No Node.js, AWS credentials or GnuPG are needed for these examples.

<details>
<summary>Complete C# quickstart, including named configuration, MFA and error handling</summary>

<!-- snippet: examples/Quickstart/Program.cs -->
```csharp
using ISECure;
using ISECure.Authentication;

if (args.Contains("--help"))
{
    Console.WriteLine("ISECure Quickstart: login, handle SMS/TOTP, list certificate count, logout.");
    Console.WriteLine("From the repository root, configure examples/environment.example.sh; see README.md.");
    return;
}

using var cancelled = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancelled.Cancel(); };
try
{
    var options = new ClientOptions(
        baseUri: new Uri(Env("BASE_URL")),
        publicKeyPem: await File.ReadAllTextAsync(Env("PUBLIC_KEY_FILE"), cancelled.Token),
        email: Env("EMAIL"),
        mode: AccountMode.Data,
        apiKey: Env("API_KEY"),
        bank: Env("BANK"),
        company: Env("COMPANY"),
        name: Env("NAME"),
        phone: Env("PHONE"));

    using var client = new ISECureClient(options);
    try
    {
        var auth = await client.LoginAsync(Secret("PASSWORD", "Account password", cancelled.Token), cancelled.Token);
        if (auth.Status == AuthStatus.NeedsMfaSelection)
        {
            var method = auth.Methods.Contains(MfaMethod.Totp) ? MfaMethod.Totp : auth.Methods[0];
            auth = await client.SelectMfaTypeAsync(method, cancelled.Token);
        }
        if (auth.Status == AuthStatus.NeedsMfa)
            auth = await client.SubmitMfaCodeAsync(Secret("MFA_CODE", $"{auth.Method} code", cancelled.Token), cancellationToken: cancelled.Token);
        if (auth.Status != AuthStatus.Authenticated)
        {
            Console.Error.WriteLine($"Login requires {auth.Status} ({auth.FailureReason}). See docs/authentication.md for the next step.");
            Environment.ExitCode = 2;
            return;
        }

        var certificates = await client.ListCertificatesAsync(cancelled.Token);
        Console.WriteLine($"Connected. {certificates.Certs.Count} certificate(s) visible.");
    }
    finally
    {
        try { await client.LogoutAsync(); }
        catch (ISecureException e) { Console.Error.WriteLine($"Local session cleared; server logout failed ({e.GetType().Name})."); }
    }
}
catch (ISecureApiException e)
{
    Console.Error.WriteLine($"API refused {e.Operation}: code {e.ResponseCode}, request {e.RequestId ?? "unavailable"}.");
    Environment.ExitCode = 1;
}
catch (OperationCanceledException) { Console.Error.WriteLine("Cancelled."); Environment.ExitCode = 130; }
catch (ISecureException e) { Console.Error.WriteLine(e.Message); Environment.ExitCode = 1; }
catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
{
    // The value of a malformed setting or path is not echoed.
    Console.Error.WriteLine(e is InvalidOperationException ? e.Message : $"Check configuration and readable key files ({e.GetType().Name}); see README.md.");
    Environment.ExitCode = 2;
}

static string Env(string name) => Environment.GetEnvironmentVariable("ISECURE_" + name) is { Length: > 0 } value
    ? value : throw new InvalidOperationException($"Set ISECURE_{name}; see examples/environment.example.sh.");

static string Secret(string name, string prompt, CancellationToken cancellationToken)
{
    if (Environment.GetEnvironmentVariable("ISECURE_" + name) is { Length: > 0 } value) return value;
    if (Console.IsInputRedirected) throw new InvalidOperationException($"Set ISECURE_{name} when stdin is redirected.");
    Console.Write(prompt + ": ");
    var chars = new List<char>();
    while (true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Console.KeyAvailable) { Thread.Sleep(25); continue; }
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace) { if (chars.Count > 0) chars.RemoveAt(chars.Count - 1); }
        else if (!char.IsControl(key.KeyChar)) chars.Add(key.KeyChar);
    }
    Console.WriteLine();
    return new string(chars.ToArray());
}
```
<!-- /snippet -->

</details>

## Add the SDK to your application

From the directory containing your application's `.csproj`:

```sh
dotnet add reference /path/to/isecure-cs-client/src/ISECure.Client/ISECure.Client.csproj
```

Replace `/path/to/isecure-cs-client` with your clone location. Import `ISECure` and
`ISECure.Authentication`; response models are in `ISECure.Models`. The complete
quickstart above can replace a console application's `Program.cs`.

There is no published NuGet release yet. To try a local package, from the SDK root:

```sh
dotnet pack src/ISECure.Client -c Release -o artifacts
```

Then from your application directory:

```sh
dotnet add package ISECure.Client --version 0.1.0-preview.1 --source /path/to/isecure-cs-client/artifacts
```

The package includes XML documentation for IntelliSense.

## Choose an example

| Task | Start here |
| --- | --- |
| First login and certificate discovery | [Quickstart source](examples/Quickstart/Program.cs) |
| Registration, SMS/TOTP, factor selection, email/phone verification | [Authentication guide and compiled recipes](docs/authentication.md) |
| Sign a file entirely in C# | [Standalone signing example](examples/SignFile/README.md) |
| Register a signing key, upload and download feedback | [File exchange walkthrough](examples/FileExchange/README.md) |
| Handle refusals, expired sessions, cancellation and uncertain uploads | [Error handling](docs/errors.md) |
| Check available methods and configuration | [API guide](docs/api.md) |

`Admin` accounts register PGP keys; `Data` accounts upload/download files. Use separate
client instances for these roles. File exchange requires the appropriate bank
certificates and service access. The API's RSA **public** key encrypts login challenges;
your own OpenPGP key pair signs files. These are different keys.

## Preview scope

The supported surface is registration, authentication/verification, logout, certificate
listing, PGP public-key upload, and file list/upload/download. Certificate enrollment
and administration, password reset, file deletion, integrator administration, and the
separate Processing API are outside this preview. Generated models for other operations
do not imply a supported method. See the [coverage table](docs/preview-scope.md).

Sessions do not refresh automatically. Log in again after expiration. Uploads are not
retried automatically: if the response is lost, check status before resubmitting.

## Contributing and verification

```sh
dotnet test -c Release
python3 scripts/check-docs.py
```

Documentation snippets come from compiled examples; CI rejects drift. Building the
repository restores one PGP dependency used only by the signing examples. The SDK
package remains dependency-free. See [contract generation](docs/generation.md),
[implementation reviews](docs/reviews.md) and [DX review](docs/dx-review.md).

[Operator-only live qualification](docs/qualification.md) is separate from normal
SDK use and requires AWS access. CI runs the unit tests and offline signing checks;
it never provisions accounts or sends payments.
