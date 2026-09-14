using Ironmon.Updater.Core;
using System.Net.Http;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Ironmon.Setup.Core;
using Ironmon.Updater.Infrastructure;
using Microsoft.Win32;

namespace Ironmon.Setup;

/// <summary>
/// Provides narrowly scoped Windows prerequisite and shell operations for the disposable installer.
/// </summary>
internal sealed class WindowsSetupPlatform : ISetupPlatform
{
    private const string WebViewKey = @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
    private const string VersionValue = "pv";
    private const string RuntimeInstallerName = "MicrosoftEdgeWebView2RuntimeInstallerX64.exe";
    private const string CacheDirectory = "Ironmon/Updater/prerequisites";
    private const string PowerShellPath = @"WindowsPowerShell\v1.0\powershell.exe";
    private const string SignaturePathVariable = "IRONMON_PREREQUISITE_PATH";
    private const string SignatureScript = "$ErrorActionPreference = 'Stop'; try { $s = Get-AuthenticodeSignature -LiteralPath $env:IRONMON_PREREQUISITE_PATH; if ($s.Status -ne 'Valid' -or $s.SignerCertificate.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false) -ne 'Microsoft Corporation') { exit 1 }; exit 0 } catch { exit 1 }";
    private const string NoProfile = "-NoProfile";
    private const string NonInteractive = "-NonInteractive";
    private const string EncodedCommand = "-EncodedCommand";
    private const string Silent = "/silent";
    private const string Install = "/install";
    private const string GameProcess = "InfiniteFusion2";
    private const string TrackerProcess = "Ironmon Tracker";
    private const string ExecutableExtension = ".exe";
    private const string ShellId = "WScript.Shell";
    private const string ShortcutName = "Ironmon Tracker.lnk";
    private const string ShortcutExtension = ".lnk";
    private const string GuidFormat = "N";
    private readonly WebViewPackageDownload _webViewDownload = new();

