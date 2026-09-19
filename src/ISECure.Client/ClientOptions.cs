namespace ISECure;

public enum AccountMode { Admin, Data }

/// <summary>Immutable configuration. Passwords are passed to authentication methods, not retained here.</summary>
public sealed class ClientOptions
{
    public Uri BaseUri { get; }
    public string PublicKeyPem { get; }
    public string Email { get; }
    public AccountMode Mode { get; }
    public string ApiKey { get; }
    public string Bank { get; }
    public string Company { get; }
    public string Name { get; }
    public string Phone { get; }
    public TimeSpan RequestTimeout { get; }
    public int MaximumResponseBytes { get; }

    public ClientOptions(Uri baseUri, string publicKeyPem, string email, AccountMode mode,
        string apiKey, string bank, string company, string name, string phone,
        TimeSpan? requestTimeout = null, int maximumResponseBytes = 8 * 1024 * 1024)
    {
        if (!baseUri.IsAbsoluteUri || baseUri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(baseUri.UserInfo) || !string.IsNullOrEmpty(baseUri.Query) ||
            !string.IsNullOrEmpty(baseUri.Fragment))
            throw new ArgumentException("Base URI must be an absolute HTTPS URI without credentials, query or fragment.", nameof(baseUri));
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        foreach (var value in new[] { publicKeyPem, email, apiKey, bank, company, name, phone })
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (apiKey.Any(c => c < 33 || c > 126)) throw new ArgumentException("Invalid API key format.", nameof(apiKey));
        var timeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5)) throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        if (maximumResponseBytes < 1024 || maximumResponseBytes > 64 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maximumResponseBytes));
        BaseUri = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/");
        PublicKeyPem = publicKeyPem; Email = email; Mode = mode; ApiKey = apiKey;
        Bank = bank; Company = company; Name = name; Phone = phone;
        RequestTimeout = timeout; MaximumResponseBytes = maximumResponseBytes;
    }
    public override string ToString() => $"ClientOptions ({Mode})";
}
