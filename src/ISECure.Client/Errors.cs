using System.Net;
namespace ISECure;

public abstract class ISecureException : Exception
{
    public string Operation { get; }
    protected ISecureException(string operation, string message) : base(message) => Operation = operation;
}
public sealed class ISecureApiException : ISecureException
{
    public HttpStatusCode StatusCode { get; }
    public string ResponseCode { get; }
    public string ResponseText { get; }
    public string? RequestId { get; }
    internal ISecureApiException(string operation, HttpStatusCode statusCode, string code, string text, string? requestId)
        : base(operation, $"ISECure operation {operation} was refused by the API.")
        => (StatusCode, ResponseCode, ResponseText, RequestId) = (statusCode, code, text, requestId);
}
public sealed class ISecureHttpException : ISecureException
{
    public HttpStatusCode StatusCode { get; }
    internal ISecureHttpException(string operation, HttpStatusCode status) : base(operation, $"ISECure operation {operation} returned HTTP {(int)status}.") => StatusCode = status;
}
public sealed class ISecureProtocolException : ISecureException
{
    internal ISecureProtocolException(string operation) : base(operation, $"ISECure operation {operation} returned an invalid response.") { }
}
public sealed class ISecureNetworkException : ISecureException
{
    internal ISecureNetworkException(string operation) : base(operation, $"ISECure operation {operation} could not reach the service.") { }
}
public sealed class ISecureTimeoutException : ISecureException
{
    internal ISecureTimeoutException(string operation) : base(operation, $"ISECure operation {operation} exceeded the request timeout.") { }
}
public sealed class ISecureAuthException : ISecureException
{
    internal ISecureAuthException(string operation) : base(operation, $"ISECure operation {operation} is not valid in the current authentication state.") { }
}

/// <summary>Contains no URLs, identifiers, headers, payloads, credentials or server response text.</summary>
public sealed record SdkDiagnostic(string Operation, int? HttpStatus, string Outcome);
