using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ISECure.Authentication;
namespace ISECure.Client.Tests;

public class FileExchangeTests
{
    [Fact]
    public async Task BytesSignaturePathsAndTenantHeadersSurviveRoundTrip()
    {
        using var rsa = RSA.Create(2048);
        byte[] exact = [0, 255, 13, 10, 0xc3, 0xa4];
        var calls = new List<string>();
        using var http = new HttpClient(new FakeHandler(async (r, ct) => {
            calls.Add(r.Method + " " + r.RequestUri!.AbsoluteUri);
            if (r.RequestUri.AbsolutePath.Contains("/session/"))
                return FakeHandler.Json(r.Method == HttpMethod.Get ? """{"ResponseCode":"00","ResponseText":"OK","Challenge":"nonce|1789841000000|id"}""" :
                    """{"ResponseCode":"00","ResponseText":"OK","ApiKey":"tenant-A","IdToken":"id-token","ExpiresIn":"3600"}""");
            Assert.Equal("tenant-A", r.Headers.GetValues("x-api-key").Single());
            Assert.Equal("id-token", r.Headers.GetValues("Authorization").Single());
            if (r.Method == HttpMethod.Put) {
                using var body = JsonDocument.Parse(await r.Content!.ReadAsStringAsync(ct));
                Assert.Equal(exact, Convert.FromBase64String(body.RootElement.GetProperty("FileContents").GetString()!));
                Assert.Equal("detached-exact-signature", body.RootElement.GetProperty("Signature").GetString());
                return FakeHandler.Json(Fixture.Ok, HttpStatusCode.Created);
            }
            if (r.RequestUri.Query.Length > 0) return FakeHandler.Json("""{"ResponseCode":"00","ResponseText":"OK","FileDescriptors":[{"FileReference":"ref/with?chars","FileType":"type/a"}]}""");
            return FakeHandler.Json(JsonSerializer.Serialize(new { ResponseCode = "00", ResponseText = "OK", Content = Convert.ToBase64String(exact) }));
        }));
        var options = new ClientOptions(new Uri("https://example.invalid/v2"), rsa.ExportSubjectPublicKeyInfoPem(), "user+tag@example.invalid", AccountMode.Data, "tenant-A", "bank/name", "company", "name", "phone");
        using var client = new ISECureClient(options, http);
        Assert.Equal(AuthStatus.Authenticated, (await client.LoginAsync("password")).Status);
        await client.UploadFileAsync(exact, "file.xml", "type/a", "detached-exact-signature");
        var listed = await client.ListFilesAsync("type/a", "NEW");
        var descriptor = Assert.Single(listed.FileDescriptors);
        var downloaded = await client.DownloadFileAsync(descriptor.FileType, descriptor.FileReference);
        Assert.Equal(exact, downloaded.Bytes.ToArray());
        Assert.Contains(calls, c => c.Contains("bank%2Fname/type%2Fa/ref%2Fwith%3Fchars"));
        Assert.Contains(calls, c => c.Contains("FileType=type%2Fa&Status=NEW"));
    }

    [Fact]
    public async Task UnauthenticatedProtectedCallsNeverReachTransport()
    {
        var handler = new FakeHandler((_, _) => throw new InvalidOperationException("must not send"));
        using var http = new HttpClient(handler); using var client = new ISECureClient(Fixture.Options(), http);
        await Assert.ThrowsAsync<ISecureAuthException>(() => client.ListFilesAsync());
        await Assert.ThrowsAsync<ISecureAuthException>(() => client.ListCertificatesAsync());
        await Assert.ThrowsAsync<ISecureAuthException>(() => client.DownloadFileAsync("type", "ref"));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("download", "\"Content\":\"bad-base64!\"")]
    [InlineData("download", "\"Content\":null")]
    [InlineData("list", "\"FileDescriptors\":null")]
    [InlineData("list", "\"FileDescriptors\":[null]")]
    [InlineData("list", "\"FileDescriptors\":[{\"FileType\":\"type\"}]")]
    [InlineData("certificates", "\"Certs\":null")]
    public async Task MalformedSuccessfulFileResponsesAreProtocolErrors(string operation, string fields)
    {
        using var rsa = RSA.Create(2048);
        using var http = new HttpClient(new FakeHandler((r, _) => Task.FromResult(
            AuthResponse(r) ?? FakeHandler.Json("{\"ResponseCode\":\"00\",\"ResponseText\":\"OK\"," + fields + "}"))));
        using var client = new ISECureClient(Options(rsa), http);
        await client.LoginAsync("password");
        await Assert.ThrowsAsync<ISecureProtocolException>(async () => {
            if (operation == "download") await client.DownloadFileAsync("type", "ref");
            else if (operation == "list") await client.ListFilesAsync();
            else await client.ListCertificatesAsync();
        });
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ServerAuthorizationFailureInvalidatesLocalSession(HttpStatusCode status)
    {
        using var rsa = RSA.Create(2048);
        var handler = new FakeHandler((r, _) => Task.FromResult(AuthResponse(r) ?? FakeHandler.Json("{}", status)));
        using var http = new HttpClient(handler); using var client = new ISECureClient(Options(rsa), http);
        await client.LoginAsync("password");
        await Assert.ThrowsAsync<ISecureHttpException>(() => client.ListFilesAsync());
        var calls = handler.Calls;
        await Assert.ThrowsAsync<ISecureAuthException>(() => client.ListFilesAsync());
        Assert.Equal(calls, handler.Calls);
    }

    [Fact]
    public async Task UploadSnapshotsBytesWhileWaitingForAnotherOperation()
    {
        using var rsa = RSA.Create(2048);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        byte[] bytes = [1, 2, 3]; byte[] original = [1, 2, 3];
        using var http = new HttpClient(new FakeHandler(async (r, ct) => {
            if (AuthResponse(r) is { } auth) return auth;
            if (r.Method == HttpMethod.Get) {
                entered.SetResult(); await release.Task.WaitAsync(ct);
                return FakeHandler.Json("""{"ResponseCode":"00","ResponseText":"OK","FileDescriptors":[]}""");
            }
            using var json = JsonDocument.Parse(await r.Content!.ReadAsStringAsync(ct));
            Assert.Equal(original, Convert.FromBase64String(json.RootElement.GetProperty("FileContents").GetString()!));
            return FakeHandler.Json(Fixture.Ok);
        }));
        using var client = new ISECureClient(Options(rsa), http); await client.LoginAsync("password");
        var list = client.ListFilesAsync(); await entered.Task;
        var upload = client.UploadFileAsync(bytes, "file.xml", "type", "signature");
        bytes[0] = 255; release.SetResult();
        await Task.WhenAll(list, upload);
    }

    private static ClientOptions Options(RSA rsa) => new(new Uri("https://example.invalid/v2"),
        rsa.ExportSubjectPublicKeyInfoPem(), "synthetic@example.invalid", AccountMode.Data, "tenant-A", "bank", "company", "name", "phone");

    private static HttpResponseMessage? AuthResponse(HttpRequestMessage request) =>
        !request.RequestUri!.AbsolutePath.Contains("/session/") ? null : FakeHandler.Json(request.Method == HttpMethod.Get ?
            """{"ResponseCode":"00","ResponseText":"OK","Challenge":"nonce|1789841000000|id"}""" :
            """{"ResponseCode":"00","ResponseText":"OK","ApiKey":"tenant-A","IdToken":"token","ExpiresIn":3600}""");
}
