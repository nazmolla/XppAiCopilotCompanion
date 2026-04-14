using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace XppAiCopilotCompanion.McpServer
{
    /// <summary>
    /// HTTP client that calls the MetaModel bridge running inside the VSIX process.
    /// All MetaModel operations are delegated to the bridge — the MCP server
    /// never manipulates metadata directly.
    /// </summary>
    internal sealed class BridgeClient
    {
        private const string DefaultBridgeUrl = "http://127.0.0.1:21330/";
        private readonly string _bridgeUrl;

        // Timeout for bridge tool calls (seconds)
        private const int CallTimeoutMs = 60_000;
        // Timeout for health-check pings (seconds)
        private const int HealthTimeoutMs = 5_000;

        // Limit concurrent bridge calls to prevent ThreadPool exhaustion.
        // The bridge is a single-threaded VSIX process — piling up requests
        // only causes contention with no throughput gain.
        private static readonly SemaphoreSlim _concurrencyGate = new SemaphoreSlim(4, 4);

        public BridgeClient(string bridgeUrl = null)
        {
            _bridgeUrl = bridgeUrl ?? DefaultBridgeUrl;
        }

        /// <summary>
        /// WebClient subclass that applies a configurable timeout.
        /// </summary>
        private sealed class TimeoutWebClient : WebClient
        {
            public int TimeoutMs { get; set; } = CallTimeoutMs;
            protected override WebRequest GetWebRequest(Uri uri)
            {
                var req = base.GetWebRequest(uri);
                req.Timeout = TimeoutMs;
                return req;
            }
        }

        /// <summary>
        /// Checks if the MetaModel bridge is alive.
        /// </summary>
        public bool IsAvailable()
        {
            try
            {
                using (var client = new TimeoutWebClient { TimeoutMs = HealthTimeoutMs })
                {
                    client.Encoding = Encoding.UTF8;
                    string response = client.DownloadString(_bridgeUrl);
                    return response != null && response.Contains("metamodel");
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Sends a JSON action to the bridge and returns the response JSON.
        /// Applies a concurrency gate and per-call timeout to prevent
        /// ThreadPool starvation when the bridge is slow.
        /// </summary>
        public string Call(string action, string bodyJson)
        {
            // Acquire concurrency slot (wait up to CallTimeoutMs)
            if (!_concurrencyGate.Wait(CallTimeoutMs))
            {
                McpLogger.Log("Bridge concurrency gate timeout for action=" + action);
                return "{\"success\":false,\"message\":\"Bridge is overloaded — too many concurrent requests. Try again.\"}";
            }

            try
            {
                using (var client = new TimeoutWebClient { TimeoutMs = CallTimeoutMs })
                {
                    client.Encoding = Encoding.UTF8;
                    client.Headers[HttpRequestHeader.ContentType] = "application/json";

                    string requestBody = InjectAction(bodyJson, action);
                    string response = client.UploadString(_bridgeUrl, requestBody);
                    return response;
                }
            }
            catch (WebException wex)
            {
                string detail = "";
                if (wex.Response is HttpWebResponse resp)
                {
                    using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                        detail = reader.ReadToEnd();
                }

                // Surface timeout specifically so callers see clear feedback
                if (wex.Status == WebExceptionStatus.Timeout)
                {
                    McpLogger.Log("Bridge call TIMEOUT for action=" + action);
                    return "{\"success\":false,\"message\":\"Bridge call timed out after "
                        + (CallTimeoutMs / 1000) + "s for action: " + action + "\"}";
                }

                return "{\"success\":false,\"message\":\"Bridge call failed: "
                    + JsonHelpers.EscapeJsonString(wex.Message + " " + detail) + "\"}";
            }
            catch (Exception ex)
            {
                return "{\"success\":false,\"message\":\"Bridge unavailable: "
                    + JsonHelpers.EscapeJsonString(ex.Message) + "\"}";
            }
            finally
            {
                _concurrencyGate.Release();
            }
        }

        private static string InjectAction(string json, string action)
        {
            // Ensure the JSON body has the "action" field at the top level
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
                return "{\"action\":\"" + action + "\"}";

            // Insert "action":"xxx" at the start of the JSON object
            string trimmed = json.Trim();
            if (trimmed.StartsWith("{"))
                return "{\"action\":\"" + action + "\"," + trimmed.Substring(1);

            return "{\"action\":\"" + action + "\"}";
        }
    }
}
