using System.Net;
using ISECure.Internal;
namespace ISECure.Client.Tests;

public class TransportTests
{
    [Fact]
    public async Task SharedHttpClientDoesNotMixTenantOrSessionHeaders()
    {
        var seen = new System.Collections.Concurrent.ConcurrentBag<string>();
        using var http = new HttpClient(new FakeHandler(async (r, ct) => {
            await Task.Delay(10, ct);
            seen.Add(r.Headers.GetValues("x-api-key").Single() + ":" + r.Headers.GetValues("Authorization").Single());
            return FakeHandler.Json(Fixture.Ok);
        }));
        var a = new ApiTransport(http, Fixture.Options("A"), null);
        var b = new ApiTransport(http, Fixture.Options("B"), null);
        await Task.WhenAll(a.SendAsync("ListFiles", HttpMethod.Get, "files/bank", null, "A", "token-A", default),
            b.SendAsync("ListFiles", HttpMethod.Get, "files/bank", null, "B", "token-B", default));
        Assert.Equal(new[] { "A:token-A", "B:token-B" }, seen.Order().ToArray());
        Assert.Empty(http.DefaultRequestHeaders);
    }

    [Theory]
    [InlineData(400, "{\"ResponseCode\":\"01\",\"ResponseText\":\"private-token\",\"RequestId\":\"request-1\"}", typeof(ISecureApiException))]
    [InlineData(200, "{\"ResponseCode\":\"01\",\"ResponseText\":\"private-token\"}", typeof(ISecureApiException))]
    [InlineData(403, "{\"message\":\"private-token\"}", typeof(ISecureHttpException))]
    [InlineData(502, "<html>private-token</html>", typeof(ISecureHttpException))]
    [InlineData(302, "private-token", typeof(ISecureHttpException))]
    [InlineData(200, "{\"ResponseCode\":\"00\"}", typeof(ISecureProtocolException))]
    [InlineData(200, "null", typeof(ISecureProtocolException))]
    [InlineData(200, "invalid private-token", typeof(ISecureProtocolException))]
    public async Task FailuresAreTypedAndNeverDumpSecrets(int status, string body, Type type)
    {
        var events = new List<SdkDiagnostic>();
        var handler = new FakeHandler((_, _) => Task.FromResult(FakeHandler.Json(body, (HttpStatusCode)status)));
        using var http = new HttpClient(handler);
        var transport = new ApiTransport(http, Fixture.Options(), events.Add);
        var ex = await Assert.ThrowsAsync(type, () => transport.SendAsync("UploadFile", HttpMethod.Put, "files/bank", new { Password = "private-token" }, "tenant-A", "private-token", default));
        Assert.DoesNotContain("private-token", ex.ToString());
        Assert.DoesNotContain("private-token", string.Join("|", events));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task NetworkErrorsDoNotExposeInnerUriOrSecret()
    {
        using var http = new HttpClient(new FakeHandler((_, _) => throw new HttpRequestException("private-token")));
        var transport = new ApiTransport(http, Fixture.Options(), null);
        var ex = await Assert.ThrowsAsync<ISecureNetworkException>(() => transport.SendAsync("Login", HttpMethod.Post, "session/user/data", null, "tenant", null, default));
        Assert.Null(ex.InnerException);
        Assert.DoesNotContain("private-token", ex.ToString());
    }

    [Fact]
    public async Task RequestTimeoutIsDistinctFromCallerCancellation()
    {
        using var http = new HttpClient(new FakeHandler(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return FakeHandler.Json(Fixture.Ok); }));
        var transport = new ApiTransport(http, Fixture.Options(timeout: TimeSpan.FromMilliseconds(30)), null);
        await Assert.ThrowsAsync<ISecureTimeoutException>(() => transport.SendAsync("Login", HttpMethod.Post, "session", null, "tenant", null, default));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transport.SendAsync("Login", HttpMethod.Post, "session", null, "tenant", null, cancelled.Token));
    }

    [Fact]
    public async Task OversizedResponseIsRejected()
    {
        using var http = new HttpClient(new FakeHandler((_, _) => Task.FromResult(FakeHandler.Json(new string('x', 2048)))));
        var transport = new ApiTransport(http, Fixture.Options(max: 1024), null);
        await Assert.ThrowsAsync<ISecureProtocolException>(() => transport.SendAsync("DownloadFile", HttpMethod.Get, "files", null, "tenant", null, default));
    }

    [Fact]
    public async Task BrokenDiagnosticSinkDoesNotChangeSuccess()
    {
        using var http = new HttpClient(new FakeHandler((_, _) => Task.FromResult(FakeHandler.Json(Fixture.Ok))));
        var transport = new ApiTransport(http, Fixture.Options(), _ => throw new InvalidOperationException());
        await transport.SendAsync("ListFiles", HttpMethod.Get, "files", null, "tenant", null, default);
    }

    [Theory]
    [InlineData("http://example.invalid")]
    [InlineData("https://user:password@example.invalid")]
    [InlineData("https://example.invalid/v2?tenant=1")]
    [InlineData("https://example.invalid/v2#fragment")]
    public void UnsafeBaseUrisAreRejected(string uri) => Assert.Throws<ArgumentException>(() =>
        new ClientOptions(new Uri(uri), "public", "email", AccountMode.Data, "tenant", "bank", "company", "name", "phone"));

    [Fact]
    public void PathsAreEscapedAndDotSegmentsRejected()
    {
        Assert.Equal("a%2Fb%3Fsecret%3D1", ISECureClient.Segment("a/b?secret=1"));
        Assert.Throws<ArgumentException>(() => ISECureClient.Segment(".."));
    }

    [Fact]
    public void DefaultHeadersAreRejectedAndExternalClientIsNotDisposed()
    {
        using var http = new HttpClient(new FakeHandler((_, _) => Task.FromResult(FakeHandler.Json(Fixture.Ok))));
        http.DefaultRequestHeaders.Add("Authorization", "secret");
        Assert.Throws<ArgumentException>(() => new ISECureClient(Fixture.Options(), http));
        http.DefaultRequestHeaders.Clear();
        using (var client = new ISECureClient(Fixture.Options(), http))
            Assert.DoesNotContain("synthetic", client.ToString());
        http.DefaultRequestHeaders.Add("AfterDispose", "usable");
    }
}
