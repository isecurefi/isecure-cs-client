using System.Security.Cryptography;
using System.Text;
using ISECure.Examples;
using Org.BouncyCastle.Bcpg.OpenPgp;

var listKeys = args.Length == 2 && args[0] == "--list-keys";
if ((!listKeys && args.Length != 3) || args.Contains("--help"))
{
    Console.WriteLine("Usage: dotnet run --project examples/SignFile -- <input-file> <private-key.asc> <signature.asc>");
    Console.WriteLine("Optional: ISECURE_PGP_PASSPHRASE and ISECURE_PGP_SIGNING_FINGERPRINT (full signing-key fingerprint).");
    Console.WriteLine("List signing-capable key fingerprints: dotnet run --project examples/SignFile -- --list-keys <private-key.asc>");
    Console.WriteLine("Signs raw bytes with SHA-256, verifies locally, and creates a new signature file. No API or AWS access.");
    if (!args.Contains("--help")) Environment.ExitCode = 2;
    return;
}

byte[] key = [];
var passphrase = (Environment.GetEnvironmentVariable("ISECURE_PGP_PASSPHRASE") ?? "").ToCharArray();
try
{
    if (listKeys)
    {
        key = await File.ReadAllBytesAsync(args[1]);
        foreach (var fingerprint in PgpSigning.SigningFingerprints(key)) Console.WriteLine(fingerprint);
        return;
    }
    var bytes = await File.ReadAllBytesAsync(args[0]);
    key = await File.ReadAllBytesAsync(args[1]);
    var signature = PgpSigning.Sign(bytes, key, passphrase, Environment.GetEnvironmentVariable("ISECURE_PGP_SIGNING_FINGERPRINT"));
    await using var output = new FileStream(args[2], FileMode.CreateNew, FileAccess.Write);
    await output.WriteAsync(Encoding.ASCII.GetBytes(signature));
    Console.WriteLine($"Signed {bytes.Length} bytes; SHA-256 {Convert.ToHexStringLower(SHA256.HashData(bytes))}.");
}
catch (ArgumentException e) { Console.Error.WriteLine(e.Message); Environment.ExitCode = 2; }
catch (Exception e) when (e is PgpException or IOException or UnauthorizedAccessException or InvalidOperationException)
{
    Console.Error.WriteLine($"Signing failed ({e.GetType().Name}). Check the key, passphrase, readable input and a new output path.");
    Environment.ExitCode = 1;
}
finally { CryptographicOperations.ZeroMemory(key); Array.Clear(passphrase); }
