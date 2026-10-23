using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace XppAiCopilotCompanion.McpServer
{
    /// <summary>
    /// Minimal MCP (Model Context Protocol) server over HTTP (streamable HTTP).
    /// Exposes D365FO X++ tools so GitHub Copilot in VS can call them.
    /// Protocol: JSON-RPC 2.0 over HTTP POST. Falls back to stdio if --stdio.
    ///
    /// All tool logic is delegated to <see cref="ToolRouter"/> which in turn
    /// delegates to the MetaModel bridge running inside the VSIX process.
    /// This file is purely transport + JSON-RPC dispatch.
    /// </summary>
    internal static class Program
    {
        private const string ServerName = "xpp-copilot-companion";
        private const string ServerVersion = "0.11.0";

        private const int DefaultPort = 21329;
        private static readonly string PortFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XppCopilotCompanion", "mcp-port.txt");

        // stdio streams (only used in --stdio mode)
        private static Stream _stdin;
        private static Stream _stdout;

        // Tool routing
        private static ToolRouter _router;

        static void Main(string[] args)
        {
            McpLogger.Log("MCP server starting. pid=" + System.Diagnostics.Process.GetCurrentProcess().Id
                + " args=" + string.Join(" ", args));

            _router = new ToolRouter(new BridgeClient());

            bool useStdio = Array.Exists(args, a => a.Equals("--stdio", StringComparison.OrdinalIgnoreCase));

            // The server lives as long as the Visual Studio instances that use it: the one that started it (--parent)
            // and the ones that found it running and registered (GET /register?pid=). It exits when none is left.
            int parentIndex = Array.FindIndex(args, a => a.Equals("--parent", StringComparison.OrdinalIgnoreCase));
            if (parentIndex >= 0 && parentIndex + 1 < args.Length && int.TryParse(args[parentIndex + 1], out int parentPid))
                RegisterParent(parentPid);
            StartParentWatchdog();

            if (useStdio)
                RunStdio();
            else
                RunHttp();
        }

        // ── Lifetime: exit when no registered Visual Studio is running ──

        private static readonly System.Collections.Generic.HashSet<int> Parents = new System.Collections.Generic.HashSet<int>();

        internal static void RegisterParent(int pid)
        {
            lock (Parents)
            {
                if (Parents.Add(pid)) McpLogger.Log("Registered Visual Studio pid=" + pid);
            }
        }

        private static void StartParentWatchdog()
        {
            var thread = new Thread(() =>
            {
                while (true)
                {
                    Thread.Sleep(5000);
                    int[] pids;
                    lock (Parents) pids = new System.Collections.Generic.List<int>(Parents).ToArray();
                    if (pids.Length == 0) continue;   // started without --parent (manual run): stay up
                    foreach (int pid in pids)
                        if (!IsAlive(pid))
                            lock (Parents)
                            {
                                Parents.Remove(pid);
                                McpLogger.Log("Visual Studio pid=" + pid + " exited.");
                            }
                    bool none;
                    lock (Parents) none = Parents.Count == 0;
                    if (none)
                    {
                        McpLogger.Log("No Visual Studio left; MCP server exiting.");
                        Environment.Exit(0);
                    }
                }
            }) { IsBackground = true, Name = "ParentWatchdog" };
            thread.Start();
        }

        private static bool IsAlive(int pid)
        {
            try
            {
                using (var p = System.Diagnostics.Process.GetProcessById(pid))
                    return !p.HasExited;
            }
            catch (ArgumentException) { return false; }
            catch { return true; }   // no access: assume it is still running
        }

        // ── HTTP mode ──

        static void RunHttp()
        {
            int port = DefaultPort;
            HttpListener listener = null;

            try
            {
                listener = new HttpListener();
                string prefix = "http://127.0.0.1:" + port + "/";
                listener.Prefixes.Add(prefix);
                listener.Start();
                McpLogger.Log("HTTP listening on " + prefix);
                Console.Error.WriteLine("[" + ServerName + "] HTTP listening on " + prefix);
            }
            catch (Exception ex)
            {
                McpLogger.Log("Port " + port + " already in use, exiting. (" + ex.Message + ")");
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PortFilePath));
                File.WriteAllText(PortFilePath, port.ToString());
                McpLogger.Log("Wrote port file: " + PortFilePath);
            }
            catch (Exception ex)
            {
                McpLogger.Log("Failed to write port file: " + ex.Message);
            }

            while (true)
            {
                try
                {
                    var ctx = listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleHttpRequest(ctx));
                }
                catch (Exception ex)
                {
                    McpLogger.Log("HTTP accept error: " + ex.Message);
                    break;
                }
            }
        }

        static void HandleHttpRequest(HttpListenerContext ctx)
        {
            try
            {
                if (ctx.Request.HttpMethod == "OPTIONS")
                {
                    ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
                    ctx.Response.AddHeader("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
                    ctx.Response.AddHeader("Access-Control-Allow-Headers", "Content-Type");
                    ctx.Response.StatusCode = 204;
                    ctx.Response.Close();
                    return;
                }

                if (ctx.Request.HttpMethod == "GET" && ctx.Request.Url.AbsolutePath.TrimEnd('/').Equals("/register", StringComparison.OrdinalIgnoreCase))
                {
                    bool ok = int.TryParse(ctx.Request.QueryString["pid"], out int pid);
                    if (ok) RegisterParent(pid);
                    SendJsonResponse(ctx, ok ? 200 : 400, ok ? "{\"registered\":" + pid + "}" : "{\"error\":\"pid is required\"}");
                    return;
                }

                if (ctx.Request.HttpMethod == "GET")
                {
                    string info = "{\"name\":\"" + ServerName + "\",\"version\":\"" + ServerVersion + "\"}";
                    SendJsonResponse(ctx, 200, info);
                    return;
                }

                if (ctx.Request.HttpMethod != "POST")
                {
                    ctx.Response.StatusCode = 405;
                    ctx.Response.Close();
                    return;
                }

                string requestBody;
                using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    requestBody = reader.ReadToEnd();

                McpLogger.Log("HTTP recv " + requestBody.Length + " chars: "
                    + (requestBody.Length > 300 ? requestBody.Substring(0, 300) + "..." : requestBody));

                string idToken = null;
                string response = null;
                try
                {
                    // Extract id before HandleMessage so we can build an error
                    // response even if HandleMessage throws.
                    idToken = JsonHelpers.ExtractJsonValueToken(requestBody, "id");
                    response = Dispatch(requestBody);
                }
                catch (Exception innerEx)
                {
                    McpLogger.Log("HandleMessage exception: " + innerEx);
                    response = idToken != null
                        ? JsonHelpers.BuildError(idToken, -32603,
                              "Internal error: " + innerEx.Message)
                        : null;
                }

                if (response != null)
                {
                    McpLogger.Log("HTTP send " + response.Length + " chars");
                    ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
                    SendJsonResponse(ctx, 200, response);
                }
                else
                {
                    ctx.Response.StatusCode = 204;
                    ctx.Response.Close();
                }
            }
            catch (Exception ex)
            {
                McpLogger.Log("HTTP handler error: " + ex.Message);
                try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
            }
        }

        static void SendJsonResponse(HttpListenerContext ctx, int status, string json)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = body.Length;
            ctx.Response.OutputStream.Write(body, 0, body.Length);
            ctx.Response.OutputStream.Close();
        }

        // ── Stdio mode ──

        static void RunStdio()
        {
            Console.InputEncoding = new UTF8Encoding(false);
            Console.OutputEncoding = new UTF8Encoding(false);
            _stdin = Console.OpenStandardInput();
            _stdout = Console.OpenStandardOutput();

            Console.Error.WriteLine("[" + ServerName + "] MCP server starting (stdio)...");
            McpLogger.Log("stdio mode active");

            try
            {
                while (true)
                {
                    string message = ReadStdioMessage();
                    if (message == null)
                    {
                        McpLogger.Log("stdin closed; exiting read loop.");
                        break;
                    }

                    McpLogger.Log("recv " + message.Length + " chars: "
                        + (message.Length > 200 ? message.Substring(0, 200) + "..." : message));

                    // Read-only agent tools run in parallel (searches, references, reads); everything else in order.
                    // Bridge calls still take turns on BridgeClient's single-worker queue.
                    string request = message;
                    if (IsParallelRead(request))
                    {
                        var task = System.Threading.Tasks.Task.Run(() => RespondStdio(request));
                        lock (_pendingReads)
                        {
                            _pendingReads.RemoveAll(t => t.IsCompleted);
                            _pendingReads.Add(task);
                        }
                    }
                    else
                        RespondStdio(request);
                }
            }
            catch (Exception ex)
            {
                McpLogger.Log("Fatal exception: " + ex);
                throw;
            }

            // Let reads still running answer before the process exits.
            System.Threading.Tasks.Task[] pending;
            lock (_pendingReads) pending = _pendingReads.ToArray();
            System.Threading.Tasks.Task.WaitAll(pending, 120000);
            Console.Error.WriteLine("[" + ServerName + "] MCP server exiting.");
            McpLogger.Log("MCP server exiting.");
        }

        private static readonly HashSet<string> ParallelTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "xpp_search_code", "xpp_find_examples", "xpp_find_references", "xpp_search_docs", "xpp_read_object", "xpp_find_object",
            "xpp_list_objects", "xpp_list_model_objects", "xpp_get_form_layout", "xpp_read_label", "xpp_get_model_info",
            "xpp_list_models", "xpp_get_environment", "xpp_get_object_type_schema", "xpp_list_project_items", "xpp_validate_object", "xpp_analyze_security",
        };

        private static string ToolCallName(string json) =>
            JsonHelpers.ExtractJsonString(json, "method") == "tools/call" ? JsonHelpers.ExtractNestedString(json, "params", "name") : null;

        private static bool IsParallelRead(string json) => ParallelTools.Contains(ToolCallName(json) ?? "");

        private static readonly object _outputLock = new object();
        private static readonly object _writeToolLock = new object();
        private static readonly List<System.Threading.Tasks.Task> _pendingReads = new List<System.Threading.Tasks.Task>();

        /// <summary>
        /// Handles a JSON-RPC message. Tool calls that change metadata or the project run one at a time (HTTP requests
        /// arrive on parallel threads); read-only tools and xpp_build (minutes long, outside the bridge queue) do not wait.
        /// </summary>
        static string Dispatch(string json)
        {
            string tool = ToolCallName(json);
            if (tool == null || ParallelTools.Contains(tool) || tool == "xpp_build")
                return HandleMessage(json);
            lock (_writeToolLock)
                return HandleMessage(json);
        }

        static void RespondStdio(string message)
        {
            string idToken = null;
            string response;
            try
            {
                idToken = JsonHelpers.ExtractJsonValueToken(message, "id");
                response = Dispatch(message);
            }
            catch (Exception ex)
            {
                McpLogger.Log("HandleMessage exception: " + ex);
                response = idToken != null ? JsonHelpers.BuildError(idToken, -32603, "Internal error: " + ex.Message) : null;
            }

            // One message at a time on stdout.
            if (response != null)
                lock (_outputLock)
                {
                    McpLogger.Log("send " + response.Length + " chars");
                    WriteStdioMessage(response);
                }
        }

        static string ReadStdioMessage()
        {
            int contentLength = -1;
            while (true)
            {
                string headerLine = ReadHeaderLine();
                if (headerLine == null) return null;
                if (headerLine.Length == 0) break;

                if (headerLine.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    string val = headerLine.Substring("Content-Length:".Length).Trim();
                    int.TryParse(val, out contentLength);
                }
            }

            if (contentLength <= 0) return null;

            byte[] buffer = new byte[contentLength];
            int totalRead = 0;
            while (totalRead < contentLength)
            {
                int read = _stdin.Read(buffer, totalRead, contentLength - totalRead);
                if (read == 0) return null;
                totalRead += read;
            }

            return Encoding.UTF8.GetString(buffer, 0, totalRead);
        }

        static string ReadHeaderLine()
        {
            var bytes = new List<byte>(128);
            while (true)
            {
                int b = _stdin.ReadByte();
                if (b < 0)
                {
                    if (bytes.Count == 0) return null;
                    break;
                }
                if (b == '\n') break;
                if (b != '\r') bytes.Add((byte)b);
            }
            return Encoding.ASCII.GetString(bytes.ToArray());
        }

        static void WriteStdioMessage(string json)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            byte[] header = Encoding.ASCII.GetBytes("Content-Length: " + body.Length + "\r\n\r\n");
            _stdout.Write(header, 0, header.Length);
            _stdout.Write(body, 0, body.Length);
            _stdout.Flush();
        }

        // ── JSON-RPC dispatch ──

        static string HandleMessage(string json)
        {
            string idToken = JsonHelpers.ExtractJsonValueToken(json, "id");
            string method = JsonHelpers.ExtractJsonString(json, "method");

            McpLogger.Log("method=" + method + " id=" + idToken);

            switch (method)
            {
                case "initialize":
                    string requestedProtocol = JsonHelpers.ExtractNestedString(json, "params", "protocolVersion");
                    string clientInfoJson = JsonHelpers.ExtractNestedObject(json, "params");
                    string clientName = JsonHelpers.ExtractJsonString(clientInfoJson, "name");
                    McpLogger.Log("initialize client=" + (string.IsNullOrWhiteSpace(clientName) ? "<unknown>" : clientName)
                        + " protocol=" + (string.IsNullOrWhiteSpace(requestedProtocol) ? "<none>" : requestedProtocol));
                    return BuildInitializeResponse(idToken, requestedProtocol);

                case "initialized":
                case "notifications/initialized":
                    return null;

                case "tools/list":
                    return _router.BuildToolsListResponse(idToken);

                case "tools/call":
                    return _router.HandleToolCall(idToken, json);

                case "shutdown":
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        Thread.Sleep(200);
                        McpLogger.Log("Shutdown requested — exiting.");
                        Environment.Exit(0);
                    });
                    return JsonHelpers.BuildResult(idToken, "{}");

                default:
                    if (idToken == null || method.StartsWith("notifications/"))
                    {
                        McpLogger.Log("ignoring notification: " + method);
                        return null;
                    }
                    return JsonHelpers.BuildError(idToken, -32601, "Method not found: " + method);
            }
        }

        private static string _cachedInstructions;

        static string LoadInstructions()
        {
            if (_cachedInstructions != null) return _cachedInstructions;
            try
            {
                string path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "XppCopilotCompanion", "system-prompt.txt");
                if (File.Exists(path))
                    _cachedInstructions = File.ReadAllText(path, Encoding.UTF8);
            }
            catch { }
            return _cachedInstructions ?? string.Empty;
        }

        static string BuildInitializeResponse(string idToken, string requestedProtocolVersion)
        {
            string protocol = string.IsNullOrWhiteSpace(requestedProtocolVersion)
                ? "2024-11-05"
                : requestedProtocolVersion;

            string instructions = LoadInstructions();
            string instructionsField = string.IsNullOrWhiteSpace(instructions)
                ? string.Empty
                : ",\n  \"instructions\": \"" + JsonHelpers.EscapeJsonString(instructions) + "\"";

            string result = @"{
  ""protocolVersion"": """ + JsonHelpers.EscapeJsonString(protocol) + @""",
  ""capabilities"": {
    ""tools"": { ""listChanged"": true }
  },
  ""serverInfo"": {
    ""name"": """ + ServerName + @""",
    ""version"": """ + ServerVersion + @"""
  }" + instructionsField + @"
}";
            return JsonHelpers.BuildResult(idToken, result);
        }
    }
}
