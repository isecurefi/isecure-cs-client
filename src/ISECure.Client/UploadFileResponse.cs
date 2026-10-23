using System.Text.Json.Serialization;
namespace ISECure.Models;

/// <summary>Upload acknowledgement with an optional inline bank report.</summary>
/// <remarks>Extends the pinned generated acknowledgement without modifying the published contract snapshot.</remarks>
public sealed class UploadFileResponse : Response
{
    /// <summary>Base64 content returned directly by UploadFile, or null for an acknowledgement without a report.</summary>
    /// <remarks>Preserved as received. No automatic decoding or follow-up download is performed.</remarks>
    [JsonPropertyName("Content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; set; }
}
