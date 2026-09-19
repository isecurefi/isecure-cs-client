using System.Net;
using System.Text;
using ISECure.Internal;
namespace ISECure.Client.Tests;

internal sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    internal int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    { Interlocked.Increment(ref Calls); return send(request, cancellationToken); }
    internal static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
internal static class Fixture
{
    internal static ClientOptions Options(string tenant = "tenant-A", TimeSpan? timeout = null, int max = 8388608) =>
        new(new Uri("https://example.invalid/v2"), "public-key", "synthetic@example.invalid", AccountMode.Data,
            tenant, "generic-bank", "Synthetic Company", "Synthetic User", "+358400100001", timeout, max);
    internal const string Ok = """{"ResponseCode":"00","ResponseText":"OK"}""";
}
