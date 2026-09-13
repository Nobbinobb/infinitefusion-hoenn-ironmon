using Ironmon.Updater.Core;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Contains the Windows permission, protected storage and explicit UAC boundary.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class WindowsUpdateAccess
{
    private const string ProbePrefix = ".ironmon-permission-";
    private const string CacheDirectory = "Ironmon Updater";
    private const string SharedGit = "git-cache";
    private const string ElevationVerb = "runas";
    private const string BundleDirectory = "DOTNET_BUNDLE_EXTRACT_BASE_DIR";
    private const string KernelLibrary = "kernel32.dll";
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;
    private const uint OpenReparsePoint = 0x00200000;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const string ExtendedPrefix = @"\\?\";
    private static readonly Lock _environmentGate = new();
    private static readonly string[] _environmentPrefixes = ["DOTNET_", "COMPlus_", "CORECLR_", "COR_"];
    private static readonly string[] _environmentNames = ["__COMPAT_LAYER", "PATH", "TEMP", "TMP"];
    private static readonly string[] _writeDirectories = [InstallationLease.StateDirectory, "Data", "Data/Scripts", "Data/Scripts/997_Ironmon", "Data/Ironmon", UpdaterText.WindowsUpdateAccessIronmonTracker];

    /// <summary>
    /// Gets whether the current process has an enabled administrator token.
    /// </summary>
    internal static bool IsAdministrator
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>
    /// Probes the destination and updater-owned directories without altering existing files.
    /// </summary>
    /// <param name="root">The reviewed installation directory.</param>
    /// <returns>Whether protected writes require a separate administrator process.</returns>
    internal static bool RequiresElevation(string root)
    {
        root = PlainPaths.Full(root);
        var nearest = root;
        while (!Directory.Exists(nearest))
            nearest = Path.GetDirectoryName(nearest) ?? throw new IOException(UpdaterText.WindowsUpdateAccessTheInstallationHasNoExistingParentDirectory);

        try
        {
            Probe(nearest);
            foreach (var relative in _writeDirectories)
            {
                var directory = PlainPaths.Child(root, relative);
                if (Directory.Exists(directory))
                    Probe(directory);
            }

            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// Creates and removes an exclusive disposable permission probe.
    /// </summary>
    /// <param name="directory">The existing directory to test.</param>
    private static void Probe(string directory)
    {
        var path = PlainPaths.Child(directory, ProbePrefix + Guid.NewGuid().ToString(TransactionStorage.GuidFormat));
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
    }

    /// <summary>
    /// Creates a fresh administrator-owned workspace with no inherited user write access.
    /// </summary>
    /// <returns>The private workspace outside the installation and user download cache.</returns>
    internal static string CreateWorkspace()
    {
        if (!IsAdministrator)
            throw new UnauthorizedAccessException(UpdaterText.WindowsUpdateAccessTheInstallationHelperNeedsAdministratorPermission);

        var parent = PlainPaths.Child(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), CacheDirectory);
        var root = PlainPaths.Child(parent, Guid.NewGuid().ToString(TransactionStorage.GuidFormat));
        Directory.CreateDirectory(parent);
        ValidateCacheParent(parent);
        new DirectoryInfo(root).Create(DirectoryPolicy());
        return root;
    }

    /// <summary>
    /// Retains the checksum-pinned Git runtime in protected shared storage for independent offline recovery.
    /// </summary>
    /// <returns>The administrator-owned private Git cache.</returns>
    internal static string GitCachePath()
    {
        var parent = PlainPaths.Child(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), CacheDirectory);
        ValidateCacheParent(parent);
        var root = PlainPaths.Child(parent, SharedGit);
        if (!Directory.Exists(root))
            new DirectoryInfo(root).Create(DirectoryPolicy());

        ValidateCacheParent(root);
        return root;
    }

    /// <summary>
    /// Refuses a cache or native-extraction parent whose owner or effective grants permit ordinary-user modification.
    /// </summary>
    /// <param name="path">The existing dedicated cache directory.</param>
    private static void ValidateCacheParent(string path)
    {
        var policy = new DirectoryInfo(PlainPaths.Full(path)).GetAccessControl();
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var owner = policy.GetOwner(typeof(SecurityIdentifier));
        if (owner != administrators && owner != system)
            throw new IOException(UpdaterText.WindowsUpdateAccessTheProtectedUpdaterCacheHasAnUnexpectedOwner);

        const FileSystemRights writes = FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        foreach (FileSystemAccessRule rule in policy.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType == AccessControlType.Allow && (rule.PropagationFlags & PropagationFlags.InheritOnly) == 0 && rule.IdentityReference != administrators && rule.IdentityReference != system && (rule.FileSystemRights & writes) != 0)
                throw new IOException(UpdaterText.WindowsUpdateAccessTheProtectedUpdaterCacheAllowsOrdinaryUserModification);
        }
    }

    /// <summary>
    /// Prevents ordinary processes from changing durable authorization, payload and recovery files.
    /// </summary>
    /// <param name="root">The authenticated installation directory.</param>
    internal static void ProtectState(string root)
    {
        var state = PlainPaths.Child(root, InstallationLease.StateDirectory);
        if (!Directory.Exists(state))
        {
            new DirectoryInfo(state).Create(DirectoryPolicy());
        }
        else
        {
            PlainPaths.CheckTree(state);
            new DirectoryInfo(state).SetAccessControl(DirectoryPolicy());
            ProtectChildren(state);
        }
    }

    /// <summary>
    /// Removes inherited write grants from existing updater-owned descendants without following redirects.
    /// </summary>
    /// <param name="root">The fixed updater state directory.</param>
    private static void ProtectChildren(string root)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(root))
        {
            PlainPaths.Full(path);
            if (Directory.Exists(path))
            {
                new DirectoryInfo(path).SetAccessControl(DirectoryPolicy());
                ProtectChildren(path);
            }
            else
            {
                using var locked = OpenRegular(path);
                var policy = new FileSecurity();
                policy.SetAccessRuleProtection(true, false);
                policy.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
                policy.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
                policy.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
                policy.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), FileSystemRights.ReadAndExecute, AccessControlType.Allow));
                new FileInfo(path).SetAccessControl(policy);
            }
        }
    }

    /// <summary>
    /// Opens the final file itself without following a redirect and holds it against replacement.
    /// </summary>
    /// <param name="path">The checked ordinary file.</param>
    /// <param name="writable">Whether an owned staged file also needs exclusive access for a durable flush.</param>
    /// <returns>The owned stable read stream.</returns>
    internal static FileStream OpenRegular(string path, bool writable = false)
    {
        var full = PlainPaths.Full(path);
        var ancestors = HoldAncestors(path);
        var handle = CreateFileW(ExtendedPrefix + full, writable ? GenericRead | GenericWrite : GenericRead, writable ? FileShare.None : FileShare.Read, IntPtr.Zero, OpenExisting, OpenReparsePoint, IntPtr.Zero);
        try
        {
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                if (error is 2 or 3)
                    throw new FileNotFoundException(UpdaterText.WindowsUpdateAccessTheUpdaterFileCouldNotBeFound, path);

                if (error == 5)
                    throw new UnauthorizedAccessException(UpdaterText.WindowsUpdateAccessTheUpdaterFileCannotBeReadWithTheCurrent);

                throw new IOException(UpdaterText.WindowsUpdateAccessTheUpdaterFileCouldNotBeOpenedSafely, new Win32Exception(error));
            }

            InstallationIdentity.ValidateRegular(handle);
            return new StableReadStream(handle, ancestors, writable ? FileAccess.ReadWrite : FileAccess.Read);
        }
        catch
        {
            handle.Dispose();
            ancestors.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Builds inheritable administrator/system writes and ordinary-user read access for recovery.
    /// </summary>
    /// <returns>A protected directory security descriptor.</returns>
    internal static DirectorySecurity DirectoryPolicy()
    {
        var policy = new DirectorySecurity();
        policy.SetAccessRuleProtection(true, false);
        policy.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        policy.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        policy.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        policy.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), FileSystemRights.ReadAndExecute, inheritance, PropagationFlags.None, AccessControlType.Allow));
        return policy;
    }

    /// <summary>
    /// Locks every existing ancestor against rename or redirection until the caller releases it.
    /// </summary>
    /// <param name="path">The executable or destination whose ancestry must remain stable.</param>
    /// <returns>Owned directory handles which the caller must dispose.</returns>
    internal static List<SafeFileHandle> LockAncestors(string path)
    {
        var handles = new List<SafeFileHandle>();
        try
        {
            var directories = new Stack<string>();
            for (var current = Path.GetDirectoryName(PlainPaths.Full(path)); current is not null; current = Path.GetDirectoryName(current))
                directories.Push(current);

            foreach (var current in directories)
            {
                if (!Directory.Exists(current))
                    continue;

                var handle = CreateFileW(ExtendedPrefix + current, 0, FileShare.ReadWrite, IntPtr.Zero, OpenExisting, BackupSemantics | OpenReparsePoint, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    handle.Dispose();
                    throw new Win32Exception(Marshal.GetLastWin32Error(), UpdaterText.WindowsUpdateAccessTheInstallationPathCouldNotBeHeldStable);
                }

                handles.Add(handle);
                InstallationIdentity.ValidateDirectory(handle);
                PlainPaths.Full(current);
            }

            return handles;
        }
        catch
        {
            foreach (var handle in handles)
                handle.Dispose();

            throw;
        }
    }

    /// <summary>
    /// Holds a destination's existing parents only for the current operation so planned directory removal remains possible.
    /// </summary>
    /// <param name="path">The checked source or destination.</param>
    /// <returns>The disposable ancestor locks.</returns>
    internal static IDisposable HoldAncestors(string path)
        => new AncestorLease(LockAncestors(path));

    /// <summary>
    /// Owns directory handles for one bounded filesystem operation.
    /// </summary>
    /// <remarks>
    /// Takes ownership of the already validated native directory handles.
    /// </remarks>
    /// <param name="handles">The ancestor handles to release.</param>
    private sealed class AncestorLease(List<SafeFileHandle> handles) : IDisposable
    {
        /// <summary>
        /// Releases all held directories.
        /// </summary>
        public void Dispose()
        {
            foreach (var handle in handles)
                handle.Dispose();
        }
    }

    /// <summary>
    /// Keeps file contents and their ancestor directories stable for the entire read.
    /// </summary>
    /// <remarks>
    /// Owns both the validated ordinary-file handle and its directory lease.
    /// </remarks>
    /// <param name="handle">The authenticated ordinary-file handle.</param>
    /// <param name="ancestors">The held parent directories.</param>
    /// <param name="access">The access granted by the validated file handle.</param>
    private sealed class StableReadStream(SafeFileHandle handle, IDisposable ancestors, FileAccess access) : FileStream(handle, access)
    {
        /// <summary>
        /// Closes the file before releasing its ancestry.
        /// </summary>
        /// <param name="disposing">Whether managed disposal was requested.</param>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            ancestors.Dispose();
        }

        /// <summary>
        /// Releases asynchronous readers and their directory handles together.
        /// </summary>
        /// <returns>The completed release.</returns>
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync().ConfigureAwait(false);
            ancestors.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Requests UAC for the already locked helper with clean runtime settings and protected native extraction.
    /// </summary>
    /// <param name="executable">The authenticated locked standalone helper.</param>
    /// <param name="pipeName">The random bounded session name.</param>
    /// <param name="ownerId">The normal UI process that may initialize this session.</param>
    /// <param name="startProcess">An optional isolated-test process boundary; production uses Windows ShellExecute.</param>
    /// <returns>The exact process started by Windows.</returns>
    internal static Process Launch(string executable, string pipeName, int ownerId, Func<ProcessStartInfo, Process?>? startProcess = null)
    {
        var parent = PlainPaths.Child(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), CacheDirectory);
        if (Directory.Exists(parent))
            ValidateCacheParent(parent);

        var start = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = ElevationVerb, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System) };
        start.ArgumentList.Add(ProtectedUpdateProtocol.Argument);
        start.ArgumentList.Add(pipeName);
        start.ArgumentList.Add(ownerId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        lock (_environmentGate)
        {
            string[] names = [.. Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(name => _environmentPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) || _environmentNames.Contains(name, StringComparer.OrdinalIgnoreCase))];
            var saved = names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
            var extraction = PlainPaths.Child(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), CacheDirectory + '/' + Guid.NewGuid().ToString(TransactionStorage.GuidFormat));
            try
            {
                foreach (var name in names)
                    Environment.SetEnvironmentVariable(name, null);

                Environment.SetEnvironmentVariable(BundleDirectory, extraction);
                Environment.SetEnvironmentVariable(_environmentNames[1], Environment.GetFolderPath(Environment.SpecialFolder.System));
                Environment.SetEnvironmentVariable(_environmentNames[2], extraction);
                Environment.SetEnvironmentVariable(_environmentNames[3], extraction);
                return (startProcess ?? Process.Start)(start) ?? throw new IOException(UpdaterText.WindowsUpdateAccessWindowsDidNotStartTheAdministratorHelper);
            }
            catch (Win32Exception error) when (error.NativeErrorCode == 1223)
            {
                throw new OperationCanceledException(UpdaterText.WindowsUpdateAccessAdministratorPermissionWasDeclinedTheInstallationWasNotReplaced, error);
            }
            finally
            {
                Environment.SetEnvironmentVariable(BundleDirectory, null);
                foreach (var name in _environmentNames)
                    Environment.SetEnvironmentVariable(name, null);

                foreach (var item in saved)
                    Environment.SetEnvironmentVariable(item.Key, item.Value);
            }
        }
    }

    /// <summary>
    /// Opens a stable directory handle with the supplied access and sharing restrictions.
    /// </summary>
    /// <param name="name">The checked path.</param>
    /// <param name="access">The requested access.</param>
    /// <param name="share">The permitted sharing.</param>
    /// <param name="security">The unused security pointer.</param>
    /// <param name="creation">The open mode.</param>
    /// <param name="flags">The directory flags.</param>
    /// <param name="template">The unused template.</param>
    /// <returns>The owned native handle.</returns>
    [LibraryImport(KernelLibrary, StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFileW(string name, uint access, FileShare share, IntPtr security, uint creation, uint flags, IntPtr template);
}
