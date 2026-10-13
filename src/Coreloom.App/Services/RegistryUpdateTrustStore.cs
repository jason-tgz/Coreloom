using System.IO;
using System.Security;
using Microsoft.Win32;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Updates;

namespace Coreloom.App.Services;

/// <summary>
/// Keeps the SHA-256 of a downloaded installer under <c>HKEY_LOCAL_MACHINE\SOFTWARE\Coreloom\Updates</c>. Only
/// administrators can write there, so another program running as the user can't swap in its own installer (and its
/// own hash) for Coreloom to run with administrator rights at the next start. Coreloom always runs elevated, so it
/// can write the key without a prompt. The key is cleared once the update is installed.
/// </summary>
public sealed class RegistryUpdateTrustStore : IUpdateTrustStore
{
    private const string KeyPath = @"SOFTWARE\Coreloom\Updates";
    private const string VersionValue = "PendingVersion";
    private const string HashValue = "PendingSha256";

    public bool TrySave(string version, string sha256)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = root.CreateSubKey(KeyPath, writable: true);
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
            using var key = root.OpenSubKey(KeyPath, writable: false);
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

    public void Clear()
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            root.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
            bool parentEmpty;
            using (var parent = root.OpenSubKey(@"SOFTWARE\Coreloom", writable: false))
            {
                parentEmpty = parent is { SubKeyCount: 0, ValueCount: 0 };
            }

            if (parentEmpty)
            {
                root.DeleteSubKey(@"SOFTWARE\Coreloom", throwOnMissingSubKey: false);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException or InvalidOperationException)
        {
            Log.Warn("Updates", "Clearing the update's hash from the registry failed.", ex);
        }
    }
}
