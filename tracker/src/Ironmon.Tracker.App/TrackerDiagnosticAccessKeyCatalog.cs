using System.Reflection;

namespace Ironmon.Tracker.App;

/// <summary>
/// Loads the maintainer public keys trusted by the distributed tracker.
/// </summary>
internal static class TrackerDiagnosticAccessKeyCatalog
{
    private const string _publicKeyResourceName = "Ironmon.Tracker.App.Resources.Keys.ironmon-access-public.pem";

    /// <summary>
    /// Creates the immutable trusted diagnostic-access keyring.
    /// </summary>
    /// <returns>The keyring containing every bundled maintainer public key.</returns>
    internal static DiagnosticAccessKeyring Create()
    {
        Assembly assembly = typeof(TrackerDiagnosticAccessKeyCatalog).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(_publicKeyResourceName)
            ?? throw new InvalidOperationException("The bundled diagnostic-access public key is missing.");

        using StreamReader reader = new(stream);
        return new([DiagnosticAccessPublicKey.Import(reader.ReadToEnd())]);
    }
}
