using System.Net;
using System.Security.Cryptography;
using ISECure.Authentication;
using ISECure.Examples;

namespace ISECure.Client.Tests;

public class ExampleTests
{
    [Theory]
    [InlineData("SMS_MFA", MfaMethod.Sms)]
    [InlineData("SOFTWARE_TOKEN_MFA", MfaMethod.Totp)]
    public async Task LoginRecipeUsesTheOfferedMethodAndOneCode(string wireMethod, MfaMethod method)
    {
        using var rsa = RSA.Create(2048);
        var requests = 0; var prompts = 0;
        using var http = new HttpClient(new FakeHandler((_, _) => Task.FromResult(FakeHandler.Json(++requests switch {
            1 => Challenge,
            2 => "{\"ResponseCode\":\"00\",\"ResponseText\":\"Select\",\"ChallengeName\":\"SELECT_MFA_TYPE\",\"Session\":\"first\",\"MfaOptions\":[\"" + wireMethod + "\"]}",
            3 => "{\"ResponseCode\":\"00\",\"ResponseText\":\"Code\",\"ChallengeName\":\"" + wireMethod + "\",\"Session\":\"second\"}",
            4 => Authenticated,
            _ => throw new InvalidOperationException("Unexpected retry")
        }))));
        using var client = new ISECureClient(Options(rsa), http);
        var result = await AuthenticationExamples.LoginWithMfaAsync(client, "password", offered => {
            Assert.Equal(method, Assert.Single(offered)); return method;
        }, (requested, _) => { Assert.Equal(method, requested); prompts++; return Task.FromResult("123456"); });
        Assert.Equal(AuthStatus.Authenticated, result.Status);
        Assert.Equal(4, requests); Assert.Equal(1, prompts);
    }

    [Fact]
    public async Task LoginRecipeReturnsRefusalWithoutRepeatedPrompts()
    {
        using var rsa = RSA.Create(2048);
        var handler = new FakeHandler((r, _) => Task.FromResult(FakeHandler.Json(r.Method == HttpMethod.Get ? Challenge :
            """{"ResponseCode":"01","ResponseText":"Incorrect password"}""")));
        using var http = new HttpClient(handler); using var client = new ISECureClient(Options(rsa), http);
        var result = await AuthenticationExamples.LoginWithMfaAsync(client, "password",
            _ => throw new InvalidOperationException("No selection expected"), (_, _) => throw new InvalidOperationException("No code expected"));
        Assert.Equal(AuthStatus.Failed, result.Status); Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData("api")]
    [InlineData("network")]
    [InlineData("http")]
    public async Task ErrorRecipeReportsSafeDetailsAndNeverRetriesUploads(string failure)
    {
        using var rsa = RSA.Create(2048);
        var uploads = 0;
        using var http = new HttpClient(new FakeHandler((r, _) => {
            if (r.RequestUri!.AbsolutePath.Contains("/session/")) return Task.FromResult(FakeHandler.Json(r.Method == HttpMethod.Get ? Challenge : Authenticated));
            uploads++;
            if (failure == "network") throw new HttpRequestException("private-sensitive-value");
            return Task.FromResult(failure == "api" ? FakeHandler.Json("""{"ResponseCode":"01","ResponseText":"private-sensitive-value","RequestId":"request-123"}""") : FakeHandler.Json("{}", HttpStatusCode.BadGateway));
        }));
        using var client = new ISECureClient(Options(rsa), http); await client.LoginAsync("password");
        var reports = new List<string>();
        Assert.False(await FileExamples.TryUploadAsync(client, [1, 2, 3], "file.xml", "type", "signature", reports.Add));
        Assert.Equal(1, uploads); Assert.DoesNotContain("private-sensitive-value", Assert.Single(reports));
    }

    private const string Challenge = """{"ResponseCode":"00","ResponseText":"OK","Challenge":"nonce|1789841000000|id"}""";
    private const string Authenticated = """{"ResponseCode":"00","ResponseText":"OK","ApiKey":"tenant-A","IdToken":"token","ExpiresIn":3600}""";
    private static ClientOptions Options(RSA rsa) => new(new Uri("https://example.invalid/v2"), rsa.ExportSubjectPublicKeyInfoPem(),
        "synthetic@example.invalid", AccountMode.Data, "tenant-A", "bank", "company", "name", "phone");
}
