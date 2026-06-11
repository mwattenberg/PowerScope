using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using PowerScope.Model.Mcp;
using Xunit;

namespace PowerScope.Tests
{
    /// <summary>
    /// Exercises the MCP server over real HTTP, the same way an MCP client
    /// (e.g. Claude Code) talks to it: JSON-RPC over the streamable HTTP transport.
    /// </summary>
    public class McpServerProtocolTests : IDisposable
    {
        private readonly TestMcpHost _host;
        private readonly McpServer _server;
        private readonly HttpClient _client;
        private readonly string _url;
        private int _nextId = 1;

        public McpServerProtocolTests()
        {
            _host = new TestMcpHost();
            _host.AddDemoStream(2, 5000, "Sine Wave");

            int port = GetFreePort();
            _server = new McpServer(new McpToolService(_host), port);
            _server.Start();

            _url = $"http://localhost:{port}/mcp";
            _client = new HttpClient();
        }

        private static int GetFreePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private async Task<HttpResponseMessage> PostRaw(JsonObject message)
        {
            StringContent content = new StringContent(message.ToJsonString(), Encoding.UTF8, "application/json");
            return await _client.PostAsync(_url, content);
        }

        private async Task<JsonObject> SendRequest(string method, JsonObject parameters = null)
        {
            JsonObject request = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = _nextId++,
                ["method"] = method
            };
            if (parameters != null)
                request["params"] = parameters;

            HttpResponseMessage response = await PostRaw(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            return (JsonObject)JsonNode.Parse(body);
        }

        [Fact]
        public async Task Initialize_ReturnsServerInfoAndSessionId()
        {
            JsonObject request = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "initialize",
                ["params"] = new JsonObject
                {
                    ["protocolVersion"] = "2025-06-18",
                    ["capabilities"] = new JsonObject(),
                    ["clientInfo"] = new JsonObject { ["name"] = "test", ["version"] = "1.0" }
                }
            };

            HttpResponseMessage response = await PostRaw(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.Contains("Mcp-Session-Id"));

            JsonObject reply = (JsonObject)JsonNode.Parse(await response.Content.ReadAsStringAsync());
            JsonObject result = (JsonObject)reply["result"];

            Assert.Equal("2025-06-18", (string)result["protocolVersion"]);
            Assert.Equal("PowerScope", (string)result["serverInfo"]["name"]);
            Assert.NotNull(result["capabilities"]["tools"]);
        }

        [Fact]
        public async Task Notification_IsAcknowledgedWith202()
        {
            JsonObject notification = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "notifications/initialized"
            };

            HttpResponseMessage response = await PostRaw(notification);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        [Fact]
        public async Task ToolsList_ContainsAllExpectedTools()
        {
            JsonObject reply = await SendRequest("tools/list");
            JsonArray tools = (JsonArray)reply["result"]["tools"];

            string[] expected =
            {
                "get_status", "read_samples", "get_measurements", "clear_data",
                "add_demo_stream", "load_config", "remove_all_streams"
            };

            List<string> names = new List<string>();
            foreach (JsonNode tool in tools)
                names.Add((string)tool["name"]);

            foreach (string name in expected)
                Assert.Contains(name, names);
        }

        [Fact]
        public async Task ToolsCall_GetStatus_ReturnsStreamInfo()
        {
            JsonObject reply = await SendRequest("tools/call", new JsonObject
            {
                ["name"] = "get_status",
                ["arguments"] = new JsonObject()
            });

            JsonObject result = (JsonObject)reply["result"];
            Assert.False((bool)result["isError"]);

            string text = (string)result["content"][0]["text"];
            JsonObject status = (JsonObject)JsonNode.Parse(text);
            Assert.Equal(2, (int)status["total_channels"]);
        }

        [Fact]
        public async Task ToolsCall_BadChannel_ReturnsToolErrorNotProtocolError()
        {
            JsonObject reply = await SendRequest("tools/call", new JsonObject
            {
                ["name"] = "read_samples",
                ["arguments"] = new JsonObject { ["channel"] = 99 }
            });

            // Tool failures must be in-result errors, not JSON-RPC errors
            Assert.Null(reply["error"]);
            Assert.True((bool)reply["result"]["isError"]);
        }

        [Fact]
        public async Task UnknownMethod_ReturnsMethodNotFound()
        {
            JsonObject reply = await SendRequest("resources/list");
            Assert.Equal(-32601, (int)reply["error"]["code"]);
        }

        [Fact]
        public async Task Ping_ReturnsEmptyResult()
        {
            JsonObject reply = await SendRequest("ping");
            Assert.NotNull(reply["result"]);
            Assert.Null(reply["error"]);
        }

        public void Dispose()
        {
            _client.Dispose();
            _server.Dispose();
            _host.Dispose();
        }
    }
}
