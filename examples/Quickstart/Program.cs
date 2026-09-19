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
