using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ISECure.Authentication;
using ISECure.Internal;
namespace ISECure.Client.Tests;

public class AuthenticationTests : IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);
    private const string Challenge = "nonce|1789841000000|request-id";
    private const string Success = """{"ResponseCode":"00","ResponseText":"Login OK","ApiKey":"tenant-A","IdToken":"private-token","ExpiresIn":"3600","Account":{"Type":"integrator","Features":["module.preview"],"Entitlements":[]}}""";
    private const string Init = """{"ResponseCode":"00","ResponseText":"OK","Challenge":"nonce|1789841000000|request-id"}""";
    private ClientOptions Options(string tenant = "tenant-A", AccountMode mode = AccountMode.Admin) => new(new Uri("https://example.invalid/v2"),
        _rsa.ExportSubjectPublicKeyInfoPem(), "synthetic+cs@example.invalid", mode, tenant, "generic-bank", "Synthetic", "User", "+358400100001");
    public void Dispose() => _rsa.Dispose();

    [Theory]
    [InlineData("Secret123!", false)]
    [InlineData("Salasana-äöå-🔐", false)]
    [InlineData("Salasana-äöå-🔐", true)]
    public void EncryptionIsUtf8OaepSha1AndSupportsBothPublicPemFormats(string password, bool pkcs1)
    {
        var pem = pkcs1 ? _rsa.ExportRSAPublicKeyPem() : _rsa.ExportSubjectPublicKeyInfoPem();
        var encrypted = ChallengeEncryption.Encrypt(pem, Challenge, password);
        Assert.Equal(password + "||1789841000000", Encoding.UTF8.GetString(_rsa.Decrypt(Convert.FromBase64String(encrypted), RSAEncryptionPadding.OaepSHA1)));
    }
    [Theory]
    [InlineData("")]
    [InlineData("nonce|x|id")]
    [InlineData("nonce|1789841000000")]
    [InlineData("nonce|1789841000000|id|extra")]
    [InlineData("|1789841000000|id")]
    public void MalformedChallengesAreRejected(string value) => Assert.Throws<ISecureProtocolException>(() => ChallengeEncryption.Encrypt(_rsa.ExportSubjectPublicKeyInfoPem(), value, "secret"));
    [Fact]
    public void RsaByteLimitIsCheckedBeforeEncryption() => Assert.Throws<ArgumentException>(() => ChallengeEncryption.Encrypt(_rsa.ExportSubjectPublicKeyInfoPem(), Challenge, new string('ä', 120)));

    [Theory]
    [InlineData("SMS_MFA", MfaMethod.Sms)]
    [InlineData("SOFTWARE_TOKEN_MFA", MfaMethod.Totp)]
    public async Task MfaCompletesTheOriginalChallengeWithoutStartingANewLogin(string challengeName, MfaMethod method)
    {
        var paths = new List<string>();
        using var http = new HttpClient(new FakeHandler(async (r, ct) => {
            paths.Add(r.Method + " " + r.RequestUri!.AbsolutePath);
            if (r.Method == HttpMethod.Get) return FakeHandler.Json(Init);
            if (r.Method == HttpMethod.Post) return FakeHandler.Json(JsonSerializer.Serialize(new { ResponseCode = "00", ResponseText = "Give code", ChallengeName = challengeName, Session = "mfa-session" }));
            using var body = JsonDocument.Parse(await r.Content!.ReadAsStringAsync(ct));
            Assert.Equal("mfa-session", body.RootElement.GetProperty("Session").GetString());
            Assert.Equal(challengeName, body.RootElement.GetProperty("ChallengeName").GetString());
            Assert.False(body.RootElement.TryGetProperty("SetupTOTP", out _));
            return FakeHandler.Json(Success);
        }));
        using var client = new ISECureClient(Options(), http);
        var first = await client.LoginAsync("secret");
        Assert.Equal(AuthStatus.NeedsMfa, first.Status); Assert.Equal(method, first.Method);
        var final = await client.SubmitMfaCodeAsync("123456");
        Assert.Equal(AuthStatus.Authenticated, final.Status);
        Assert.Contains("module.preview", final.Account!.Features);
        Assert.Equal(3, paths.Count);
        Assert.EndsWith("/mfacode", paths.Last());
        Assert.DoesNotContain("private-token", final.ToString());
    }

    [Fact]
    public async Task FactorSelectionPreservesReturnedSessionAndRejectsUnsupportedChoice()
    {
        using var http = new HttpClient(new FakeHandler(async (r, ct) => {
            if (r.Method == HttpMethod.Get) return FakeHandler.Json(Init);
            if (r.Method == HttpMethod.Post) return FakeHandler.Json("""{"ResponseCode":"00","ResponseText":"Select MFA type","ChallengeName":"SELECT_MFA_TYPE","Session":"selection","MfaOptions":["SOFTWARE_TOKEN_MFA"]}""");
            using var body = JsonDocument.Parse(await r.Content!.ReadAsStringAsync(ct));
            if (r.RequestUri!.AbsolutePath.EndsWith("selectmfa")) {
                Assert.Equal("selection", body.RootElement.GetProperty("Session").GetString());
                Assert.Equal("SOFTWARE_TOKEN_MFA", body.RootElement.GetProperty("MfaType").GetString());
                return FakeHandler.Json("""{"ResponseCode":"00","ResponseText":"Give authenticator code","ChallengeName":"SOFTWARE_TOKEN_MFA","Session":"selected"}""");
            }
            Assert.Equal("selected", body.RootElement.GetProperty("Session").GetString());
            return FakeHandler.Json(Success);
        }));
        using var client = new ISECureClient(Options(), http);
        Assert.Equal(AuthStatus.NeedsMfaSelection, (await client.LoginAsync("secret")).Status);
        await Assert.ThrowsAsync<ISecureAuthException>(() => client.SelectMfaTypeAsync(MfaMethod.Sms));
        Assert.Equal(MfaMethod.Totp, (await client.SelectMfaTypeAsync(MfaMethod.Totp)).Method);
        Assert.Equal(AuthStatus.Authenticated, (await client.SubmitMfaCodeAsync("123456")).Status);
    }

    [Theory]
    [InlineData("Phone", "Verify phone number with received SMS code")]
    [InlineData("Email", "Login OK. Verify email address.")]
    public async Task VerificationPrecedesMfaAndRequiresFreshLogin(string kind, string text)
    {
        var verified = false;
        using var http = new HttpClient(new FakeHandler(async (r, ct) => {
            if (r.Method == HttpMethod.Get) return FakeHandler.Json(Init);
            if (r.RequestUri!.AbsolutePath.Contains("/session/"))
                return FakeHandler.Json(verified ? Success : JsonSerializer.Serialize(new { ResponseCode = "00", ResponseText = text, Session = "overlapping-session", AccessToken = kind == "Email" ? "email-access" : null }));
            using var body = JsonDocument.Parse(await r.Content!.ReadAsStringAsync(ct));
            Assert.Equal("123456", body.RootElement.GetProperty("Code").GetString());
            if (kind == "Email") Assert.Equal("email-access", body.RootElement.GetProperty("AccessToken").GetString());
            verified = true; return FakeHandler.Json(Fixture.Ok);
        }));
        using var client = new ISECureClient(Options(), http);
        Assert.Equal(kind == "Email" ? AuthStatus.NeedsEmailVerification : AuthStatus.NeedsPhoneVerification, (await client.LoginAsync("secret")).Status);
        var accepted = kind == "Email" ? await client.VerifyEmailAsync("123456") : await client.VerifyPhoneAsync("123456");
        Assert.Equal(AuthStatus.VerificationAccepted, accepted.Status);
        await Assert.ThrowsAsync<ISecureAuthException>(() => client.SubmitMfaCodeAsync("123456"));
        Assert.Equal(AuthStatus.Authenticated, (await client.LoginAsync("secret")).Status);
    }

    [Fact]
    public async Task SetupTotpReturnsSecretOnlyToCallerAndVerifiesExplicitToken()
    {
        using var http = new HttpClient(new FakeHandler(async (r, ct) => {
            if (r.Method == HttpMethod.Get) return FakeHandler.Json(Init);
            if (r.Method == HttpMethod.Post) return FakeHandler.Json("""{"ResponseCode":"00","ResponseText":"Give SMS code","Session":"session","ChallengeName":"SMS_MFA"}""");
            using var body = JsonDocument.Parse(await r.Content!.ReadAsStringAsync(ct));
            if (r.RequestUri!.AbsolutePath.EndsWith("verifytotp")) {
                Assert.Equal("enrollment-token", body.RootElement.GetProperty("AccessToken").GetString()); return FakeHandler.Json(Fixture.Ok);
            }
            Assert.True(body.RootElement.GetProperty("SetupTOTP").GetBoolean());
            return FakeHandler.Json(Success.Replace("\"ExpiresIn\"", "\"SecretCode\":\"secret-seed\",\"OtpauthUri\":\"otpauth://totp/example\",\"AccessToken\":\"enrollment-token\",\"ExpiresIn\""));
        }));
        using var client = new ISECureClient(Options(), http);
        await client.LoginAsync("secret");
        var result = await client.SubmitMfaCodeAsync("123456", setupTotp: true);
        Assert.NotNull(result.TotpEnrollment);
        Assert.DoesNotContain("secret-seed", result.TotpEnrollment.ToString());
        Assert.Equal(AuthStatus.VerificationAccepted, (await client.VerifyTotpAsync(result.TotpEnrollment.AccessToken, "654321")).Status);
    }

    [Fact]
    public async Task FailedMfaClearsChallengeAndMapsReason()
    {
        var handler = new FakeHandler((r, _) => Task.FromResult(FakeHandler.Json(r.Method == HttpMethod.Get ? Init : r.Method == HttpMethod.Post ?
            """{"ResponseCode":"00","ResponseText":"Give SMS code","Session":"session"}""" :
            """{"ResponseCode":"01","ResponseText":"Incorrect code"}""", r.Method == HttpMethod.Put ? HttpStatusCode.BadRequest : HttpStatusCode.OK)));
        using var http = new HttpClient(handler); using var client = new ISECureClient(Options(), http);
        await client.LoginAsync("secret");
        var result = await client.SubmitMfaCodeAsync("123456");
        Assert.Equal(AuthStatus.Failed, result.Status); Assert.Equal(AuthFailureReason.InvalidCode, result.FailureReason);
        await Assert.ThrowsAsync<ISecureAuthException>(() => client.SubmitMfaCodeAsync("123456"));
        Assert.Equal(3, handler.Calls);
    }

    [Theory]
    [InlineData("{\"ResponseCode\":\"00\",\"ResponseText\":\"Give code\",\"Session\":\"x\",\"ChallengeName\":\"NEW_CHALLENGE\"}")]
    [InlineData("{\"ResponseCode\":\"00\",\"ResponseText\":\"Select MFA type\",\"Session\":\"x\",\"ChallengeName\":\"SELECT_MFA_TYPE\",\"MfaOptions\":[\"UNKNOWN\"]}")]
    [InlineData("{\"ResponseCode\":\"00\",\"ResponseText\":\"Verify email\"}")]
    [InlineData("{\"ResponseCode\":\"00\",\"ResponseText\":\"Give SMS code\"}")]
    public async Task UnknownOrIncompleteAuthenticationFailsClosed(string response)
    {
        using var http = new HttpClient(new FakeHandler((r, _) => Task.FromResult(FakeHandler.Json(r.Method == HttpMethod.Get ? Init : response))));
        using var client = new ISECureClient(Options(), http);
        await Assert.ThrowsAsync<ISecureProtocolException>(() => client.LoginAsync("secret"));
        await Assert.ThrowsAsync<ISecureAuthException>(() => client.SubmitMfaCodeAsync("123456"));
    }

    [Fact]
    public async Task RegistrationEncryptsPasswordAndAdoptsNewTenant()
    {
        var count = 0;
        using var http = new HttpClient(new FakeHandler(async (r, ct) => {
            ++count;
            if (r.Method == HttpMethod.Get) return FakeHandler.Json(Init);
            using var json = JsonDocument.Parse(await r.Content!.ReadAsStringAsync(ct));
            if (r.Method == HttpMethod.Put) {
                Assert.Equal("0", json.RootElement.GetProperty("ApiKey").GetString());
                Assert.Equal("päss🔐||1789841000000", Encoding.UTF8.GetString(_rsa.Decrypt(Convert.FromBase64String(json.RootElement.GetProperty("Encrypted").GetString()!), RSAEncryptionPadding.OaepSHA1)));
                return FakeHandler.Json("""{"ResponseCode":"00","ResponseText":"Registered","ApiKey":"tenant-A"}""", HttpStatusCode.Created);
            }
            Assert.Equal("tenant-A", r.Headers.GetValues("x-api-key").Single());
            return FakeHandler.Json(Success);
        }));
        using var client = new ISECureClient(Options("0"), http);
        Assert.Equal("tenant-A", (await client.RegisterAsync("päss🔐")).ApiKey);
        Assert.Equal(AuthStatus.Authenticated, (await client.LoginAsync("päss🔐")).Status);
        Assert.Equal(4, count);
    }

    [Fact]
    public async Task LogoutClearsSessionEvenWhenNetworkFails()
    {
        var deletes = 0;
        using var http = new HttpClient(new FakeHandler((r, _) => {
            if (r.Method == HttpMethod.Delete) { deletes++; Assert.Equal("private-token", r.Headers.GetValues("Authorization").Single()); throw new HttpRequestException(); }
            return Task.FromResult(FakeHandler.Json(r.Method == HttpMethod.Get ? Init : Success));
        }));
        using var client = new ISECureClient(Options(), http);
        await client.LoginAsync("secret");
        await Assert.ThrowsAsync<ISecureNetworkException>(() => client.LogoutAsync());
        await client.LogoutAsync(); Assert.Equal(1, deletes);
    }

    [Fact]
    public async Task FailedReloginCannotReuseOldToken()
    {
        var loginCount = 0; var deletes = 0;
        using var http = new HttpClient(new FakeHandler((r, _) => {
            if (r.Method == HttpMethod.Get) return Task.FromResult(FakeHandler.Json(Init));
            if (r.Method == HttpMethod.Delete) deletes++;
            return Task.FromResult(FakeHandler.Json(++loginCount == 1 ? Success : """{"ResponseCode":"01","ResponseText":"Invalid credentials"}"""));
        }));
        using var client = new ISECureClient(Options(), http);
        await client.LoginAsync("secret");
        Assert.Equal(AuthStatus.Failed, (await client.LoginAsync("wrong")).Status);
        await client.LogoutAsync(); Assert.Equal(0, deletes);
    }

    [Theory]
    [InlineData("tenant-B", "3600")]
    [InlineData("tenant-A", "0")]
    [InlineData("tenant-A", "not-a-number")]
    public async Task TenantMismatchOrBadExpiryCannotEstablishSession(string tenant, string expiry)
    {
        using var http = new HttpClient(new FakeHandler((r, _) => Task.FromResult(FakeHandler.Json(r.Method == HttpMethod.Get ? Init : Success.Replace("tenant-A", tenant).Replace("3600", expiry)))));
        using var client = new ISECureClient(Options(), http);
        await Assert.ThrowsAsync<ISecureProtocolException>(() => client.LoginAsync("secret"));
    }
    [Fact]
    public async Task CancelledLogoutStillClearsLocalSession()
    {
        var deletes = 0;
        using var http = new HttpClient(new FakeHandler((r, ct) => {
            if (r.Method == HttpMethod.Delete) { deletes++; ct.ThrowIfCancellationRequested(); }
            return Task.FromResult(FakeHandler.Json(r.Method == HttpMethod.Get ? Init : Success));
        }));
        using var client = new ISECureClient(Options(), http);
        await client.LoginAsync("secret");
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.LogoutAsync(cts.Token));
        await client.LogoutAsync(); Assert.True(deletes <= 1);
    }

    [Fact]
    public async Task DisposingDuringLoginCannotPublishCredentialsOrMfaState()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new FakeHandler(async (r, _) => {
            if (r.Method == HttpMethod.Get) return FakeHandler.Json(Init);
            started.SetResult(); await finish.Task; return FakeHandler.Json(Success);
        }));
        var client = new ISECureClient(Options(), http);
        var login = client.LoginAsync("secret");
        await started.Task; client.Dispose(); finish.SetResult();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => login);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.LogoutAsync());
    }

    [Fact]
    public async Task ConcurrentReloginAndLogoutAreSerialized()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deletes = 0;
        using var http = new HttpClient(new FakeHandler(async (r, _) => {
            if (r.Method == HttpMethod.Get) return FakeHandler.Json(Init);
            if (r.Method == HttpMethod.Delete) { deletes++; return FakeHandler.Json(Fixture.Ok); }
            entered.SetResult(); await finish.Task; return FakeHandler.Json(Success);
        }));
        using var client = new ISECureClient(Options(), http);
        var login = client.LoginAsync("secret"); await entered.Task;
        var logout = client.LogoutAsync(); Assert.False(logout.IsCompleted);
        finish.SetResult(); await login; await logout;
        await client.LogoutAsync(); Assert.Equal(1, deletes);
    }

    [Fact]
    public async Task WrongVerificationCodeReturnsFailureAndClearsPendingToken()
    {
        using var http = new HttpClient(new FakeHandler((r, _) => Task.FromResult(FakeHandler.Json(r.Method == HttpMethod.Get ? Init :
            r.RequestUri!.AbsolutePath.Contains("/session/") ? """{"ResponseCode":"00","ResponseText":"Verify email","AccessToken":"email-token"}""" :
            """{"ResponseCode":"01","ResponseText":"Code has expired"}"""))));
        using var client = new ISECureClient(Options(), http);
        await client.LoginAsync("secret");
        Assert.Equal(AuthFailureReason.ExpiredCode, (await client.VerifyEmailAsync("123456")).FailureReason);
        await Assert.ThrowsAsync<ISecureAuthException>(() => client.VerifyEmailAsync("123456"));
    }

    [Fact]
    public async Task NumericCognitoExpiryIsAcceptedAndExpiredSessionCanBeLoggedOut()
    {
        var clock = new ManualTime(); var deletes = 0;
        using var http = new HttpClient(new FakeHandler((r, _) => {
            if (r.Method == HttpMethod.Delete) { deletes++; return Task.FromResult(FakeHandler.Json(Fixture.Ok)); }
            return Task.FromResult(FakeHandler.Json(r.Method == HttpMethod.Get ? Init : Success.Replace("\"3600\"", "3600")));
        }));
        using var client = new ISECureClient(Options(), http, clock);
        Assert.Equal(AuthStatus.Authenticated, (await client.LoginAsync("secret")).Status);
        clock.Now += TimeSpan.FromHours(2);
        await client.LogoutAsync(); await client.LogoutAsync(); Assert.Equal(1, deletes);
    }

}
