using System.IO;
using System.Security;
using Microsoft.Win32;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Updates;

namespace Coreloom.App.Services;

/// <summary>
/// Keeps the SHA-256 of a downloaded update under <c>HKEY_LOCAL_MACHINE\SOFTWARE\Coreloom\Updates</c>. Only
/// administrators can write there, so another program running as the user can't swap in its own installer or zip (and
/// its own hash) for Coreloom to install with administrator rights. Coreloom always runs elevated, so it can write the
/// key without a prompt. The value is cleared once the update is installed.
/// </summary>
/// <remarks>
/// The installed copy uses the key itself (as Coreloom 1.1.0 did). Each portable or unzipped copy gets its own subkey,
/// <c>Copies\&lt;id of its folder&gt;</c>, so copies in different folders don't overwrite each other's download.
/// </remarks>
public sealed class RegistryUpdateTrustStore : IUpdateTrustStore
{
    private const string RootPath = @"SOFTWARE\Coreloom";
    private const string UpdatesPath = RootPath + @"\Updates";
    private const string CopiesPath = UpdatesPath + @"\Copies";
    private const string VersionValue = "PendingVersion";
    private const string HashValue = "PendingSha256";

    private readonly string _keyPath;

    /// <param name="copyId">Null for the installed copy; <see cref="FolderUpdate.CopyId"/> for any other copy.</param>
    public RegistryUpdateTrustStore(string? copyId = null)
    {
        _keyPath = copyId is null ? UpdatesPath : $@"{CopiesPath}\{copyId}";
    }

    public bool TrySave(string version, string sha256)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = root.CreateSubKey(_keyPath, writable: true);
            key.SetValue(VersionValue, version, RegistryValueKind.String);
            key.SetValue(HashValue, sha256.Trim().ToLowerInvariant(), RegistryValueKind.String);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            Log.Warn("Updates", "Recording the update's hash in the registry failed.", ex);
            return false;
        }
    }

    public (string Version, string Sha256)? Load()
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = root.OpenSubKey(_keyPath, writable: false);
            if (key?.GetValue(VersionValue) is string version && key.GetValue(HashValue) is string hash && hash.Length == 64)
            {
                return (version, hash);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            Log.Warn("Updates", "Reading the update's hash from the registry failed.", ex);
        }

        return null;
    }

    /// <summary>Removes this copy's values, then any key left empty (other copies' values stay).</summary>
    public void Clear()
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using (var key = root.OpenSubKey(_keyPath, writable: true))
            {
                key?.DeleteValue(VersionValue, throwOnMissingValue: false);
                key?.DeleteValue(HashValue, throwOnMissingValue: false);
            }

            // Deepest first, so a parent left empty by its child's removal goes too.
            string[] prune = _keyPath == UpdatesPath ? [CopiesPath, UpdatesPath, RootPath] : [_keyPath, CopiesPath, UpdatesPath, RootPath];
            foreach (string path in prune)
            {
                bool empty;
                using (var key = root.OpenSubKey(path, writable: false))
                {
                    empty = key is { SubKeyCount: 0, ValueCount: 0 };
                }

                if (empty)
                {
                    root.DeleteSubKey(path, throwOnMissingSubKey: false);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException or InvalidOperationException)
        {
            Log.Warn("Updates", "Clearing the update's hash from the registry failed.", ex);
        }
    }
}
