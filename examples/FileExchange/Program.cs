using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ISECure;
using ISECure.Authentication;

if (args.Contains("--driver"))
{
    // JSON-lines interface for autonomous qualification. Credentials arrive through stdin, never process arguments.
    var clients = new Dictionary<string, ISECureClient>();
    var json = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
    try
    {
        while (await Console.In.ReadLineAsync() is { } line)
        {
            try
            {
                using var doc = JsonDocument.Parse(line); var x = doc.RootElement;
                string Value(string name) => x.GetProperty(name).GetString()!;
                var id = Value("client"); var operation = Value("operation");
                object? result;
                if (operation == "create")
                {
                    if (clients.ContainsKey(id)) throw new ArgumentException("Client already exists.");
                    clients[id] = new ISECureClient(new ClientOptions(new Uri(Value("baseUrl")), Value("publicKey"), Value("email"),
                        Enum.Parse<AccountMode>(Value("mode"), true), Value("apiKey"), Value("bank"), Value("company"), Value("name"), Value("phone")));
                    result = new { created = true };
                }
                else
                {
                    var client = clients[id];
                    result = operation switch
                    {
                        "register" => await client.RegisterAsync(Value("password")),
                        "login" => await client.LoginAsync(Value("password")),
                        "select" => await client.SelectMfaTypeAsync(Enum.Parse<MfaMethod>(Value("method"), true)),
                        "mfa" => await client.SubmitMfaCodeAsync(Value("code"), x.TryGetProperty("setupTotp", out var setup) && setup.GetBoolean()),
                        "verifyTotp" => await client.VerifyTotpAsync(Value("accessToken"), Value("code")),
                        "certificates" => await client.ListCertificatesAsync(),
                        "uploadKey" => await client.UploadPgpKeyAsync(Value("publicKey")),
                        "list" => await client.ListFilesAsync(Value("fileType"), Value("status")),
                        "upload" => await client.UploadFileAsync(Convert.FromBase64String(Value("contents")), Value("fileName"), Value("fileType"), Value("signature")),
                        "download" => await Download(client, Value("fileType"), Value("reference")),
                        "logout" => await client.LogoutAsync(),
                        _ => throw new ArgumentException("Unknown operation.")
                    };
                }
                Console.WriteLine(JsonSerializer.Serialize(new { ok = true, result }, json));
            }
            catch (ISecureApiException e) { Console.WriteLine(JsonSerializer.Serialize(new { ok = false, error = "api", status = (int)e.StatusCode, code = e.ResponseCode, responseText = e.ResponseText, requestId = e.RequestId })); }
            catch (ISecureHttpException e) { Console.WriteLine(JsonSerializer.Serialize(new { ok = false, error = "http", status = (int)e.StatusCode })); }
            catch (Exception e) { Console.WriteLine(JsonSerializer.Serialize(new { ok = false, error = e.GetType().Name })); }
        }
    }
    finally { foreach (var client in clients.Values) client.Dispose(); }
    return;
}

if (args.Contains("--help"))
{
    Console.WriteLine("ISECure File Exchange — Experimental. Configure ISECURE_* environment variables; see examples/FileExchange/README.md.");
    return;
}
try
{
    string Env(string name) => Environment.GetEnvironmentVariable("ISECURE_" + name) ?? throw new ArgumentException("Missing ISECURE_" + name);
    using var client = new ISECureClient(new ClientOptions(new Uri(Env("BASE_URL")), await File.ReadAllTextAsync(Env("PUBLIC_KEY_FILE")),
        Env("EMAIL"), Enum.Parse<AccountMode>(Env("MODE"), true), Env("API_KEY"), Env("BANK"), Env("COMPANY"), Env("NAME"), Env("PHONE")));
    var state = await client.LoginAsync(Env("PASSWORD"));
    if (state.Status == AuthStatus.NeedsMfaSelection) state = await client.SelectMfaTypeAsync(MfaMethod.Totp);
    if (state.Status == AuthStatus.NeedsMfa) state = await client.SubmitMfaCodeAsync(Env("MFA_CODE"));
    if (state.Status != AuthStatus.Authenticated) throw new InvalidOperationException("Authentication did not complete: " + state.Status);
    try
    {
        var before = await client.ListFilesAsync(Env("DOWNLOAD_TYPE"), "ALL");
        Console.WriteLine($"Listed {before.FileDescriptors.Count} files.");
        var bytes = await File.ReadAllBytesAsync(Env("UPLOAD_FILE"));
        var signature = await File.ReadAllTextAsync(Env("SIGNATURE_FILE"));
        await client.UploadFileAsync(bytes, Path.GetFileName(Env("UPLOAD_FILE")), Env("UPLOAD_TYPE"), signature);
        Console.WriteLine($"Uploaded {bytes.Length} bytes; SHA-256 {Convert.ToHexStringLower(SHA256.HashData(bytes))}.");
        var prior = before.FileDescriptors.Select(x => x.FileReference).ToHashSet();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (true)
        {
            var listed = await client.ListFilesAsync(Env("DOWNLOAD_TYPE"), "NEW", timeout.Token);
            var descriptor = listed.FileDescriptors.FirstOrDefault(x => !prior.Contains(x.FileReference));
            if (descriptor is not null)
            {
                var downloaded = await client.DownloadFileAsync(descriptor.FileType, descriptor.FileReference, timeout.Token);
                var repeated = await client.DownloadFileAsync(descriptor.FileType, descriptor.FileReference, timeout.Token);
                if (!downloaded.Bytes.Span.SequenceEqual(repeated.Bytes.Span)) throw new InvalidOperationException("Repeated download bytes differ.");
                await File.WriteAllBytesAsync(Env("DOWNLOAD_FILE"), downloaded.Bytes.ToArray(), timeout.Token);
                Console.WriteLine($"Downloaded {downloaded.Bytes.Length} bytes; SHA-256 {Convert.ToHexStringLower(SHA256.HashData(downloaded.Bytes.Span))}.");
                break;
            }
            await Task.Delay(1000, timeout.Token);
        }
    }
    finally { await client.LogoutAsync(); }
}
catch (Exception e)
{
    Console.Error.WriteLine(e is ISecureException ? e.Message : "File exchange example failed: " + e.GetType().Name);
    Environment.ExitCode = 1;
}

static async Task<object> Download(ISECureClient client, string type, string reference)
{
    var file = await client.DownloadFileAsync(type, reference);
    return new { content = Convert.ToBase64String(file.Bytes.Span), sha256 = Convert.ToHexStringLower(SHA256.HashData(file.Bytes.Span)), bytes = file.Bytes.Length };
}
