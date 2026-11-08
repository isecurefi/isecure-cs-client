using ISECure.Internal;
using ISECure.Models;
namespace ISECure;

public sealed partial class ISECureClient
{
    private PasswordDiscovery? _resetDiscovery;
    private string? _resetTenant;

    /// <summary>Sends a password reset code and retains operation-bound discovery in memory.</summary>
    /// <param name="cancellationToken">Cancels waiting and the HTTP request.</param>
    public Task InitPasswordResetAsync(CancellationToken cancellationToken = default) => ExclusiveAsync(async () =>
    {
        _resetDiscovery = null; _resetTenant = null;
        if (_options.EncryptionProfile is null)
            await _transport.SendAsync("InitPasswordReset", HttpMethod.Get, AccountPath + "/password", null, _apiKey, null, cancellationToken).ConfigureAwait(false);
        else
        {
            _resetDiscovery = await ChallengeAsync("InitPasswordReset", AccountPath + "/password", cancellationToken).ConfigureAwait(false);
            _resetTenant = _apiKey;
        }
        return true;
    }, cancellationToken);

    /// <summary>Completes reset once with a code and new password. This does not log in.</summary>
    /// <param name="code">Six-digit reset code from the account recovery channel.</param>
    /// <param name="newPassword">New password, never retained.</param>
    /// <param name="cancellationToken">Cancels waiting and the HTTP request.</param>
    public Task<Response> ResetPasswordAsync(string code, string newPassword, CancellationToken cancellationToken = default) => ExclusiveAsync(async () =>
    {
        ValidateCode(code);
        var discovery = _resetDiscovery;
        var tenant = _resetTenant;
        _resetDiscovery = null; _resetTenant = null;
        if (_options.EncryptionProfile is null) discovery = await ChallengeAsync("InitLogin", SessionPath, cancellationToken).ConfigureAwait(false);
        else if (discovery is null || tenant != _apiKey) throw new ISecureAuthException("ResetPassword");
        var body = new PasswordResetReq { Code = code, ChResp = discovery!.Challenge,
            EncryptionProfile = discovery.EncryptionProfile, KeyId = discovery.KeyId,
            Encrypted = discovery.Encrypt(_options.PublicKeyPem, newPassword) };
        var data = await _transport.SendAsync("PasswordReset", HttpMethod.Post, AccountPath + "/password", body, _apiKey, null, cancellationToken).ConfigureAwait(false);
        ResetAuthentication();
        return ApiTransport.Deserialize<Response>(data, "ResetPassword");
    }, cancellationToken);
}
