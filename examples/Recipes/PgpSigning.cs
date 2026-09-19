using System.Text;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;

namespace ISECure.Examples;

public static class PgpSigning
{
    public static string[] SigningFingerprints(byte[] secretKeyBytes)
    {
        using var input = new MemoryStream(secretKeyBytes, writable: false);
        using var decoded = PgpUtilities.GetDecoderStream(input);
        return new PgpSecretKeyRingBundle(decoded).GetKeyRings().SelectMany(ring => ring.GetSecretKeys())
            .Where(key => key.IsSigningKey && !key.IsPrivateKeyEmpty)
            .Select(key => Convert.ToHexString(key.PublicKey.GetFingerprint())).ToArray();
    }

    // Example dependency only: the ISECure.Client package contains no PGP library.
    public static string Sign(byte[] bytes, byte[] secretKeyBytes, char[] passphrase, string? fingerprint = null)
    {
        using var keyInput = new MemoryStream(secretKeyBytes, writable: false);
        using var decoded = PgpUtilities.GetDecoderStream(keyInput);
        var bundle = new PgpSecretKeyRingBundle(decoded);
        var candidates = bundle.GetKeyRings().SelectMany(ring => ring.GetSecretKeys())
            .Where(key => key.IsSigningKey && !key.IsPrivateKeyEmpty &&
                (fingerprint is null || Convert.ToHexString(key.PublicKey.GetFingerprint()).Equals(fingerprint, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (candidates.Length != 1)
            throw new ArgumentException("Select exactly one signing key with ISECURE_PGP_SIGNING_FINGERPRINT (full fingerprint).");
        var key = candidates[0];
        var generator = new PgpSignatureGenerator(key.PublicKey.Algorithm, HashAlgorithmTag.Sha256);
        generator.InitSign(PgpSignature.BinaryDocument, key.ExtractPrivateKeyUtf8(passphrase));
        generator.Update(bytes);
        var signature = generator.Generate();
        // Verify the produced signature before it leaves this example.
        signature.InitVerify(key.PublicKey);
        signature.Update(bytes);
        if (!signature.Verify()) throw new InvalidOperationException("Local signature verification failed.");
        using var output = new MemoryStream();
        using (var armor = new ArmoredOutputStream(output)) signature.Encode(armor);
        return Encoding.ASCII.GetString(output.ToArray());
    }
}
