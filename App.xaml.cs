using System.Configuration;
using System.Data;
using System.Windows;
using PowerScope.Model.Mcp;

namespace PowerScope
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// Parses command line arguments before MainWindow is created:
    ///   --config &lt;path&gt;    load the given session XML instead of Settings.xml
    ///   --mcp-port &lt;port&gt;  port for the MCP server (default 5642)
    ///   --no-mcp           disable the MCP server
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Session XML to load at startup instead of the default Settings.xml,
        /// set via --config. Null when not specified.
        /// </summary>
        public static string ConfigFilePath { get; private set; }

        /// <summary>
        /// Port the MCP server listens on (localhost only), set via --mcp-port.
        /// </summary>
        public static int McpPort { get; private set; } = McpServer.DefaultPort;

        /// <summary>
        /// Whether the MCP server should be started, cleared via --no-mcp.
        /// </summary>
        public static bool McpEnabled { get; private set; } = true;

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

                    case "--mcp-port":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out int port) && port > 0 && port <= 65535)
                        {
                            McpPort = port;
                            i++;
                        }
                        break;

                    case "--no-mcp":
                        McpEnabled = false;
                        break;
                }
            }
        }
    }
}
