using System.Text.Json.Serialization;
namespace ISECure.Models;

public partial class LoginReq
{
    /// <summary>Explicit password profile; null retains legacy requests.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? EncryptionProfile { get; set; }
    /// <summary>Discovered encryption key identifier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? KeyId { get; set; }
}

public partial class RegisterReq
{
    /// <summary>Explicit password profile; null retains legacy requests.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? EncryptionProfile { get; set; }
    /// <summary>Discovered encryption key identifier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? KeyId { get; set; }
}

public partial class PasswordResetReq
{
    /// <summary>Explicit password profile; null retains legacy requests.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? EncryptionProfile { get; set; }
    /// <summary>Discovered encryption key identifier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? KeyId { get; set; }
}
