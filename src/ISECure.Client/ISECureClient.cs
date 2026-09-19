using ISECure.Internal;
namespace ISECure;

/// <summary>One user/role/tenant session. Operations on an instance are serialized.</summary>
public sealed partial class ISECureClient : IDisposable
{
    private readonly ClientOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly ApiTransport _transport;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string _apiKey;
    private readonly ClientSession _authenticated;
    private volatile bool _disposed;

    /// <summary>Creates an isolated account session.</summary>
    /// <param name="options">Immutable account, tenant, bank and environment configuration.</param>
    /// <param name="httpClient">Optional caller-owned HTTP client with no default headers/credentials, cookies, redirects or retries.</param>
    /// <param name="timeProvider">Optional clock for session expiry; defaults to TimeProvider.System.</param>
    /// <param name="diagnostic">Optional operation/status/outcome callback. No payloads or credentials are emitted; callback exceptions are ignored.</param>
    /// <remarks>The client serializes its own operations. Instances own independent local state; server logout may revoke other sessions of the same account/role. Dispose the SDK client when finished.</remarks>
    /// <exception cref="ArgumentException">The injected HTTP client contains default headers.</exception>
    public ISECureClient(ClientOptions options, HttpClient? httpClient = null,
        TimeProvider? timeProvider = null, Action<SdkDiagnostic>? diagnostic = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (httpClient?.DefaultRequestHeaders.Any() == true)
            throw new ArgumentException("Injected HttpClient must not have default headers.", nameof(httpClient));
        _options = options;
        _apiKey = options.ApiKey;
        _http = httpClient ?? new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };
        _ownsHttp = httpClient is null;
        _transport = new(_http, options, diagnostic);
        _time = timeProvider ?? TimeProvider.System;
        _authenticated = new(_time);
    }
    internal static string Segment(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value is "." or "..") throw new ArgumentException("Dot path segments are not allowed.", nameof(value));
        return Uri.EscapeDataString(value);
    }
    private string Mode => _options.Mode == AccountMode.Admin ? "admin" : "data";
    private string AccountPath => $"account/{Segment(_options.Email)}/{Mode}";
    private string SessionPath => $"session/{Segment(_options.Email)}/{Mode}";
    private async Task<T> ExclusiveAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { ObjectDisposedException.ThrowIf(_disposed, this); return await action().ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
    private (string ApiKey, string IdToken) RequireSession(string operation) => _authenticated.Require(operation);
    private void ClearSession() => _authenticated.Clear();
    /// <summary>Clears local secrets and disposes an owned HTTP client. Does not call server logout.</summary>
    public void Dispose()
    {
        lock (_authSync)
        {
            _disposed = true;
            ResetAuthentication();
            _authenticated.Dispose();
        }
        if (_ownsHttp) _http.Dispose();
    }
    /// <summary>Returns a fixed label without account details or credentials.</summary>
    public override string ToString() => "ISECureClient";
}
