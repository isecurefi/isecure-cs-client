using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace ISECure.Internal;

internal sealed class ApiTransport(HttpClient http, ClientOptions options, Action<SdkDiagnostic>? diagnostic)
{
    internal static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    internal async Task<JsonElement> SendAsync(string operation, HttpMethod method, string path,
        object? body, string apiKey, string? idToken, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(options.RequestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var expectedUri = new Uri(options.BaseUri, path);
        using var request = new HttpRequestMessage(method, expectedUri);
        request.Headers.Add("x-api-key", apiKey);
        if (idToken is not null) request.Headers.Add("Authorization", idToken);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        int? status = null;
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
            status = (int)response.StatusCode;
            if (response.RequestMessage?.RequestUri is Uri actual && actual != expectedUri)
                throw new ISecureProtocolException(operation);
            if (status is >= 300 and < 400) throw new ISecureHttpException(operation, response.StatusCode);
            if (response.Content.Headers.ContentLength > options.MaximumResponseBytes) throw new ISecureProtocolException(operation);
            await using var stream = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, linked.Token).ConfigureAwait(false)) > 0)
            {
                if (bytes.Length + read > options.MaximumResponseBytes) throw new ISecureProtocolException(operation);
                bytes.Write(buffer, 0, read);
            }
            JsonElement data;
            try
            {
                using var document = JsonDocument.Parse(bytes.ToArray());
                data = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                if (!response.IsSuccessStatusCode) throw new ISecureHttpException(operation, response.StatusCode);
                throw new ISecureProtocolException(operation);
            }
            var code = String(data, "ResponseCode");
            var text = String(data, "ResponseText");
            if (code is { Length: 2 } && code.All(char.IsAsciiDigit) && text is not null && code != "00")
                throw new ISecureApiException(operation, response.StatusCode, code, text, String(data, "RequestId"));
            if (!response.IsSuccessStatusCode) throw new ISecureHttpException(operation, response.StatusCode);
            if (code != "00" || text is null) throw new ISecureProtocolException(operation);
            Emit(new(operation, status, "success"));
            return data;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Emit(new(operation, status, "cancelled"));
            throw new OperationCanceledException("ISECure request cancelled.", cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Emit(new(operation, status, "timeout"));
            throw new ISecureTimeoutException(operation);
        }
        catch (HttpRequestException)
        {
            Emit(new(operation, status, "network_error"));
            throw new ISecureNetworkException(operation);
        }
        catch (IOException)
        {
            Emit(new(operation, status, "network_error"));
            throw new ISecureNetworkException(operation);
        }
        catch (ISecureException)
        {
            Emit(new(operation, status, "error"));
            throw;
        }
    }
    private void Emit(SdkDiagnostic value)
    {
        try { diagnostic?.Invoke(value); }
        catch (Exception) { /* Optional diagnostics cannot change request semantics. */ }
    }
    internal static string? String(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    internal static T Deserialize<T>(JsonElement data, string operation)
    {
        try { return data.Deserialize<T>(Json) ?? throw new ISecureProtocolException(operation); }
        catch (JsonException) { throw new ISecureProtocolException(operation); }
    }
}
