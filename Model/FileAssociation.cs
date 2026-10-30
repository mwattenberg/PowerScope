using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PowerScope.Model
{
    /// <summary>
    /// Registers the .psp session-file extension with this PowerScope.exe under
    /// HKCU\Software\Classes so double-clicking (or "Open with") a saved session
    /// launches PowerScope with that file. Per-user, no admin rights required.
    /// Windows' UserChoice protection (Windows 8+) means this only sets a default
    /// when none exists yet; if .psp is already claimed by another app, the user
    /// must pick PowerScope once via Explorer's "Open with" &gt; "Always use this app".
    /// </summary>
    public static class FileAssociation
    {
        private const string Extension = ".psp";
        private const string ProgId = "PowerScope.SessionFile";

        public static void EnsureRegistered()
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath) || !exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    return; // e.g. running via `dotnet run` - nothing sensible to register

                using (RegistryKey classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes"))
                {
                    using (RegistryKey extKey = classes.CreateSubKey(Extension))
                        extKey.SetValue(null, ProgId);

                    using (RegistryKey progIdKey = classes.CreateSubKey(ProgId))
                    {
                        progIdKey.SetValue(null, "PowerScope Session");

                        using (RegistryKey iconKey = progIdKey.CreateSubKey("DefaultIcon"))
                            iconKey.SetValue(null, $"\"{exePath}\",0");

                        using (RegistryKey commandKey = progIdKey.CreateSubKey(@"shell\open\command"))
                            commandKey.SetValue(null, $"\"{exePath}\" \"%1\"");
                    }
                }

                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
            catch
            {
                // Best-effort only; association is a convenience, not required for the app to run.
            }
        }

        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const int SHCNF_IDLIST = 0x0000;

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);
    }
}
