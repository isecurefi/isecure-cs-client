using ISECure.Internal;
namespace ISECure.Client.Tests;
internal sealed class ManualTime : TimeProvider
{
    internal DateTimeOffset Now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
}
public class SessionTests
{
    [Fact]
    public void SessionIsolationExpiryAndClearingAreIndependent()
    {
        var clock = new ManualTime();
        using var a = new ClientSession(clock); using var b = new ClientSession(clock);
        a.Establish("tenant-A", "token-A", TimeSpan.FromSeconds(1));
        b.Establish("tenant-B", "token-B", TimeSpan.FromMinutes(1));
        Assert.Equal(("tenant-A", "token-A"), a.Require("ListFiles"));
        Assert.Equal(("tenant-B", "token-B"), b.Require("ListFiles"));
        clock.Now += TimeSpan.FromSeconds(1);
        Assert.Throws<ISecureAuthException>(() => a.Require("ListFiles"));
        Assert.Equal("token-B", b.Require("ListFiles").IdToken);
        b.Clear(); Assert.Throws<ISecureAuthException>(() => b.Require("ListFiles"));
    }
    [Fact]
    public void DisposalCannotResurrectASessionOrRevealTokens()
    {
        var session = new ClientSession(TimeProvider.System);
        session.Establish("tenant", "secret", TimeSpan.FromMinutes(1));
        Assert.DoesNotContain("secret", session.ToString());
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() => session.Establish("tenant", "secret", TimeSpan.FromMinutes(1)));
        Assert.Throws<ObjectDisposedException>(() => session.Require("ListFiles"));
    }
}
