using ISECure;
using ISECure.Models;

namespace ISECure.Examples;

public static class FileExamples
{
    #region upload-key
    public static async Task RegisterSigningKeyAsync(
        ISECureClient authenticatedAdmin, string publicKeyFile,
        CancellationToken cancellationToken = default)
    {
        var publicKey = await File.ReadAllTextAsync(publicKeyFile, cancellationToken);
        await authenticatedAdmin.UploadPgpKeyAsync(publicKey, PgpKeyPurpose.Authorize, cancellationToken);
    }
    #endregion

    #region signed-upload
    public static async Task<Response> SignAndUploadAsync(
        ISECureClient authenticatedData, string inputFile, string fileType,
        string privateKeyFile, char[] passphrase, string? signingFingerprint = null,
        CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(inputFile, cancellationToken);
        var key = await File.ReadAllBytesAsync(privateKeyFile, cancellationToken);
        try
        {
            var signature = PgpSigning.Sign(bytes, key, passphrase, signingFingerprint);
            // Upload the same byte array that was signed; never re-read or re-encode the file.
            return await authenticatedData.UploadFileAsync(
                bytes, Path.GetFileName(inputFile), fileType, signature, cancellationToken);
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(key); }
    }
    #endregion

    #region download
    public static async Task DownloadAsync(
        ISECureClient authenticatedData, FileDescriptor descriptor, string outputFile,
        CancellationToken cancellationToken = default)
    {
        var file = await authenticatedData.DownloadFileAsync(
            descriptor.FileType, descriptor.FileReference, cancellationToken);
        // Caller chooses the local path. Never use a remote reference directly as a path.
        await using var output = new FileStream(outputFile, FileMode.CreateNew, FileAccess.Write);
        await output.WriteAsync(file.Bytes, cancellationToken);
    }
    #endregion

    #region errors
    public static async Task<bool> TryUploadAsync(
        ISECureClient authenticatedData, byte[] bytes, string fileName,
        string fileType, string detachedSignature, Action<string> report,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await authenticatedData.UploadFileAsync(bytes, fileName, fileType, detachedSignature, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            report("Cancelled. If the request was sent, check its status before resubmitting.");
            throw;
        }
        catch (ISecureApiException error)
        {
            report($"{error.Operation}: API code {error.ResponseCode}; request {error.RequestId ?? "unavailable"}.");
        }
        catch (ISecureAuthException)
        {
            report("Log in again before submitting this operation.");
        }
        catch (ISecureException error)
        {
            // Even a missing/malformed response can follow an accepted upload.
            report($"{error.Operation}: {error.GetType().Name}. Check status before resubmitting.");
        }
        return false; // No automatic write retry and no raw ResponseText logging.
    }
    #endregion
}
