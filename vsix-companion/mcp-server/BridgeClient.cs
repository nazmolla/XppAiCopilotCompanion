using System;
using System.IO;
using System.Net;
using System.Text;

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

        public BridgeClient(string bridgeUrl = null)
        {
            _bridgeUrl = bridgeUrl ?? DefaultBridgeUrl;
        }

        /// <summary>
        /// Checks if the MetaModel bridge is alive.
        /// </summary>
        public bool IsAvailable()
        {
            try
            {
                using (var client = new WebClient())
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
        /// </summary>
        public string Call(string action, string bodyJson)
        {
            try
            {
                using (var client = new WebClient())
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
                return "{\"success\":false,\"message\":\"Bridge call failed: "
                    + JsonHelpers.EscapeJsonString(wex.Message + " " + detail) + "\"}";
            }
            catch (Exception ex)
            {
                return "{\"success\":false,\"message\":\"Bridge unavailable: "
                    + JsonHelpers.EscapeJsonString(ex.Message) + "\"}";
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
