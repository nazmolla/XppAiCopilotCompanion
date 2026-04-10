using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

namespace XppAiCopilotCompanion.MetaModel
{
    /// <summary>
    /// Lightweight HTTP server running inside the VSIX process.
    /// Listens on port 21330 and routes JSON requests to <see cref="IMetaModelBridge"/>.
    /// The MCP server (standalone exe on port 21329) delegates to this bridge
    /// for all MetaModel operations.
    /// </summary>
    public sealed class MetaModelBridgeServer : IDisposable
    {
        private const int BridgePort = 21330;
        private readonly IMetaModelBridge _bridge;
        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private bool _running;

        public MetaModelBridgeServer(IMetaModelBridge bridge)
        {
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        }

        public bool IsRunning => _running;
        public string Url => "http://127.0.0.1:" + BridgePort + "/";

        public void Start()
        {
            if (_running) return;

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add(Url);
                _listener.Start();
                _cts = new CancellationTokenSource();
                _running = true;

                System.Threading.Tasks.Task.Run(() => AcceptLoop(_cts.Token));
                System.Diagnostics.Debug.WriteLine("[XppCopilot] MetaModel bridge started on " + Url);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[XppCopilot] Bridge start failed: " + ex.Message);
            }
        }

        private async System.Threading.Tasks.Task AcceptLoop(CancellationToken ct)
        {
            while (_running && !ct.IsCancellationRequested)
            {
                try
                {
                    var ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                    _ = System.Threading.Tasks.Task.Run(() => HandleRequest(ctx));
                }
                catch (ObjectDisposedException) { break; }
                catch (HttpListenerException) { break; }
                catch { }
            }
        }

        private void HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                if (ctx.Request.HttpMethod == "GET")
                {
                    SendJson(ctx, 200, "{\"bridge\":\"metamodel\",\"status\":\"ok\"}");
                    return;
                }

                if (ctx.Request.HttpMethod != "POST")
                {
                    ctx.Response.StatusCode = 405;
                    ctx.Response.Close();
                    return;
                }

                string body;
                using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    body = reader.ReadToEnd();

