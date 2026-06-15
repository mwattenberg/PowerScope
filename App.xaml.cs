using System.Windows;

namespace PowerScope
{
    /// <summary>
    /// Parses command line arguments before MainWindow is created:
    ///   --config &lt;path&gt;  load the given session XML instead of Settings.xml
    /// The TCP MCP server is enabled/disabled via the PlotSettings UI
    /// (persisted in the session XML), not via a command line switch.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Session XML to load at startup instead of the default Settings.xml.
        /// Set via --config. Null when not specified.
        /// </summary>
        public static string ConfigFilePath { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            ParseCommandLine(e.Args);
            base.OnStartup(e);
        }

        private static void ParseCommandLine(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--config":
                        if (i + 1 < args.Length)
                            ConfigFilePath = args[++i];
                        break;
                }
            }
        }
    }
}
