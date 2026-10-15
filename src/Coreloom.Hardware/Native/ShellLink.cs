using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using System.Text;
using Coreloom.Core.Branding;

namespace Coreloom.Hardware.Native;

/// <summary>
/// A Windows shortcut (.lnk) opened through the Shell's own ShellLink object, to read where it points and which icon it
/// shows, and to change that icon. (It lives with the other Win32 interop so the Windows test run can exercise it on a
/// real shortcut.)
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class ShellLink : IDisposable
{
    private const int MaxPath = 32768;

    private readonly IShellLinkW _link;
    private readonly string? _path;

    private ShellLink(IShellLinkW link, string? path)
    {
        _link = link;
        _path = path;
    }

    /// <summary>Opens an existing shortcut.</summary>
    public static ShellLink Open(string shortcutPath)
    {
        var link = (IShellLinkW)new CShellLink();
        try
        {
            ((IPersistFile)link).Load(shortcutPath, 0 /* STGM_READ: saving writes the file anew */);
            return new ShellLink(link, shortcutPath);
        }
        catch
        {
            Marshal.FinalReleaseComObject(link);
            throw;
        }
    }

    /// <summary>Creates a shortcut to <paramref name="targetPath"/> (saved by <see cref="Save"/>).</summary>
    public static ShellLink Create(string shortcutPath, string targetPath)
    {
        var link = (IShellLinkW)new CShellLink();
        link.SetPath(targetPath);
        return new ShellLink(link, shortcutPath);
    }

    /// <summary>The file the shortcut starts (empty when it points at something that isn't a file).</summary>
    public string TargetPath
    {
        get
        {
            var buffer = new StringBuilder(MaxPath);
            try
            {
                _link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, 0);
            }
            catch (COMException)
            {
                return string.Empty;
            }

            return buffer.ToString();
        }
    }

    /// <summary>The icon it shows; <see cref="IconLocation.None"/> when it uses its target's own icon.</summary>
    public IconLocation Icon
    {
        get
        {
            var buffer = new StringBuilder(MaxPath);
            _link.GetIconLocation(buffer, buffer.Capacity, out int index);
            string path = buffer.ToString();
            return path.Length == 0 ? IconLocation.None : new IconLocation(Environment.ExpandEnvironmentVariables(path), index);
        }

        set => _link.SetIconLocation(value.Path, value.Index);
    }

    public void Save() => ((IPersistFile)_link).Save(_path, true);

    public void Dispose() => Marshal.FinalReleaseComObject(_link);

    /// <summary>
    /// Tells Explorer the shortcuts changed so it draws them again: each file, then (when any changed) the icon cache as a
    /// whole, which is what makes a pinned taskbar button pick up its new icon.
    /// </summary>
    public static void NotifyChanged(IReadOnlyCollection<string> shortcutPaths)
    {
        const int UpdateItem = 0x00002000;      // SHCNE_UPDATEITEM
        const int AssociationChanged = 0x08000000; // SHCNE_ASSOCCHANGED
        const uint PathW = 0x0005;              // SHCNF_PATHW
        const uint IdList = 0x0000;             // SHCNF_IDLIST
        const uint FlushNoWait = 0x3000;        // SHCNF_FLUSHNOWAIT
        foreach (string path in shortcutPaths)
        {
            IntPtr text = Marshal.StringToHGlobalUni(path);
            try
            {
                SHChangeNotify(UpdateItem, PathW | FlushNoWait, text, IntPtr.Zero);
            }
            finally
            {
                Marshal.FreeHGlobal(text);
            }
        }

        if (shortcutPaths.Count > 0)
        {
            SHChangeNotify(AssociationChanged, IdList | FlushNoWait, IntPtr.Zero, IntPtr.Zero);
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);

        void GetIDList(out IntPtr ppidl);

        void SetIDList(IntPtr pidl);

        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

        void GetHotkey(out short pwHotkey);

        void SetHotkey(short wHotkey);

        void GetShowCmd(out int piShowCmd);

        void SetShowCmd(int iShowCmd);

        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

        void Resolve(IntPtr hwnd, uint fFlags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