    /// <summary>
    /// Detects the installed Evergreen runtime in Microsoft's documented per-user and per-machine registration.
    /// </summary>
    public bool WebViewAvailable
    {
        get
        {
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                using var registry = RegistryKey.OpenBaseKey(hive, RegistryView.Registry32);
                using var key = registry.OpenSubKey(WebViewKey);
                if (key?.GetValue(VersionValue) is string text && Version.TryParse(text, out var version) && version > new Version(0, 0, 0, 0))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Requires the actual tracker OS baseline and an x64 Windows process.
    /// </summary>
    public void EnsureSupported()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException(UpdaterText.WindowsSetupPlatformIronmonSetupRequiresWindows10Version1809OrNewer);
    }

    /// <summary>
    /// Reads the standalone runtime's current download size without downloading any installer body.
    /// </summary>
    /// <param name="cancellationToken">The review token.</param>
    /// <returns>The complete runtime package size, when Microsoft supplies it.</returns>
    public async Task<long?> GetWebViewDownloadBytesAsync(CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        return await _webViewDownload.InspectAsync(client, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads only Microsoft's full x64 runtime installer after consent, authenticates it, installs, then detects the runtime again.
    /// </summary>
    /// <param name="cancellationToken">The pre-launch cancellation token.</param>
    /// <returns>The independently rechecked runtime availability.</returns>
    public async Task InstallWebViewAsync(CancellationToken cancellationToken)
    {
        if (WebViewAvailable)
            return;

        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), CacheDirectory, Guid.NewGuid().ToString(GuidFormat));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, RuntimeInstallerName);
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
        await using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await _webViewDownload.DownloadAsync(client, output, cancellationToken).ConfigureAwait(false);

        using var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        await VerifyMicrosoftPublisherAsync(file, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var install = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true };
        install.ArgumentList.Add(Silent);
        install.ArgumentList.Add(Install);
        using var process = Process.Start(install) ?? throw new IOException(UpdaterText.WindowsSetupPlatformWindowsCouldNotStartTheMicrosoftPrerequisiteInstaller);
        await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromMinutes(15), cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0 || !WebViewAvailable)
            throw new IOException(UpdaterText.WindowsSetupPlatformMicrosoftWebView2CouldNotBeDetectedAfterInstallationComplete);
    }

    /// <summary>
    /// Requires Windows Authenticode validation and the Microsoft Corporation signer before prerequisite execution.
    /// </summary>
    /// <param name="file">The locked downloaded runtime installer.</param>
    /// <param name="cancellationToken">The verification token.</param>
    /// <returns>The publisher verification, or a failure without executing the downloaded file.</returns>
    internal static async Task VerifyMicrosoftPublisherAsync(string file, CancellationToken cancellationToken)
    {
        var verify = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), PowerShellPath)) { UseShellExecute = false, CreateNoWindow = true };
        verify.ArgumentList.Add(NoProfile);
        verify.ArgumentList.Add(NonInteractive);
        verify.ArgumentList.Add(EncodedCommand);
        verify.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(SignatureScript)));
        verify.Environment[SignaturePathVariable] = file;
        using var signature = Process.Start(verify) ?? throw new IOException(UpdaterText.WindowsSetupPlatformWindowsCouldNotVerifyThePrerequisitePublisher);
        await signature.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (signature.ExitCode != 0)
            throw new IOException(UpdaterText.WindowsSetupPlatformThePrerequisiteDidNotHaveAValidMicrosoftCorporation);

    }

    /// <summary>
    /// Requests normal exit only for processes belonging to the selected installation, never force-terminating them.
    /// </summary>
    /// <param name="root">The selected game folder.</param>
    /// <param name="cancellationToken">The safe wait token.</param>
    /// <returns>The selected installation's idle check.</returns>
    public async Task CloseInstallationAsync(string root, CancellationToken cancellationToken)
    {
        foreach (var name in new[] { GameProcess, TrackerProcess })
        {
            var expected = Path.GetFullPath(Path.Combine(root, name == GameProcess ? name + ExecutableExtension : UpdaterHandoff.TrackerRelativePath));
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    if (process.HasExited)
                        continue;

                    var identity = UpdateProcessIdentity.Capture(process);
                    if (identity.ExecutablePath.Equals(expected, StringComparison.OrdinalIgnoreCase))
                        await identity.WaitForExitAsync(requestClose: true, waiting: _ => InstallationProgressScope.Report(new(InstallationStage.WaitingForApplications)), cancellationToken: cancellationToken).ConfigureAwait(false);
                }
            }
        }

        await UpdateProcessIdentity.WaitForInstallationIdleAsync(root, UpdaterHandoff.TrackerRelativePath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates an opt-in desktop shortcut atomically, preserving unrelated shortcuts and avoiding duplicate matching links.
    /// </summary>
    /// <param name="root">The committed game folder.</param>
    public void CreateShortcut(string root)
        => CreateShortcut(root, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));

    /// <summary>
    /// Creates a tracker link in the selected shell directory, allowing isolated duplicate and collision validation.
    /// </summary>
    /// <param name="root">The committed game folder.</param>
    /// <param name="desktop">The Windows desktop, or an owned fixture directory.</param>
    internal static void CreateShortcut(string root, string desktop)
    {
        var target = Path.GetFullPath(Path.Combine(root, UpdaterHandoff.TrackerRelativePath));
        if (!File.Exists(target))
            throw new IOException(UpdaterText.WindowsSetupPlatformTheInstalledTrackerCouldNotBeFoundForIts);

        var destination = Path.Combine(desktop, ShortcutName);
        var temporary = Path.Combine(desktop, Guid.NewGuid().ToString(GuidFormat) + ShortcutExtension);
        var type = Type.GetTypeFromProgID(ShellId) ?? throw new IOException(UpdaterText.WindowsSetupPlatformWindowsShortcutSupportIsUnavailable);
        dynamic shell = Activator.CreateInstance(type)!;
        try
        {
            if (File.Exists(destination))
            {
                dynamic existing = shell.CreateShortcut(destination);
                try
                {
                    if (string.Equals((string)existing.TargetPath, target, StringComparison.OrdinalIgnoreCase))
                        return;

                    throw new IOException(UpdaterText.WindowsSetupPlatformADifferentIronmonTrackerShortcutAlreadyExistsOnThe);
                }
                finally
                {
                    Marshal.FinalReleaseComObject(existing);
                }
            }

            dynamic shortcut = shell.CreateShortcut(temporary);
            try
            {
                shortcut.TargetPath = target;
                shortcut.WorkingDirectory = root;
                shortcut.IconLocation = target;
                shortcut.Description = UpdaterText.WindowsSetupPlatformOpenTheIronmonTracker;
                shortcut.Save();
                File.Move(temporary, destination, false);
            }
            finally
            {
                Marshal.FinalReleaseComObject(shortcut);
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    /// <summary>
    /// Opens the installed tracker directly with its game working directory after verified core success.
    /// </summary>
    /// <param name="root">The committed game folder.</param>
    public void OpenTracker(string root)
    {
        if (!WebViewAvailable || UpdateTransaction.ReadActiveId(root) is not null)
            throw new InvalidOperationException(UpdaterText.WindowsSetupPlatformFinishRecoveryAndInstallMicrosoftWebView2BeforeOpeningThe);

        using var process = Process.Start(new ProcessStartInfo(Path.Combine(root, UpdaterHandoff.TrackerRelativePath)) { UseShellExecute = false, WorkingDirectory = root }) ?? throw new IOException(UpdaterText.WindowsSetupPlatformTheInstalledTrackerCouldNotBeOpened);
    }
}
