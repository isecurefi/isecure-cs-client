# Errors and cancellation

Use the SDK's typed failures to decide what the application should do:

| Outcome | Meaning | Application action |
| --- | --- | --- |
| `AuthResult.Status == Failed` | Login, MFA or verification was refused. | Inspect `FailureReason` and `ResponseCode`; restart authentication after resolving the cause. |
| `ISecureApiException` | A structured API refusal, possibly with HTTP 200. | Inspect `ResponseCode`, `RequestId`, and deliberately inspect `ResponseText` if needed. |
| `ISecureAuthException` | No usable local session, expiry, or an invalid auth step. | Complete the appropriate login/verification flow. |
| `ISecureHttpException` | HTTP error without a recognized API refusal, or a redirect. | Inspect `StatusCode`; resolve endpoint/service/access problems. |
| `ISecureProtocolException` | Invalid, incomplete or oversized response. | Check the endpoint/response limit; retain operation and support request context. |
| `ISecureNetworkException` | Connection or response stream failed. | Check connectivity; an upload may have been accepted. |
| `ISecureTimeoutException` | Request timeout elapsed. | Check operation status before resubmitting a write. |
| `OperationCanceledException` | Caller cancellation. | Propagate cancellation; a sent write may still have completed. |
| `ArgumentException` / `ArgumentOutOfRangeException` | Invalid local input. | Correct configuration or method arguments before retrying. |

## An upload with deliberate failure handling

This compiled example takes an already authenticated data client and an exact byte
array/signature pair. `report` is your application's status/log callback. It reports
only structural data, preserves caller cancellation, and does not retry a write.

Imports: `using ISECure;`. Copy the complete method into your application's class.

<!-- snippet: examples/Recipes/FileExamples.cs#errors -->
```csharp
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
```
<!-- /snippet -->

A request timeout, disconnect, cancelled response, HTTP error or malformed acknowledgement
can leave the outcome of a write unknown. Reconcile through file listings/feedback or
support before resubmission. The SDK cannot turn an uncertain response into proof that
nothing happened.

## Timeouts, cancellation and limits

Pass `CancellationToken` to each method. Cancelling while waiting for another operation
prevents that queued request from being sent. Requests use a 30-second timeout by
default, configurable up to five minutes through `ClientOptions.requestTimeout`.
`LogoutAsync` always performs local clearing, even when its token is already cancelled.

The default response limit is 8 MiB of **encoded JSON**, including base64 overhead.
Use `maximumResponseBytes` (1 KiB–64 MiB) if your expected file responses require it.
An injected `HttpClient.Timeout` can impose an additional shorter limit; set it to
`Timeout.InfiniteTimeSpan` if you want SDK timeouts alone to control requests.

## Safe diagnostics

SDK exception `Message`/`ToString()` and optional `SdkDiagnostic` contain structural
operation/status information. The API exception's `ResponseText` is raw server data;
inspect it deliberately rather than sending it wholesale to logs. Do not log auth
results containing `TotpEnrollment`, downloaded files, passwords or PGP private keys.

Use the optional `diagnostic` callback on `ISECureClient` for operation/status/outcome
metrics. Callback exceptions are swallowed so logging cannot change the API result.
