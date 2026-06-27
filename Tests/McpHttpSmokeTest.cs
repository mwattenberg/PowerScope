using System.Net.Http;
using System.Text;
using PowerScope.Model.Mcp;
using Xunit;

namespace PowerScope.Tests
{
    /// <summary>
    /// Verifies the embedded MCP server actually answers MCP JSON-RPC requests over
    /// plain HTTP (the Streamable HTTP transport) end to end — no companion bridge
    /// process, no stdio. Regression guard against MapMcp()/transport wiring breaking.
    /// </summary>
    public class McpHttpSmokeTest
    {
        [Fact]
        public async Task ServerRespondsOverHttp()
        {
            int port = 54399;
            using TestMcpHost host = new TestMcpHost();
            McpToolService tools = new McpToolService(host);
            using McpServer server = new McpServer(tools, port);
            server.Start();

            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept", "application/json, text/event-stream");
            string body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{},\"clientInfo\":{\"name\":\"smoke\",\"version\":\"1.0\"}}}";
            HttpResponseMessage response = await client.PostAsync(
                $"http://127.0.0.1:{port}/",
                new StringContent(body, Encoding.UTF8, "application/json"));

            string text = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, $"Status {response.StatusCode}: {text}");
            Assert.Contains("protocolVersion", text);
        }
    }
}
