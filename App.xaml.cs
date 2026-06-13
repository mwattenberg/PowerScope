using System.Configuration;
using System.Data;
using System.Windows;

namespace PowerScope
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// Parses command line arguments before MainWindow is created:
    ///   --config &lt;path&gt;  load the given session XML instead of Settings.xml
    ///   --stdio           start the MCP server on stdin/stdout (for Claude Desktop)
    ///   --no-mcp          disable the MCP server (no-op when --stdio is not set)
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Session XML to load at startup instead of the default Settings.xml,
        /// set via --config. Null when not specified.
        /// </summary>
        public static string ConfigFilePath { get; private set; }

        /// <summary>
        /// Whether to run the MCP stdio server. Enabled by --stdio so the process
        /// can serve as a Claude Desktop MCP server while also showing its window.
        /// </summary>
        public static bool McpEnabled { get; private set; } = false;

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

                    case "--stdio":
                        McpEnabled = true;
                        break;

                    case "--no-mcp":
                        McpEnabled = false;
                        break;
                }
            }
        }
    }
}
