namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Distinguishes new ownership from an ordinary forward update in independently verified recovery evidence.
/// </summary>
public enum InstallationPurpose
{
    /// <summary>
    /// Updates an authenticated installed Ironmon baseline.
    /// </summary>
    Update = 0,

    /// <summary>
    /// Adds Ironmon to an existing authenticated game, claiming no prior mod files.
    /// </summary>
    AddIronmon = 1,

    /// <summary>
    /// Installs game and Ironmon into an empty destination.
    /// </summary>
    InstallGame = 2,

    /// <summary>
    /// Reconciles the same signed Ironmon version, including an explicit package flavor choice.
    /// </summary>
    Repair = 3
}

/// <summary>
/// Defines stable Setup identities shared by inspection and independent recovery.
/// </summary>
public static class SetupProtocol
{
    /// <summary>
    /// Identifies the absence of an installed Ironmon package without inventing a legacy baseline.
    /// </summary>
    public const string UninstalledVersion = "0.0.0";
}
