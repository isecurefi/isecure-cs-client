namespace ISECure.Internal;

internal sealed class ClientSession(TimeProvider time) : IDisposable
{
    private readonly object _sync = new();
    private string? _token;
    private string? _tenant;
    private DateTimeOffset _expires;
    private bool _disposed;
    internal void Establish(string tenant, string token, TimeSpan lifetime)
    {
        if (string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(token) || lifetime <= TimeSpan.Zero)
            throw new ISecureProtocolException("Login");
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _tenant = tenant; _token = token; _expires = time.GetUtcNow() + lifetime;
        }
    }
    internal (string ApiKey, string IdToken) Require(string operation)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_token is null || _tenant is null || time.GetUtcNow() >= _expires)
            {
                Clear(); throw new ISecureAuthException(operation);
            }
            return (_tenant, _token);
        }
    }
    internal (string ApiKey, string IdToken)? TakeForLogout()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            (string, string)? result = _tenant is not null && _token is not null ? (_tenant, _token) : null;
            Clear(); return result;
        }
    }
    internal void Clear() { lock (_sync) { _token = null; _tenant = null; _expires = default; } }
    public void Dispose() { lock (_sync) { _disposed = true; Clear(); } }
    public override string ToString() => "ClientSession";
}
