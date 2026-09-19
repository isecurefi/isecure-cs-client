using System.Text.Json;
using ISECure.Internal;
using ISECure.Models;
namespace ISECure;

/// <summary>Purpose assigned to a registered public PGP key.</summary>
public enum PgpKeyPurpose
{
    /// <summary>Verify signatures on uploaded files.</summary>
    Authorize,
    /// <summary>Encrypt supported exported key material to this public key.</summary>
    Export
}
/// <summary>Exact decoded file content returned by the API.</summary>
public sealed class DownloadedFile
{
    /// <summary>Decoded bytes. Use ToArray or Span without text conversion when saving or hashing.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }
    internal DownloadedFile(byte[] bytes) => Bytes = bytes;
    /// <summary>Returns only the byte count, never file contents.</summary>
    public override string ToString() => $"DownloadedFile ({Bytes.Length} bytes)";
}

public sealed partial class ISECureClient
{
    /// <summary>Lists certificates and bank connections visible to this authenticated account.</summary>
    /// <param name="cancellationToken">Cancels waiting for the client and the HTTP request.</param>
    /// <returns>Certificate and connection metadata from the API.</returns>
    /// <remarks>Does not enroll, renew or import certificates.</remarks>
    /// <exception cref="ISecureApiException">The API refused the operation; inspect ResponseCode and RequestId.</exception>
    /// <exception cref="ISecureAuthException">The client has no valid authenticated session.</exception>
    /// <exception cref="ISecureException">An HTTP, protocol, network or timeout failure occurred.</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled the operation.</exception>
    public Task<ListCertsResp> ListCertificatesAsync(CancellationToken cancellationToken = default) => ProtectedAsync("ListCerts", HttpMethod.Get, "certs", null,
        data => {
            if (!data.TryGetProperty("Certs", out var certs) || certs.ValueKind != JsonValueKind.Array)
                throw new ISecureProtocolException("ListCerts");
            return ApiTransport.Deserialize<ListCertsResp>(data, "ListCerts");
        }, cancellationToken);

    /// <summary>Registers an armored PGP public key on the authenticated admin account.</summary>
    /// <param name="armoredPublicKey">ASCII-armored public key block; never pass a private key.</param>
    /// <param name="purpose">Authorize for file-signature verification, or Export for supported key-export workflows.</param>
    /// <param name="cancellationToken">Cancels waiting for the client and the HTTP request.</param>
    /// <returns>The API acknowledgement.</returns>
    /// <remarks>For signed uploads, register the public key corresponding to the signing private key with Authorize.</remarks>
    /// <exception cref="ISecureApiException">The API refused the operation; inspect ResponseCode and RequestId.</exception>
    /// <exception cref="ISecureAuthException">The client has no valid authenticated session.</exception>
    /// <exception cref="ISecureException">An HTTP, protocol, network or timeout failure occurred.</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled the operation.</exception>
    public Task<Response> UploadPgpKeyAsync(string armoredPublicKey, PgpKeyPurpose purpose = PgpKeyPurpose.Authorize, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(armoredPublicKey);
        if (!armoredPublicKey.StartsWith("-----BEGIN PGP PUBLIC KEY BLOCK-----", StringComparison.Ordinal) ||
            armoredPublicKey.Contains("PRIVATE KEY", StringComparison.Ordinal)) throw new ArgumentException("An armored public PGP key is required.", nameof(armoredPublicKey));
        if (!Enum.IsDefined(purpose)) throw new ArgumentOutOfRangeException(nameof(purpose));
        return ProtectedAsync("UploadKey", HttpMethod.Put, "pgp",
            new UploadKeyReq { PgpKey = armoredPublicKey, PgpKeyPurpose = purpose == PgpKeyPurpose.Authorize ? "authorize" : "export" },
            data => ApiTransport.Deserialize<Response>(data, "UploadKey"), cancellationToken);
    }

