namespace ISECure;

/// <summary>Account role used for authentication and server authorization.</summary>
public enum AccountMode
{
    /// <summary>Administration, including public PGP key registration; cannot upload/download files.</summary>
    Admin,
    /// <summary>File exchange using the account's configured bank connections.</summary>
    Data
}

/// <summary>Immutable configuration. Passwords are passed to authentication methods, not retained here.</summary>
public sealed class ClientOptions
{
    /// <summary>HTTPS API base URL; normalized to end in a slash.</summary>
    public Uri BaseUri { get; }
    /// <summary>Environment RSA public PEM key for password challenge encryption; separate from your PGP key.</summary>
    public string PublicKeyPem { get; }
    /// <summary>Registered account email.</summary>
    public string Email { get; }
    /// <summary>Role used to authenticate.</summary>
    public AccountMode Mode { get; }
    /// <summary>Initially configured tenant identifier. Registration may adopt a new key internally without changing these immutable options.</summary>
    public string ApiKey { get; }
    /// <summary>Bank identifier used for file operations.</summary>
    public string Bank { get; }
    /// <summary>Company profile value sent during registration.</summary>
    public string Company { get; }
    /// <summary>Person name sent during registration.</summary>
    public string Name { get; }
    /// <summary>Account phone number used for registration and phone verification.</summary>
    public string Phone { get; }
    /// <summary>Per-request timeout; defaults to 30 seconds and must be at most five minutes.</summary>
    public TimeSpan RequestTimeout { get; }
    /// <summary>Maximum encoded HTTP response body size, including base64/JSON overhead; defaults to 8 MiB.</summary>
    public int MaximumResponseBytes { get; }

    /// <summary>Creates immutable configuration. Prefer named arguments for the profile fields.</summary>
    /// <param name="baseUri">Absolute HTTPS API URL with no credentials, query or fragment.</param>
    /// <param name="publicKeyPem">RSA public PEM key for this API environment.</param>
    /// <param name="email">Registered account email.</param>
    /// <param name="mode">Admin or Data account role.</param>
    /// <param name="apiKey">Tenant API key; use "0" only for new-owner registration.</param>
    /// <param name="bank">Bank identifier supplied by the API onboarding instructions.</param>
    /// <param name="company">Company name; retained as registration profile configuration.</param>
    /// <param name="name">Person name; retained as registration profile configuration.</param>
    /// <param name="phone">Account phone number, including country code.</param>
    /// <param name="requestTimeout">Optional timeout greater than zero and no longer than five minutes.</param>
    /// <param name="maximumResponseBytes">Maximum JSON response size, from 1 KiB through 64 MiB.</param>
    /// <exception cref="ArgumentException">A required value is blank, the API key contains invalid characters, or the URI is not a suitable HTTPS base URL.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Role, timeout or response limit is invalid.</exception>
    public ClientOptions(Uri baseUri, string publicKeyPem, string email, AccountMode mode,
        string apiKey, string bank, string company, string name, string phone,
        TimeSpan? requestTimeout = null, int maximumResponseBytes = 8 * 1024 * 1024)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        if (!baseUri.IsAbsoluteUri || baseUri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(baseUri.UserInfo) || !string.IsNullOrEmpty(baseUri.Query) ||
            !string.IsNullOrEmpty(baseUri.Fragment))
            throw new ArgumentException("Base URI must be an absolute HTTPS URI without credentials, query or fragment.", nameof(baseUri));
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPem);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(bank);
        ArgumentException.ThrowIfNullOrWhiteSpace(company);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(phone);
        if (apiKey.Any(c => c < 33 || c > 126)) throw new ArgumentException("Invalid API key format.", nameof(apiKey));
        var timeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5)) throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        if (maximumResponseBytes < 1024 || maximumResponseBytes > 64 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maximumResponseBytes));
        BaseUri = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/");
        PublicKeyPem = publicKeyPem; Email = email; Mode = mode; ApiKey = apiKey;
        Bank = bank; Company = company; Name = name; Phone = phone;
        RequestTimeout = timeout; MaximumResponseBytes = maximumResponseBytes;
    }
    /// <summary>Returns only the account mode, without identity or key values.</summary>
    public override string ToString() => $"ClientOptions ({Mode})";
}
