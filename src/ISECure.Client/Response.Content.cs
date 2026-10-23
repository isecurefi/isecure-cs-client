using System.Text.Json.Serialization;
namespace ISECure.Models;

/// <summary>API acknowledgement, including an optional inline bank report for UploadFile.</summary>
/// <remarks>Adds an optional field to the pinned generated acknowledgement without changing existing method signatures.</remarks>
public partial class Response
{
    /// <summary>Base64 content returned directly by UploadFile, or null for an acknowledgement without a report.</summary>
    /// <remarks>Preserved as received. No automatic decoding or follow-up download is performed.</remarks>
    [JsonPropertyName("Content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; set; }
}
