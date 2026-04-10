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
        private const string ServerVersion = "0.6.0";

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

            if (useStdio)
                RunStdio();
            else
                RunHttp();
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

                string response = HandleMessage(requestBody);
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

                    string response = HandleMessage(message);
                    if (response != null)
                    {
                        McpLogger.Log("send " + response.Length + " chars");
                        WriteStdioMessage(response);
                    }
                }
            }
            catch (Exception ex)
            {
                McpLogger.Log("Fatal exception: " + ex);
                throw;
            }

            Console.Error.WriteLine("[" + ServerName + "] MCP server exiting.");
            McpLogger.Log("MCP server exiting.");
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

            Console.Error.WriteLine("[" + ServerName + "] method=" + method + " id=" + idToken);
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

        static string BuildInitializeResponse(string idToken, string requestedProtocolVersion)
        {
            string protocol = string.IsNullOrWhiteSpace(requestedProtocolVersion)
                ? "2024-11-05"
                : requestedProtocolVersion;

            string result = @"{
  ""protocolVersion"": """ + JsonHelpers.EscapeJsonString(protocol) + @""",
  ""capabilities"": {
    ""tools"": { ""listChanged"": true }
  },
  ""serverInfo"": {
    ""name"": """ + ServerName + @""",
    ""version"": """ + ServerVersion + @"""
  }
}";
            return JsonHelpers.BuildResult(idToken, result);
        }
    }
}