    /// <summary>Lists file descriptors for the configured bank.</summary>
    /// <param name="fileType">Bank-supported file type, or null to omit this filter.</param>
    /// <param name="status">API status filter, for example NEW or ALL; null omits the filter.</param>
    /// <param name="cancellationToken">Cancels waiting for the client and the HTTP request.</param>
    /// <returns>Descriptors containing the exact FileType and FileReference to pass to DownloadFileAsync.</returns>
    /// <remarks>Bank access, certificates and product entitlements are enforced by the service.</remarks>
    /// <exception cref="ISecureApiException">The API refused the operation; inspect ResponseCode and RequestId.</exception>
    /// <exception cref="ISecureAuthException">The client has no valid authenticated session.</exception>
    /// <exception cref="ISecureException">An HTTP, protocol, network or timeout failure occurred.</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled the operation.</exception>
    public Task<ListFilesResp> ListFilesAsync(string? fileType = null, string? status = null, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(fileType)) query.Add("FileType=" + Uri.EscapeDataString(fileType));
        if (!string.IsNullOrWhiteSpace(status)) query.Add("Status=" + Uri.EscapeDataString(status));
        var path = "files/" + Segment(_options.Bank) + (query.Count == 0 ? "" : "?" + string.Join("&", query));
        return ProtectedAsync("ListFiles", HttpMethod.Get, path, null, data => {
            if (!data.TryGetProperty("FileDescriptors", out var list) || list.ValueKind != JsonValueKind.Array) throw new ISecureProtocolException("ListFiles");
            var result = ApiTransport.Deserialize<ListFilesResp>(data, "ListFiles");
            if (result.FileDescriptors.Any(x => x is null || string.IsNullOrWhiteSpace(x.FileReference) || string.IsNullOrWhiteSpace(x.FileType)))
                throw new ISecureProtocolException("ListFiles");
            return result;
        }, cancellationToken);
    }

    /// <summary>Uploads exact file bytes with a caller-created detached PGP signature.</summary>
    /// <param name="bytes">Original nonempty bytes that were signed; no text or newline conversion is performed.</param>
    /// <param name="fileName">File name sent to the bank, such as payment.xml.</param>
    /// <param name="fileType">Bank-supported payment or file type.</param>
    /// <param name="detachedSignature">Armored detached binary-document PGP signature of bytes.</param>
    /// <param name="cancellationToken">Cancels waiting for the client and the HTTP request.</param>
    /// <returns>The API acknowledgement; bank processing and feedback can follow asynchronously.</returns>
    /// <remarks>Use data mode and an enrolled bank connection. Bytes are snapshotted before waiting. A timeout may follow an accepted upload; check status before retrying.</remarks>
    /// <exception cref="ISecureApiException">The API refused the operation; inspect ResponseCode and RequestId.</exception>
    /// <exception cref="ISecureAuthException">The client has no valid authenticated session.</exception>
    /// <exception cref="ISecureException">An HTTP, protocol, network or timeout failure occurred.</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled the operation.</exception>
    public Task<Response> UploadFileAsync(ReadOnlyMemory<byte> bytes, string fileName, string fileType, string detachedSignature, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName); ArgumentException.ThrowIfNullOrWhiteSpace(fileType);
        ArgumentException.ThrowIfNullOrWhiteSpace(detachedSignature);
        if (bytes.IsEmpty) throw new ArgumentException("File must contain bytes.", nameof(bytes));
        // Snapshot bytes before any await; caller mutations cannot change the signed request payload.
        var request = new UploadFileReq { FileContents = Convert.ToBase64String(bytes.Span), FileName = fileName, FileType = fileType, Signature = detachedSignature };
        return ProtectedAsync("UploadFile", HttpMethod.Put, "files/" + Segment(_options.Bank), request,
            data => ApiTransport.Deserialize<Response>(data, "UploadFile"), cancellationToken);
    }

    /// <summary>Downloads and base64-decodes a file without changing its bytes.</summary>
    /// <param name="fileType">FileType returned by ListFilesAsync.</param>
    /// <param name="fileReference">FileReference returned by ListFilesAsync.</param>
    /// <param name="cancellationToken">Cancels waiting for the client and the HTTP request.</param>
    /// <returns>Decoded bytes; save them directly for exact file preservation.</returns>
    /// <remarks>Use data mode. Downloading may change the bank-side file status; use ALL when checking previously downloaded files.</remarks>
    /// <exception cref="ISecureApiException">The API refused the operation; inspect ResponseCode and RequestId.</exception>
    /// <exception cref="ISecureAuthException">The client has no valid authenticated session.</exception>
    /// <exception cref="ISecureException">An HTTP, protocol, network or timeout failure occurred.</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled the operation.</exception>
    public Task<DownloadedFile> DownloadFileAsync(string fileType, string fileReference, CancellationToken cancellationToken = default) =>
        ProtectedAsync("DownloadFile", HttpMethod.Get, $"files/{Segment(_options.Bank)}/{Segment(fileType)}/{Segment(fileReference)}", null, data => {
            var content = ApiTransport.String(data, "Content") ?? throw new ISecureProtocolException("DownloadFile");
            try { return new DownloadedFile(Convert.FromBase64String(content)); }
            catch (FormatException) { throw new ISecureProtocolException("DownloadFile"); }
        }, cancellationToken);

    private Task<T> ProtectedAsync<T>(string operation, HttpMethod method, string path, object? body, Func<JsonElement, T> decode, CancellationToken cancellationToken) =>
        ExclusiveAsync(async () => {
            var auth = RequireSession(operation);
            try
            {
                var data = await _transport.SendAsync(operation, method, path, body, auth.ApiKey, auth.IdToken, cancellationToken).ConfigureAwait(false);
                return decode(data);
            }
            catch (ISecureHttpException e) when ((int)e.StatusCode is 401 or 403) { ResetAuthentication(); throw; }
            catch (ISecureApiException e) when ((int)e.StatusCode is 401 or 403) { ResetAuthentication(); throw; }
        }, cancellationToken);
}
