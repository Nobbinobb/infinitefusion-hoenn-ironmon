using System.Security.Cryptography;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.ReleaseTool;

/// <summary>
/// Exposes bounded release-tool commands without embedding fixture keys or invoking network or publication operations.
/// </summary>
internal static class Program
{
    private const string Create = "create";
    private const string Verify = "verify";
    private const string Sign = "sign";
    private const string Package = "package";
    private const string HelperVersion = "helper-version";
    private const string Trust = "trust";
    private const string Signed = "signed";
    private const string Unsigned = "unsigned";
    private const string History = "history";
    private const string NoHistory = "-";
    private const string ValidateInputs = "validate-inputs";

    /// <summary>
    /// Runs one explicit command, reporting no private key bytes or full secret-bearing exception values.
    /// </summary>
    /// <param name="args">The fixed command and local paths.</param>
    /// <returns>Zero only for a successfully verified operation.</returns>
    private static int Main(string[] args)
    {
        try
        {
            if (args is [Create, var request])
            {
                ReleaseBundle.Create(ReleaseJson.Parse<BundleRequest>(File.ReadAllBytes(request), ReleaseJson.ManifestLimit));
            }
            else if (args is [Package, var root, var version, var flavor, var commit])
            {
                BundleFiles.WriteOwnership(root, version, flavor, commit);
            }
            else if (args is [Verify, var directory, var trust, var mode] && mode is Signed or Unsigned or History)
            {
                ReleaseBundle.Verify(directory, trust, mode != Unsigned, mode != History);
            }
            else if (args is [HelperVersion, var history, var trustFile])
            {
                var sequence = history == NoHistory || !File.Exists(PlainPaths.Child(history, ReleaseProtocol.ManifestName)) ? 1 : checked(ReleaseBundle.Verify(history, trustFile, true, false).ReleaseSequence + 1);
                Console.WriteLine(ReleaseBundle.HelperVersion(sequence));
            }
            else if (args is [Trust, var publicKeys])
            {
                ReleaseBundle.ReadTrust(publicKeys);
            }
            else if (args is [ValidateInputs, var gameInventory, var gameManifest, var gameVersion, var previous, var trustedKeys])
            {
                ReleaseBundle.ValidateInputs(gameInventory, gameManifest, gameVersion, previous == NoHistory ? null : previous, trustedKeys);
            }
            else if (args is [Sign, var candidate, var keys])
            {
                var encoded = Environment.GetEnvironmentVariable(ReleaseSigning.KeyVariable) ?? throw new InvalidOperationException("The dedicated updater signing secret is missing.");
                Environment.SetEnvironmentVariable(ReleaseSigning.KeyVariable, null);
                var id = Environment.GetEnvironmentVariable(ReleaseSigning.IdVariable) ?? throw new InvalidOperationException("The updater signing key identifier is missing.");
                var bytes = Convert.FromBase64String(encoded);
                try
                {
                    ReleaseSigning.Sign(candidate, keys, id, bytes);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(bytes);
                }
            }
            else
            {
                throw new ArgumentException("Unsupported release tool command or argument count.");
            }

            return 0;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or ArgumentException or CryptographicException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine(args.FirstOrDefault() == Sign ? "Release signing failed. Check the verified candidate and dedicated key configuration." : error.Message);
            return 1;
        }
    }
}
