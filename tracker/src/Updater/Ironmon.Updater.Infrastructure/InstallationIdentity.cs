using Ironmon.Updater.Core;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Binds a transaction to the original Windows directory object as well as its canonical pathname.
/// </summary>
internal static partial class InstallationIdentity
{
    private const string KernelLibrary = "kernel32.dll";
    private const string HexFormat = "X8";
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;

    /// <summary>
    /// Reads the volume serial and persistent directory file index without following reparse points.
    /// </summary>
    /// <param name="root">The validated existing installation directory.</param>
    /// <returns>The Windows filesystem identity.</returns>
    internal static string Read(string root)
    {
        using var handle = CreateFileW(PlainPaths.Full(root), 0, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, OpenExisting, BackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var information))
            throw new Win32Exception(Marshal.GetLastWin32Error(), UpdaterText.InstallationIdentityTheInstallationDirectoryIdentityCouldNotBeRead);

        return information.VolumeSerialNumber.ToString(HexFormat) + information.FileIndexHigh.ToString(HexFormat) + information.FileIndexLow.ToString(HexFormat);
    }

    /// <summary>
    /// Rejects redirected, directory and multiply linked file handles before elevated reads or ACL changes.
    /// </summary>
    /// <param name="handle">The open file handle whose final identity is checked.</param>
    internal static void ValidateRegular(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var information) || (information.Attributes & (uint)(FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 || information.Links != 1)
            throw new IOException(UpdaterText.InstallationIdentityUpdaterInputMustBeAnOrdinaryFileWithOne);
    }

    /// <summary>
    /// Rejects a redirected or non-directory ancestor using its actual opened handle.
    /// </summary>
    /// <param name="handle">The locked ancestor directory.</param>
    internal static void ValidateDirectory(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var information) || (information.Attributes & (uint)(FileAttributes.ReparsePoint | FileAttributes.Directory)) != (uint)FileAttributes.Directory)
            throw new IOException(UpdaterText.InstallationIdentityUpdaterAncestorsMustBeOrdinaryDirectories);
    }

    /// <summary>
    /// Opens a directory handle using Windows backup semantics.
    /// </summary>
    /// <param name="name">The checked directory path.</param>
    /// <param name="access">The metadata access mask.</param>
    /// <param name="share">The allowed sharing flags.</param>
    /// <param name="security">The unused security descriptor.</param>
    /// <param name="creation">The open-existing mode.</param>
    /// <param name="flags">The directory flag.</param>
    /// <param name="template">The unused template.</param>
    /// <returns>The owned directory handle.</returns>
    [LibraryImport(KernelLibrary, EntryPoint = nameof(CreateFileW), StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFileW(string name, uint access, FileShare share, IntPtr security, uint creation, uint flags, IntPtr template);

    /// <summary>
    /// Reads the filesystem identity associated with an open handle.
    /// </summary>
    /// <param name="handle">The directory handle.</param>
    /// <param name="information">The returned Windows file information.</param>
    /// <returns>Whether Windows returned the identity.</returns>
    [LibraryImport(KernelLibrary, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);

    /// <summary>
    /// Matches the native BY_HANDLE_FILE_INFORMATION layout.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        internal uint Attributes;
        internal uint CreationLow;
        internal uint CreationHigh;
        internal uint AccessLow;
        internal uint AccessHigh;
        internal uint WriteLow;
        internal uint WriteHigh;
        internal uint VolumeSerialNumber;
        internal uint SizeHigh;
        internal uint SizeLow;
        internal uint Links;
        internal uint FileIndexHigh;
        internal uint FileIndexLow;
    }
}
