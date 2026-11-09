using System.Security.Cryptography;
using System.Text.Json;
using ISECure.Internal;
namespace ISECure.Client.Tests;

public class PasswordProfileTests
{
    private static JsonDocument Vectors() => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/password-profile/vectors.json")));
    [Fact]
    public void NewProfileInteroperatesWithShared3072And4096Fixtures()
    {
        using var vectors = Vectors();
        foreach (var value in vectors.RootElement.GetProperty("Cases").EnumerateArray())
        {
            var key = vectors.RootElement.GetProperty("Keys").EnumerateArray().First(k => k.GetProperty("KeyId").GetString() == value.GetProperty("KeyId").GetString());
            var fields = JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(value)!;
            fields["PublicKey"] = key.GetProperty("PublicKey");
            var discovery = PasswordDiscovery.Parse(JsonSerializer.SerializeToElement(fields), PasswordProfiles.RsaOaepSha256V1);
            var ciphertext = discovery.Encrypt("unused legacy key", value.GetProperty("Password").GetString()!);
            using var rsa = RSA.Create(); rsa.ImportFromPem(key.GetProperty("PrivateKey").GetString()!);
            var frame = rsa.Decrypt(Convert.FromBase64String(ciphertext), RSAEncryptionPadding.OaepSHA256);
            Assert.Equal(value.GetProperty("FrameHex").GetString(), Convert.ToHexString(frame).ToLowerInvariant());
            Assert.Throws<ArgumentException>(() => discovery.Encrypt("", new string('x',284)));
        }
    }
    [Fact]
    public void OldServerCannotSilentlyDowngradeExplicitProfile()
    {
        using var legacy = JsonDocument.Parse("""{"Challenge":"nonce|1789841000000|id"}""");
        Assert.Throws<ISecureProtocolException>(() => PasswordDiscovery.Parse(legacy.RootElement, PasswordProfiles.RsaOaepSha256V1));
    }
    [Fact]
    public async Task ResetUsesItsOwnDiscoveryAndPostRouteOnce()
    {
        using var vectors = Vectors();
        var value = vectors.RootElement.GetProperty("Cases")[0]; var key = vectors.RootElement.GetProperty("Keys")[0];
        var calls = 0;
        using var http = new HttpClient(new FakeHandler(async (r,ct) => {
            calls++;
            Assert.Equal("/v2/account/synthetic%40example.invalid/admin/password", r.RequestUri!.AbsolutePath);
            if (r.Method == HttpMethod.Get) {
                Assert.Contains("EncryptionProfile=",r.RequestUri.Query);
                return FakeHandler.Json(JsonSerializer.Serialize(new { ResponseCode="00",ResponseText="Password reset started",Challenge=value.GetProperty("Challenge").GetString(),EncryptionProfile=PasswordProfiles.RsaOaepSha256V1,KeyId=key.GetProperty("KeyId").GetString(),PublicKey=key.GetProperty("PublicKey").GetString()}));
            }
            Assert.Equal(HttpMethod.Post,r.Method);
            using var body = JsonDocument.Parse(await r.Content!.ReadAsStringAsync(ct));
            Assert.Equal(value.GetProperty("Challenge").GetString(),body.RootElement.GetProperty("ChResp").GetString());
            Assert.Equal(PasswordProfiles.RsaOaepSha256V1,body.RootElement.GetProperty("EncryptionProfile").GetString());
            return FakeHandler.Json("""{"ResponseCode":"00","ResponseText":"Password reset complete"}""");
        }));
        using var client = new ISECureClient(new ClientOptions(new Uri("https://example.invalid/v2"),"unused", "synthetic@example.invalid",AccountMode.Admin,"tenant", "bank","company","name","+358400000000"),http);
        await client.InitPasswordResetAsync(); await client.ResetPasswordAsync("123456","Synthetic-password-42!");
        await Assert.ThrowsAsync<ISecureAuthException>(() => client.ResetPasswordAsync("123456","Again-42!"));
        Assert.Equal(2,calls);
    }
    [Theory]
    [InlineData("login")]
    [InlineData("register")]
    public async Task DefaultDiscoveryUsesProfileAndRefusesOldServer(string operation)
    {
        var calls = 0;
        using var http = new HttpClient(new FakeHandler((r, ct) => {
            calls++;
            Assert.Equal(HttpMethod.Get, r.Method);
            Assert.Contains("EncryptionProfile=password-rsa-oaep-sha256-v1", r.RequestUri!.Query);
            if (operation == "register") Assert.Contains("ApiKey=tenant", r.RequestUri.Query);
            return Task.FromResult(FakeHandler.Json("""{"ResponseCode":"00","Challenge":"old-server"}"""));
        }));
        var options = new ClientOptions(new Uri("https://example.invalid/v2"), "unused", "synthetic@example.invalid",
            AccountMode.Data, "tenant", "bank", "company", "name", "+358400000000");
        Assert.Equal(PasswordProfiles.RsaOaepSha256V1, options.EncryptionProfile);
        using var client = new ISECureClient(options, http);
        if (operation == "login") await Assert.ThrowsAsync<ISecureProtocolException>(() => client.LoginAsync("Synthetic-42!"));
        else await Assert.ThrowsAsync<ISecureProtocolException>(() => client.RegisterAsync("Synthetic-42!"));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void LegacyMustBeExplicitAndInvalidProfilesAreRejected()
    {
        ClientOptions Options(string? profile) => new(new Uri("https://example.invalid/v2"), "unused",
            "synthetic@example.invalid", AccountMode.Data, "tenant", "bank", "company", "name", "+358400000000",
            encryptionProfile: profile);
        Assert.Equal(PasswordProfiles.RsaOaepSha256V1, Options(null).EncryptionProfile);
        Assert.Null(Options(PasswordProfiles.Legacy).EncryptionProfile);
        Assert.Throws<ArgumentException>(() => Options("unsupported"));
    }
}
