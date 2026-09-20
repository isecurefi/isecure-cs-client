using ISECure.Internal;
using ISECure.Models;
namespace ISECure;

public sealed partial class ISECureClient
{
    /// <summary>Enrolls certificates for the configured bank using an authenticated admin account.</summary>
    /// <param name="company">Company name required by the bank's enrollment agreement.</param>
    /// <param name="wsUserId">Web Services user identifier supplied for this bank connection.</param>
    /// <param name="code">Enrollment PIN/code. Treat it as a credential; never log it.</param>
    /// <param name="cancellationToken">Cancels waiting for the client and the HTTP request.</param>
    /// <returns>The API acknowledgement. Use ListCertificatesAsync to discover the enrolled connection.</returns>
    /// <remarks>Bank access and enrollment rules are enforced by the service. No automatic retry occurs.
    /// A timeout or cancellation after dispatch can follow successful enrollment; check certificates before retrying.
    /// Private certificate keys are generated and stored by the service, not by this SDK.</remarks>
    /// <exception cref="ArgumentException">A required input is empty or whitespace.</exception>
    /// <exception cref="ISecureApiException">The API refused enrollment; inspect ResponseCode and RequestId.</exception>
    /// <exception cref="ISecureAuthException">The client has no valid authenticated session.</exception>
    /// <exception cref="ISecureException">An HTTP, protocol, network or timeout failure occurred.</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled the operation.</exception>
    public Task<Response> EnrollCertificateAsync(string company, string wsUserId, string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(company);
        ArgumentException.ThrowIfNullOrWhiteSpace(wsUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return ProtectedAsync("EnrollCert", HttpMethod.Post, "certs/" + Segment(_options.Bank),
            new EnrollCertReq { Company = company, WsUserId = wsUserId, Code = code },
            data => ApiTransport.Deserialize<Response>(data, "EnrollCert"), cancellationToken);
    }
}
