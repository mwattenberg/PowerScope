using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace PowerScope.Model.Mcp
{
    /// <summary>
    /// Minimal MCP (Model Context Protocol) server using the streamable HTTP transport.
    /// Implements the JSON-RPC subset MCP clients require - initialize, notifications,
    /// ping, tools/list and tools/call - with no external dependencies.
    ///
    /// Listens on localhost only: this keeps the server unreachable from other machines
    /// and lets HttpListener bind without administrator rights (the 'localhost' prefix
    /// is exempt from URL ACL reservations).
    ///
    /// Register in Claude Code with:
    ///   claude mcp add --transport http powerscope http://localhost:5642/mcp
    /// </summary>
    public class McpServer : IDisposable
    {
        public const int DefaultPort = 5642;

        private readonly HttpListener _listener;
        private readonly McpToolService _tools;
        private readonly string _sessionId = Guid.NewGuid().ToString("N");
        private bool _disposed;

        public int Port { get; }

        public McpServer(McpToolService tools, int port = DefaultPort)
        {
            _tools = tools ?? throw new ArgumentNullException(nameof(tools));
            Port = port;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");
        }

        public void Start()
        {
            _listener.Start();
            Task.Run(AcceptLoop);
        }

        private async Task AcceptLoop()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception)
                {
                    break; // listener stopped or disposed
                }

                _ = Task.Run(() => HandleRequest(context));
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            HttpListenerResponse response = context.Response;
            try
            {
                response.Headers["Mcp-Session-Id"] = _sessionId;

                switch (context.Request.HttpMethod)
                {
                    case "POST":
                        HandlePost(context.Request, response);
                        break;
                    case "DELETE":
                        // Session termination - nothing to clean up, sessions are stateless here
                        response.StatusCode = 200;
                        break;
                    default:
                        // No server-initiated SSE stream is offered (GET) and other verbs are unsupported
                        response.StatusCode = 405;
                        break;
                }
            }
            catch (Exception)
            {
                try { response.StatusCode = 500; } catch { }
            }
            finally
            {
                try { response.Close(); } catch { }
            }
        }

        private void HandlePost(HttpListenerRequest request, HttpListenerResponse response)
        {
            string body;
            using (StreamReader reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8))
            {
                body = reader.ReadToEnd();
            }

            JsonNode parsed;
            try
            {
                parsed = JsonNode.Parse(body);
            }
            catch
            {
                WriteJson(response, RpcError(null, -32700, "Parse error"));
                return;
            }

            JsonNode reply;
            if (parsed is JsonArray batch)
            {
                JsonArray replies = new JsonArray();
                foreach (JsonNode message in batch)
                {
                    JsonObject singleReply = ProcessMessage(message as JsonObject);
                    if (singleReply != null)
                        replies.Add(singleReply);
                }
                reply = replies.Count > 0 ? replies : null;
            }
            else
            {
                reply = ProcessMessage(parsed as JsonObject);
            }

            if (reply == null)
            {
                // Notification(s) only - acknowledge without a body
                response.StatusCode = 202;
                return;
            }

            WriteJson(response, reply);
        }

        /// <summary>
        /// Processes a single JSON-RPC message. Returns the response object,
        /// or null for notifications and client responses (which get no reply).
        /// </summary>
        private JsonObject ProcessMessage(JsonObject message)
        {
            if (message == null)
                return RpcError(null, -32600, "Invalid request");

            string method = (string)message["method"];
            if (method == null)
                return null; // a response from the client - nothing to do

            JsonNode id = message["id"];
            if (id == null)
                return null; // notification (e.g. notifications/initialized)

            try
            {
                JsonNode result;
                switch (method)
                {
                    case "initialize":
                        result = HandleInitialize(message["params"] as JsonObject);
                        break;
                    case "ping":
                        result = new JsonObject();
                        break;
                    case "tools/list":
                        result = new JsonObject { ["tools"] = _tools.GetToolDefinitions() };
                        break;
                    case "tools/call":
                        result = HandleToolsCall(message["params"] as JsonObject);
                        break;
                    default:
                        return RpcError(id, -32601, $"Method not found: {method}");
                }

                return new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id.DeepClone(),
                    ["result"] = result
                };
            }
            catch (Exception ex)
            {
                return RpcError(id, -32603, ex.Message);
            }
        }

        private JsonNode HandleInitialize(JsonObject parameters)
        {
            // Echo the client's protocol version - this server only uses features
            // common to all streamable-HTTP protocol revisions
            string protocolVersion = (string)(parameters?["protocolVersion"]) ?? "2025-06-18";

            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0";

            return new JsonObject
            {
                ["protocolVersion"] = protocolVersion,
                ["capabilities"] = new JsonObject
                {
                    ["tools"] = new JsonObject()
                },
                ["serverInfo"] = new JsonObject
                {
                    ["name"] = "PowerScope",
                    ["version"] = version
                },
                ["instructions"] = "PowerScope real-time data acquisition. Call get_status to discover active streams and channels, " +
                                   "read_samples / get_measurements to access waveform data, clear_data before capturing a transient. " +
                                   "Use add_demo_stream (synthetic signals) or load_config (saved hardware session) to set up acquisition."
            };
        }

        private JsonNode HandleToolsCall(JsonObject parameters)
        {
            string name = (string)(parameters?["name"]);
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("tools/call requires a 'name' parameter");

            JsonObject arguments = parameters["arguments"] as JsonObject ?? new JsonObject();

            string text;
            bool isError = false;
            try
            {
                text = _tools.CallTool(name, arguments).ToJsonString();
            }
            catch (McpToolException ex)
            {
                text = ex.Message;
                isError = true;
            }
            catch (Exception ex)
            {
                // Unexpected tool failures are still reported as tool errors so the
                // AI client can read the message and adjust, per the MCP spec
                text = $"Tool '{name}' failed: {ex.Message}";
                isError = true;
            }

            return new JsonObject
            {
                ["content"] = new JsonArray
                {
                    new JsonObject { ["type"] = "text", ["text"] = text }
                },
                ["isError"] = isError
            };
        }

        private static JsonObject RpcError(JsonNode id, int code, string message)
        {
            return new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id?.DeepClone(),
                ["error"] = new JsonObject
                {
                    ["code"] = code,
                    ["message"] = message
                }
            };
        }

        private static void WriteJson(HttpListenerResponse response, JsonNode payload)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(payload.ToJsonString());
            response.StatusCode = 200;
            response.ContentType = "application/json";
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            try
            {
                if (_listener.IsListening)
                    _listener.Stop();
                _listener.Close();
            }
            catch { }

            GC.SuppressFinalize(this);
        }
    }
}
