using System;
using System.Collections.Concurrent;
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

        // Timeout for the HTTP request from the worker thread to the bridge
        private const int HttpTimeoutMs = 10_000;
        // Timeout for health-check pings
        private const int HealthTimeoutMs = 5_000;
        // How long a Call() caller waits for its queued request to complete
        private const int CallTimeoutMs = 15_000;
        // Cooldown after a bridge timeout before processing the next item
        private const int PostTimeoutCooldownMs = 3_000;

        // Serial request queue — one bridge call at a time.
        // Prevents cascading timeouts when the bridge's VS main thread is busy.
        private static readonly BlockingCollection<BridgeWorkItem> _queue
            = new BlockingCollection<BridgeWorkItem>(new ConcurrentQueue<BridgeWorkItem>());
        private static readonly Thread _worker;

        static BridgeClient()
        {
            _worker = new Thread(ProcessQueue)
            {
                IsBackground = true,
                Name = "McpBridgeQueue"
            };
            _worker.Start();
        }

        private sealed class BridgeWorkItem
        {
            public string BridgeUrl;
            public string Action;
            public string BodyJson;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
            public string Result;
            public volatile bool Cancelled;
        }

        public BridgeClient(string bridgeUrl = null)
        {
            _bridgeUrl = bridgeUrl ?? DefaultBridgeUrl;
        }

        /// <summary>
        /// WebClient subclass that applies a configurable timeout.
        /// </summary>
        private sealed class TimeoutWebClient : WebClient
        {
            public int TimeoutMs { get; set; } = HttpTimeoutMs;
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
        /// Enqueues a bridge request and waits for the single worker thread
        /// to process it. Returns the JSON response from the bridge, or a
        /// timeout/error JSON if the call could not complete in time.
        /// </summary>
        public string Call(string action, string bodyJson)
        {
            var item = new BridgeWorkItem
            {
                BridgeUrl = _bridgeUrl,
                Action = action,
                BodyJson = bodyJson
            };

            try { _queue.Add(item); }
            catch
            {
                return "{\"success\":false,\"message\":\"Bridge request queue is disposed.\"}";
            }

            if (item.Done.Wait(CallTimeoutMs))
                return item.Result;

            // Mark as cancelled so the worker thread skips this item
            // instead of making a wasted HTTP call to the bridge.
            item.Cancelled = true;

            McpLogger.Log("Bridge queue wait timeout for action=" + action
                + " (waited " + (CallTimeoutMs / 1000) + "s)");
            return "{\"success\":false,\"message\":\"Bridge call timed out after "
                + (CallTimeoutMs / 1000) + "s for action: " + action
                + ". A previous request may still be in progress.\"}";
        }

        /// <summary>
        /// Single worker thread: pulls items from the queue one at a time
        /// and sends each to the bridge via HTTP. After a timeout, adds a
        /// cooldown to let the bridge recover before the next call.
        /// </summary>
        private static void ProcessQueue()
        {
            foreach (var item in _queue.GetConsumingEnumerable())
            {
                // Skip items whose caller already timed out — no point
                // making an HTTP call when nobody is waiting for the result.
                if (item.Cancelled)
                {
                    McpLogger.Log("Skipping cancelled queue item for action=" + item.Action);
                    item.Done.Set();
                    continue;
                }

                bool timedOut = false;
                try
                {
                    item.Result = ExecuteHttpCall(item.BridgeUrl, item.Action, item.BodyJson);
                }
                catch (WebException wex)
                {
                    string detail = "";
                    if (wex.Response is HttpWebResponse resp)
                    {
                        using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                            detail = reader.ReadToEnd();
                    }

                    if (wex.Status == WebExceptionStatus.Timeout)
                    {
                        timedOut = true;
                        McpLogger.Log("Bridge HTTP TIMEOUT for action=" + item.Action);
                        item.Result = "{\"success\":false,\"message\":\"Bridge call timed out after "
                            + (HttpTimeoutMs / 1000) + "s for action: " + item.Action + "\"}";
                    }
                    else
                    {
                        item.Result = "{\"success\":false,\"message\":\"Bridge call failed: "
                            + JsonHelpers.EscapeJsonString(wex.Message + " " + detail) + "\"}";
                    }
                }
                catch (Exception ex)
                {
                    item.Result = "{\"success\":false,\"message\":\"Bridge unavailable: "
                        + JsonHelpers.EscapeJsonString(ex.Message) + "\"}";
                }
                finally
                {
                    item.Done.Set();
                }

                if (timedOut)
                {
                    McpLogger.Log("Bridge cooldown " + PostTimeoutCooldownMs + "ms after timeout");
                    Thread.Sleep(PostTimeoutCooldownMs);
                }
            }
        }

        private static string ExecuteHttpCall(string bridgeUrl, string action, string bodyJson)
        {
            using (var client = new TimeoutWebClient { TimeoutMs = HttpTimeoutMs })
            {
                client.Encoding = Encoding.UTF8;
                client.Headers[HttpRequestHeader.ContentType] = "application/json";
                string requestBody = InjectAction(bodyJson, action);
                return client.UploadString(bridgeUrl, requestBody);
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
