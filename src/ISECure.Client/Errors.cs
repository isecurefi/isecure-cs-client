using System.Net;
namespace ISECure;

/// <summary>Base SDK failure. Message and ToString omit request bodies, credentials and raw server text.</summary>
public abstract class ISecureException : Exception
{
    /// <summary>Stable SDK operation name; safe to include in diagnostics.</summary>
    public string Operation { get; }
    /// <summary>Creates an SDK failure with a safe operation name and message.</summary>
    /// <param name="operation">Stable operation identifier.</param>
    /// <param name="message">Safe description without credentials or raw payloads.</param>
    protected ISecureException(string operation, string message) : base(message) => Operation = operation;
}
/// <summary>A structured API refusal; HTTP status can still be 200.</summary>
public sealed class ISecureApiException : ISecureException
{
    /// <summary>HTTP status returned by the service.</summary>
    public HttpStatusCode StatusCode { get; }
    /// <summary>Two-digit API response code, independent of HTTP status.</summary>
    public string ResponseCode { get; }
    /// <summary>Raw server explanation; may contain sensitive data. Do not log indiscriminately.</summary>
    public string ResponseText { get; }
    /// <summary>Optional server request identifier for support correlation.</summary>
    public string? RequestId { get; }
    internal ISecureApiException(string operation, HttpStatusCode statusCode, string code, string text, string? requestId)
        : base(operation, $"ISECure operation {operation} was refused by the API.")
        => (StatusCode, ResponseCode, ResponseText, RequestId) = (statusCode, code, text, requestId);
}
/// <summary>An unsuccessful HTTP response without a recognized API refusal, or a redirect.</summary>
public sealed class ISecureHttpException : ISecureException
{
    /// <summary>HTTP status returned by the service.</summary>
    public HttpStatusCode StatusCode { get; }
    internal ISecureHttpException(string operation, HttpStatusCode status) : base(operation, $"ISECure operation {operation} returned HTTP {(int)status}.") => StatusCode = status;
}
/// <summary>The service returned an incomplete, malformed or oversized response.</summary>
public sealed class ISecureProtocolException : ISecureException
{
    internal ISecureProtocolException(string operation) : base(operation, $"ISECure operation {operation} returned an invalid response.") { }
}
/// <summary>The HTTP connection or response stream failed. The outcome of a write can be unknown.</summary>
public sealed class ISecureNetworkException : ISecureException
{
    internal ISecureNetworkException(string operation) : base(operation, $"ISECure operation {operation} could not reach the service.") { }
}
/// <summary>The request exceeded its timeout. The outcome of a write can be unknown.</summary>
public sealed class ISecureTimeoutException : ISecureException
{
    internal ISecureTimeoutException(string operation) : base(operation, $"ISECure operation {operation} exceeded the request timeout.") { }
}
/// <summary>The requested operation is invalid for the local authentication state, including session expiry.</summary>
public sealed class ISecureAuthException : ISecureException
{
    internal ISecureAuthException(string operation) : base(operation, $"ISECure operation {operation} is not valid in the current authentication state.") { }
}

/// <summary>Contains no URLs, identifiers, headers, payloads, credentials or server response text.</summary>
/// <param name="Operation">Stable SDK operation name.</param>
/// <param name="HttpStatus">HTTP status when a response was received.</param>
/// <param name="Outcome">Structural outcome such as success, cancelled or timeout.</param>
public sealed record SdkDiagnostic(string Operation, int? HttpStatus, string Outcome);
