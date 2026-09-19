using System.Text.Json;
using ISECure.Internal;
using ISECure.Models;
namespace ISECure;

public enum PgpKeyPurpose { Authorize, Export }
public sealed class DownloadedFile
{
    public ReadOnlyMemory<byte> Bytes { get; }
    internal DownloadedFile(byte[] bytes) => Bytes = bytes;
    public override string ToString() => $"DownloadedFile ({Bytes.Length} bytes)";
}

public sealed partial class ISECureClient
{
    public Task<ListCertsResp> ListCertificatesAsync(CancellationToken cancellationToken = default) => ProtectedAsync("ListCerts", HttpMethod.Get, "certs", null,
        data => {
            if (!data.TryGetProperty("Certs", out var certs) || certs.ValueKind != JsonValueKind.Array)
                throw new ISecureProtocolException("ListCerts");
            return ApiTransport.Deserialize<ListCertsResp>(data, "ListCerts");
        }, cancellationToken);

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
