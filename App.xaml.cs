using System.Windows;
using PowerScope.Model;

namespace PowerScope
{
    /// <summary>
    /// Parses command line arguments before MainWindow is created:
    ///   --config &lt;path&gt;  load the given session file instead of Settings.xml
    ///   &lt;path&gt;           a bare path (no leading "--") is treated the same way -
    ///                    this is how Explorer invokes PowerScope when the user
    ///                    double-clicks (or "Open with"s) a registered .psp/.xml session file
    /// The TCP MCP server is enabled/disabled via the PlotSettings UI
    /// (persisted in the session file), not via a command line switch.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Session file to load at startup instead of the default Settings.xml.
        /// Set via --config or a bare file path argument. Null when not specified.
        /// </summary>
        public static string ConfigFilePath { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            ParseCommandLine(e.Args);
            FileAssociation.EnsureRegistered();
            base.OnStartup(e);
        }

        private static void ParseCommandLine(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.Equals("--config", System.StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length)
                        ConfigFilePath = args[++i];
                }
                else if (!arg.StartsWith("--") && ConfigFilePath == null)
                {
                    // Bare path from Explorer double-click / "Open with"
                    ConfigFilePath = arg;
                }
            }
        }
    }
}