                string action = ExtractJsonString(body, "action");
                string responseJson = DispatchAction(action, body);
                SendJson(ctx, 200, responseJson);
            }
            catch (Exception ex)
            {
                try
                {
                    SendJson(ctx, 500, "{\"success\":false,\"message\":\"" + EscapeJson(ex.Message) + "\"}");
                }
                catch { ctx.Response.StatusCode = 500; ctx.Response.Close(); }
            }
        }

        private string DispatchAction(string action, string body)
        {
            // All bridge calls must be marshalled to the VS main thread
            // because IMetaModelService is thread-affine.
            string result = null;

            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                result = DispatchOnMainThread(action, body);
            });

            return result;
        }

        private string DispatchOnMainThread(string action, string body)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            switch (action)
            {
                case "create_object":
                    return HandleCreateObject(body);
                case "read_object":
                    return HandleReadObject(body);
                case "read_object_by_path":
                    return HandleReadObjectByPath(body);
                case "update_object":
                    return HandleUpdateObject(body);
                case "delete_object":
                    return HandleDeleteObject(body);
                case "find_object":
                    return HandleFindObject(body);
                case "list_objects":
                    return HandleListObjects(body);
                case "get_model_info":
                    return HandleGetModelInfo(body);
                case "list_models":
                    return HandleListModels(body);
                case "read_label":
                    return HandleReadLabel(body);
                case "create_label":
                    return HandleCreateLabel(body);
                case "add_to_project":
                    return HandleAddToProject(body);
                case "list_project_items":
                    return HandleListProjectItems(body);
                case "get_environment":
                    return HandleGetEnvironment(body);
                default:
                    return "{\"success\":false,\"message\":\"Unknown action: " + EscapeJson(action ?? "") + "\"}";
            }
        }

        // ── Action handlers ──

        private string HandleCreateObject(string body)
        {
            var req = new CreateObjectRequest
            {
                ObjectType = ExtractJsonString(body, "objectType"),
                ObjectName = ExtractJsonString(body, "objectName"),
                Declaration = ExtractJsonString(body, "declaration"),
                Methods = ExtractJsonStringArray(body, "methods"),
                MetadataXml = ExtractJsonString(body, "metadataXml"),
                ModelName = ExtractJsonString(body, "modelName")
            };
            var result = _bridge.CreateObject(req);
            return SerializeResult(result);
        }

        private string HandleReadObject(string body)
        {
            string objectType = ExtractJsonString(body, "objectType");
            string objectName = ExtractJsonString(body, "objectName");
            var result = _bridge.ReadObject(objectType, objectName);
            return SerializeReadResult(result);
        }

        private string HandleReadObjectByPath(string body)
        {
            string filePath = ExtractJsonString(body, "filePath");
            var result = _bridge.ReadObjectByPath(filePath);
            return SerializeReadResult(result);
        }

        private string HandleUpdateObject(string body)
        {
            var req = new UpdateObjectRequest
            {
                ObjectType = ExtractJsonString(body, "objectType"),
                ObjectName = ExtractJsonString(body, "objectName"),
                Declaration = ExtractJsonString(body, "declaration"),
                Methods = ExtractJsonStringArray(body, "methods"),
                RemoveMethodNames = ExtractJsonStringArray(body, "removeMethodNames"),
                MetadataXml = ExtractJsonString(body, "metadataXml")
            };
            var result = _bridge.UpdateObject(req);
            return SerializeResult(result);
        }

        private string HandleDeleteObject(string body)
        {
            string objectType = ExtractJsonString(body, "objectType");
            string objectName = ExtractJsonString(body, "objectName");
            string modelName = ExtractJsonString(body, "modelName");
            var result = _bridge.DeleteObject(objectType, objectName, modelName);
            return SerializeResult(result);
        }

        private string HandleFindObject(string body)
        {
            string objectName = ExtractJsonString(body, "objectName");
            string objectType = ExtractJsonString(body, "objectType");
            string exactStr = ExtractJsonString(body, "exactMatch");
            bool exact = "true".Equals(exactStr, StringComparison.OrdinalIgnoreCase);
            var result = _bridge.FindObject(objectName, objectType, exact);

            var sb = new StringBuilder();
            sb.Append("{\"success\":" + (result.Success ? "true" : "false"));
            if (result.Message != null)
                sb.Append(",\"message\":\"" + EscapeJson(result.Message) + "\"");
            sb.Append(",\"matches\":[");
            for (int i = 0; i < result.Matches.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var m = result.Matches[i];
                sb.Append("{\"objectType\":\"" + EscapeJson(m.ObjectType) + "\"");
                sb.Append(",\"objectName\":\"" + EscapeJson(m.ObjectName) + "\"");
                sb.Append(",\"modelName\":\"" + EscapeJson(m.ModelName ?? "") + "\"");
                sb.Append(",\"isCustom\":" + (m.IsCustom ? "true" : "false") + "}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private string HandleListObjects(string body)
        {
            string modelName = ExtractJsonString(body, "modelName");
            string objectType = ExtractJsonString(body, "objectType");
            string nameFilter = ExtractJsonString(body, "nameFilter");
            string maxStr = ExtractJsonString(body, "maxResults");
            int maxResults = 100;
            if (!string.IsNullOrEmpty(maxStr)) int.TryParse(maxStr, out maxResults);

            // Require nameFilter to prevent full metadata scans that time out
            if (string.IsNullOrWhiteSpace(nameFilter))
                return "{\"success\":false,\"message\":\"nameFilter is required. Provide a substring to search for (e.g. 'CustTable').\",\"objects\":[]}";

            var result = _bridge.ListObjects(modelName, objectType, nameFilter, maxResults);

            var sb = new StringBuilder();
            sb.Append("{\"success\":" + (result.Success ? "true" : "false"));
            if (result.Message != null)
                sb.Append(",\"message\":\"" + EscapeJson(result.Message) + "\"");
            sb.Append(",\"objects\":[");
            for (int i = 0; i < result.Objects.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var o = result.Objects[i];
                sb.Append("{\"objectType\":\"" + EscapeJson(o.ObjectType) + "\"");
                sb.Append(",\"objectName\":\"" + EscapeJson(o.ObjectName) + "\"");
                sb.Append(",\"modelName\":\"" + EscapeJson(o.ModelName ?? "") + "\"");
                sb.Append(",\"isCustom\":" + (o.IsCustom ? "true" : "false") + "}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private string HandleGetModelInfo(string body)
        {
            string modelName = ExtractJsonString(body, "modelName");
            var result = _bridge.GetModelInfo(modelName);
            var sb = new StringBuilder();
            sb.Append("{\"success\":" + (result.Success ? "true" : "false"));
            if (result.Message != null) sb.Append(",\"message\":\"" + EscapeJson(result.Message) + "\"");
            if (result.Name != null) sb.Append(",\"name\":\"" + EscapeJson(result.Name) + "\"");
            if (result.DisplayName != null) sb.Append(",\"displayName\":\"" + EscapeJson(result.DisplayName) + "\"");
            if (result.Publisher != null) sb.Append(",\"publisher\":\"" + EscapeJson(result.Publisher) + "\"");
            if (result.Version != null) sb.Append(",\"version\":\"" + EscapeJson(result.Version) + "\"");
            if (result.Layer != null) sb.Append(",\"layer\":\"" + EscapeJson(result.Layer) + "\"");
            sb.Append(",\"modelId\":" + result.ModelId);
            sb.Append("}");
            return sb.ToString();
        }

        private string HandleListModels(string body)
        {
            var models = _bridge.ListModels();
            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"models\":[");
            for (int i = 0; i < models.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var m = models[i];
                sb.Append("{\"name\":\"" + EscapeJson(m.Name) + "\"");
                sb.Append(",\"displayName\":\"" + EscapeJson(m.DisplayName ?? "") + "\"");
                sb.Append(",\"isCustom\":" + (m.IsCustom ? "true" : "false") + "}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private string HandleReadLabel(string body)
        {
            string labelFileId = ExtractJsonString(body, "labelFileId");
            string language = ExtractJsonString(body, "language");
            string labelId = ExtractJsonString(body, "labelId");
            string searchText = ExtractJsonString(body, "searchText");
            string maxStr = ExtractJsonString(body, "maxResults");
            int maxResults = 50;
            if (!string.IsNullOrEmpty(maxStr)) int.TryParse(maxStr, out maxResults);
            var result = _bridge.ReadLabel(labelFileId, language, labelId, searchText, maxResults);

            var sb = new StringBuilder();
            sb.Append("{\"success\":" + (result.Success ? "true" : "false"));
            if (result.Message != null) sb.Append(",\"message\":\"" + EscapeJson(result.Message) + "\"");
            sb.Append(",\"labels\":[");
            for (int i = 0; i < result.Labels.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var l = result.Labels[i];
                sb.Append("{\"id\":\"" + EscapeJson(l.Id) + "\"");
                sb.Append(",\"text\":\"" + EscapeJson(l.Text) + "\"}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private string HandleCreateLabel(string body)
        {
            string labelFileId = ExtractJsonString(body, "labelFileId");
            string language = ExtractJsonString(body, "language");
            string labelId = ExtractJsonString(body, "labelId");
            string text = ExtractJsonString(body, "text");
            string comment = ExtractJsonString(body, "comment");
            var result = _bridge.CreateLabel(labelFileId, language, labelId, text, comment);
            return SerializeResult(result);
        }

        private string HandleAddToProject(string body)
        {
            string objectType = ExtractJsonString(body, "objectType");
            string objectName = ExtractJsonString(body, "objectName");
            var result = _bridge.AddToProject(objectType, objectName);
            return SerializeResult(result);
        }

        private string HandleListProjectItems(string body)
        {
            var items = _bridge.ListProjectItems();
            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"items\":[");
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("{\"name\":\"" + EscapeJson(items[i].Name) + "\"");
                if (items[i].FilePath != null)
                    sb.Append(",\"filePath\":\"" + EscapeJson(items[i].FilePath) + "\"");
                sb.Append("}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private string HandleGetEnvironment(string body)
        {
            var info = _bridge.GetEnvironmentInfo();
            var sb = new StringBuilder();
            sb.Append("{\"success\":true");
            sb.Append(",\"customMetadataFolder\":\"" + EscapeJson(info.CustomMetadataFolder ?? "") + "\"");
            sb.Append(",\"referenceMetadataFolders\":[");
            for (int i = 0; i < info.ReferenceMetadataFolders.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("\"" + EscapeJson(info.ReferenceMetadataFolders[i]) + "\"");
            }
            sb.Append("]");
            sb.Append(",\"activeProjectName\":\"" + EscapeJson(info.ActiveProjectName ?? "") + "\"");
            sb.Append(",\"activeModelName\":\"" + EscapeJson(info.ActiveModelName ?? "") + "\"");
            sb.Append(",\"activeModelId\":" + info.ActiveModelId);
            sb.Append(",\"activeModelLayer\":\"" + EscapeJson(info.ActiveModelLayer ?? "") + "\"");
            sb.Append("}");
            return sb.ToString();
        }

        // ── Serialization helpers ──

        private string SerializeResult(MetaModelResult r)
        {
            var sb = new StringBuilder();
            sb.Append("{\"success\":" + (r.Success ? "true" : "false"));
            sb.Append(",\"message\":\"" + EscapeJson(r.Message ?? "") + "\"");
            if (r.FilePath != null)
                sb.Append(",\"filePath\":\"" + EscapeJson(r.FilePath) + "\"");
            sb.Append("}");
            return sb.ToString();
        }

        private string SerializeReadResult(ReadObjectResult r)
        {
            var sb = new StringBuilder();
            sb.Append("{\"success\":" + (r.Success ? "true" : "false"));
            if (r.Message != null) sb.Append(",\"message\":\"" + EscapeJson(r.Message) + "\"");
            if (r.ObjectType != null) sb.Append(",\"objectType\":\"" + EscapeJson(r.ObjectType) + "\"");
            if (r.ObjectName != null) sb.Append(",\"objectName\":\"" + EscapeJson(r.ObjectName) + "\"");
            if (r.Declaration != null) sb.Append(",\"declaration\":\"" + EscapeJson(r.Declaration) + "\"");
            if (r.MetadataXml != null) sb.Append(",\"metadataXml\":\"" + EscapeJson(r.MetadataXml) + "\"");
            if (r.ModelName != null) sb.Append(",\"modelName\":\"" + EscapeJson(r.ModelName) + "\"");
            sb.Append(",\"isCustom\":" + (r.IsCustom ? "true" : "false"));
            sb.Append(",\"methods\":[");
            for (int i = 0; i < r.Methods.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("{\"name\":\"" + EscapeJson(r.Methods[i].Name) + "\"");
                sb.Append(",\"source\":\"" + EscapeJson(r.Methods[i].Source) + "\"}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        // ── Minimal JSON helpers (no external deps) ──

        private static string ExtractJsonString(string json, string key)
        {
            if (json == null) return null;
            string pattern = "\"" + key + "\"";
            int idx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) return null;
            int colon = json.IndexOf(':', idx + pattern.Length);
            if (colon < 0) return null;
            int start = colon + 1;
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
            if (start >= json.Length || json[start] != '"') return null;

            var sb = new StringBuilder();
            for (int i = start + 1; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    char next = json[i + 1];
                    switch (next)
                    {
                        case '"': sb.Append('"'); i++; continue;
                        case '\\': sb.Append('\\'); i++; continue;
                        case 'n': sb.Append('\n'); i++; continue;
                        case 'r': sb.Append('\r'); i++; continue;
                        case 't': sb.Append('\t'); i++; continue;
                        case 'u':
                            if (i + 5 < json.Length)
                            {
                                string hex = json.Substring(i + 2, 4);
                                int cp;
                                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                                    System.Globalization.CultureInfo.InvariantCulture, out cp))
                                {
                                    sb.Append((char)cp);
                                    i += 5;
                                    continue;
                                }
                            }
                            sb.Append('u'); i++; continue;
                        default: sb.Append(next); i++; continue;
                    }
                }
                if (c == '"') break;
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static string[] ExtractJsonStringArray(string json, string key)
        {
            if (json == null) return null;
            string pattern = "\"" + key + "\"";
            int idx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) return null;
            int colon = json.IndexOf(':', idx + pattern.Length);
            if (colon < 0) return null;
            int bracketStart = json.IndexOf('[', colon);
            if (bracketStart < 0) return null;

            int depth = 0;
            int bracketEnd = -1;
            bool inStr = false;
            bool esc = false;
            for (int i = bracketStart; i < json.Length; i++)
            {
                char c = json[i];
                if (esc) { esc = false; continue; }
                if (c == '\\') { esc = true; continue; }
                if (c == '"') { inStr = !inStr; continue; }
                if (inStr) continue;
                if (c == '[') depth++;
                if (c == ']') { depth--; if (depth == 0) { bracketEnd = i; break; } }
            }
            if (bracketEnd < 0) return null;

            string content = json.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
            var result = new System.Collections.Generic.List<string>();
            int pos = 0;
            while (pos < content.Length)
            {
                int qs = content.IndexOf('"', pos);
                if (qs < 0) break;
                var val = new StringBuilder();
                int j = qs + 1;
                while (j < content.Length)
                {
                    char c = content[j];
                    if (c == '\\' && j + 1 < content.Length)
                    {
                        char next = content[j + 1];
                        switch (next)
                        {
                            case '"': val.Append('"'); break;
                            case '\\': val.Append('\\'); break;
                            case 'n': val.Append('\n'); break;
                            case 'r': val.Append('\r'); break;
                            case 't': val.Append('\t'); break;
                            default: val.Append(next); break;
                        }
                        j += 2; continue;
                    }
                    if (c == '"') break;
                    val.Append(c); j++;
                }
                result.Add(val.ToString());
                pos = j + 1;
            }
            return result.Count > 0 ? result.ToArray() : null;
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        private static void SendJson(HttpListenerContext ctx, int status, string json)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = body.Length;
            ctx.Response.OutputStream.Write(body, 0, body.Length);
            ctx.Response.OutputStream.Close();
        }

        public void Dispose()
        {
            _running = false;
            _cts?.Cancel();
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
        }
    }
}
