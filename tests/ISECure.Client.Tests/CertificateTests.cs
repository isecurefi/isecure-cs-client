using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ISECure.Authentication;
namespace ISECure.Client.Tests;

public class CertificateTests
{
    private static ClientOptions Options(RSA rsa, string tenant = "tenant-A") =>
        new(new Uri("https://example.invalid/v2"), rsa.ExportSubjectPublicKeyInfoPem(), "user@example.invalid",
            AccountMode.Admin, tenant, "bank/name", "Profile Company", "Synthetic User", "+358400100001");

    private static HttpResponseMessage? AuthResponse(HttpRequestMessage request)
    {
        if (!request.RequestUri!.AbsolutePath.Contains("/session/")) return null;
        return FakeHandler.Json(request.Method == HttpMethod.Get
            ? """{"ResponseCode":"00","ResponseText":"OK","Challenge":"nonce|1789841000000|id"}"""
            : JsonSerializer.Serialize(new { ResponseCode = "00", ResponseText = "OK",
                ApiKey = request.Headers.GetValues("x-api-key").Single(), IdToken = "id-" + request.Headers.GetValues("x-api-key").Single(), ExpiresIn = "3600" }));
    }

    [Fact]
    public async Task EnrollmentPreservesExactInputsEscapesBankAndIsolatesTenants()
    {
        using var rsa = RSA.Create(2048);
        var tenants = new List<string>();
        var diagnostics = new List<SdkDiagnostic>();
        using var http = new HttpClient(new FakeHandler(async (request, ct) => {
            if (AuthResponse(request) is { } auth) return auth;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://example.invalid/v2/certs/bank%2Fname", request.RequestUri!.AbsoluteUri);
            var tenant = request.Headers.GetValues("x-api-key").Single();
            tenants.Add(tenant);
            Assert.Equal("id-" + tenant, request.Headers.GetValues("Authorization").Single());
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal(3, body.RootElement.EnumerateObject().Count());
            Assert.Equal("Ää & Partners Oy", body.RootElement.GetProperty("Company").GetString());
            Assert.Equal("WS/123", body.RootElement.GetProperty("WsUserId").GetString());
            Assert.Equal("secret+pin/001", body.RootElement.GetProperty("Code").GetString());
            return FakeHandler.Json(Fixture.Ok);
        }));
        using var a = new ISECureClient(Options(rsa), http, diagnostic: diagnostics.Add);
        using var b = new ISECureClient(Options(rsa, "tenant-B"), http, diagnostic: diagnostics.Add);
        await a.LoginAsync("password"); await b.LoginAsync("password");
        await a.EnrollCertificateAsync("Ää & Partners Oy", "WS/123", "secret+pin/001");
        await b.EnrollCertificateAsync("Ää & Partners Oy", "WS/123", "secret+pin/001");
        Assert.Equal(new[] { "tenant-A", "tenant-B" }, tenants);
        Assert.Empty(http.DefaultRequestHeaders);
        Assert.DoesNotContain("secret+pin/001", JsonSerializer.Serialize(diagnostics));
        Assert.Contains(diagnostics, d => d.Operation == "EnrollCert");
    }

    [Fact]
    public async Task UnauthenticatedOrCancelledEnrollmentDoesNotSend()
    {
        using var http = new HttpClient(new FakeHandler((_, _) => throw new InvalidOperationException("must not send")));
        using var client = new ISECureClient(Fixture.Options(), http);
        await Assert.ThrowsAsync<ISecureAuthException>(() => client.EnrollCertificateAsync("company", "user", "pin"));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.EnrollCertificateAsync("company", "user", "pin", cancel.Token));
    }

    [Theory]
    [InlineData("", "user", "pin")]
    [InlineData("company", " ", "pin")]
    [InlineData("company", "user", "")]
    public async Task MissingEnrollmentInputsDoNotSend(string company, string user, string pin)
    {
        using var http = new HttpClient(new FakeHandler((_, _) => throw new InvalidOperationException("must not send")));
        using var client = new ISECureClient(Fixture.Options(), http);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.EnrollCertificateAsync(company, user, pin));
    }

    [Theory]
    [InlineData("network")]
    [InlineData("api")]
    [InlineData("unauthorized")]
    [InlineData("malformed")]
    [InlineData("cancelled")]
    public async Task FailedEnrollmentIsNeverRetried(string failure)
    {
        using var rsa = RSA.Create(2048);
        using var cancel = new CancellationTokenSource();
        var enrollments = 0;
        using var http = new HttpClient(new FakeHandler(async (request, ct) => {
            if (AuthResponse(request) is { } auth) return auth;
            enrollments++;
            if (failure == "network") throw new HttpRequestException("secret+pin/001");
            if (failure == "cancelled") { cancel.Cancel(); await Task.Delay(Timeout.Infinite, ct); }
            return failure switch {
                "api" => FakeHandler.Json("""{"ResponseCode":"01","ResponseText":"Bank refused enrollment","RequestId":"request-1"}"""),
                "unauthorized" => FakeHandler.Json("{}", HttpStatusCode.Forbidden),
                _ => FakeHandler.Json("""{"ResponseCode":"00"}""")
            };
        }));
        using var client = new ISECureClient(Options(rsa), http);
        await client.LoginAsync("password");
        var error = await Record.ExceptionAsync(() => client.EnrollCertificateAsync("company", "user", "secret+pin/001", cancel.Token));
        Assert.NotNull(error);
        Assert.DoesNotContain("secret+pin/001", error.ToString());
        switch (failure) {
            case "api": Assert.Equal("request-1", Assert.IsType<ISecureApiException>(error).RequestId); break;
            case "network": Assert.IsType<ISecureNetworkException>(error); break;
            case "unauthorized": Assert.IsType<ISecureHttpException>(error); break;
            case "cancelled": Assert.IsAssignableFrom<OperationCanceledException>(error); break;
            default: Assert.IsType<ISecureProtocolException>(error); break;
        }
        Assert.Equal(1, enrollments);
        if (failure == "unauthorized") {
            await Assert.ThrowsAsync<ISecureAuthException>(() => client.EnrollCertificateAsync("company", "user", "pin"));
            Assert.Equal(1, enrollments);
        }
    }
}
