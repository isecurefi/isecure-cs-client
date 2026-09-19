# Sign a file in C#

This standalone example creates an **armored detached binary-document OpenPGP signature**
using SHA-256, verifies it locally, and saves the signature. It does not call ISECure,
AWS, Node.js or GnuPG. Bouncy Castle is an example-only dependency; the SDK remains
independent of a signing library.

## Run

From the repository root, after `dotnet build -c Release`:

```sh
dotnet run --project examples/SignFile -c Release --no-build -- \
  .private/payment.xml .private/signing-private.asc .private/payment.sig.asc
```

Supply an exported OpenPGP private key and an existing input file. The output path must
be new: the example refuses to overwrite it. For a passphrase-protected key, set
`ISECURE_PGP_PASSPHRASE` through your secret provider. An unset value means an unencrypted
private key, not an interactive passphrase prompt. Do not put a passphrase on the command line.

If a key bundle contains multiple signing-capable keys, explicitly select the intended
signing key. This command lists their **full fingerprints** without printing key material:

```sh
dotnet run --project examples/SignFile -c Release --no-build -- \
  --list-keys .private/signing-private.asc
```

Set `ISECURE_PGP_SIGNING_FINGERPRINT` to the fingerprint of the signing key designated by
your key-management process, then rerun the signing command. Use its complete public
key bundle for [API key registration](../FileExchange/README.md#register-your-pgp-public-key).
The listing describes algorithm capability; it does not decide your key usage policy.

The printed SHA-256 identifies the bytes that were signed. Keep that input file unchanged
until upload. If a file is edited or line endings change, create a new signature.
The example is qualified with OpenPGP v4 RSA keys, including encrypted keys with UTF-8
passphrases. Other key formats are subject to the signing library and API's support.

## Sign and upload in one operation

The compiled recipe reads the file once, signs those bytes, then uploads that same
array. Provide an already authenticated **data** client; register the corresponding
public key using an **admin** client beforehand. The caller owns and should clear the
passphrase character array after use.

Imports: `using ISECure;`, `using ISECure.Models;` and the example's `PgpSigning` helper
from [PgpSigning.cs](../Recipes/PgpSigning.cs). Bouncy Castle **2.7.0** is pinned in
[Recipes.csproj](../Recipes/Recipes.csproj).

<!-- snippet: examples/Recipes/FileExamples.cs#signed-upload -->
```csharp
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
```
<!-- /snippet -->

The helper is application/example code, not an SDK method. To reuse it, copy
`PgpSigning.cs` into your application and add `BouncyCastle.Cryptography` version
`2.7.0`, or reference `examples/Recipes/Recipes.csproj` while exploring the repository.
Production applications can supply signatures from their existing signing service.

The example uses the maintained [Bouncy Castle C# package](https://www.nuget.org/packages/BouncyCastle.Cryptography/2.7.0)
and its [OpenPGP APIs](https://www.bouncycastle.org/documentation/documentation-c/).
CI independently verifies its signatures using OpenPGP.js, including tampered bytes,
wrong passphrases and overwrite refusal. The live qualification checks server acceptance.
