using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using XppAiCopilotCompanion.MetaModel;
using Task = System.Threading.Tasks.Task;

namespace XppAiCopilotCompanion
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuids.PackageGuidString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideOptionPage(typeof(XppCopilotOptionsPage), "X++ AI Copilot", "General", 0, 0, true)]
    [ProvideAutoLoad(Microsoft.VisualStudio.Shell.Interop.UIContextGuids80.SolutionExists,
        PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(Microsoft.VisualStudio.Shell.Interop.UIContextGuids80.NoSolution,
        PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class XppCopilotPackage : AsyncPackage
    {
        public static XppCopilotPackage Instance { get; private set; }
        private System.Diagnostics.Process _mcpServerProcess;
        private MetaModelBridgeServer _bridgeServer;
        private MetaModel.MetaModelBridge _metaModelBridge;

        /// <summary>
        /// The shared MetaModelBridge instance used for all metadata operations.
        /// Available after package initialization.
        /// </summary>
        internal MetaModel.IMetaModelBridge MetaModelBridge => _metaModelBridge;

    // Must match ServerVersion in mcp-server/Program.cs.
    // When the server exe is updated, bump this to force stale cached
    // processes to be killed and replaced with the new binary.
    private const string ExpectedMcpServerVersion = "0.5.0";

        protected override async Task InitializeAsync(
            CancellationToken cancellationToken,
            IProgress<ServiceProgressData> progress)
        {
            await base.InitializeAsync(cancellationToken, progress);
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            Instance = this;

            await RefreshContextCommand.InitializeAsync(this);
            await GenerateCodeCommand.InitializeAsync(this);
            await CreateObjectCommand.InitializeAsync(this);
            await RegisterMcpCommand.InitializeAsync(this);
            await McpDiagnosticsCommand.InitializeAsync(this);

            RegisterMcpServer(includeProbe: false);

            // Start the MetaModel bridge HTTP server so the MCP server can
            // delegate to real IMetaModelService APIs inside this VS process.
            StartMetaModelBridge();

            // Trigger a config refresh after VS startup settles so Copilot
            // re-reads MCP configuration in sessions where discovery happens early.
            _ = TouchMcpConfigsAfterDelayAsync();
        }

        private async Task TouchMcpConfigsAfterDelayAsync()
        {
            // Rewrite the MCP config files at several points after startup so that
            // VS Copilot's FileSystemWatcher receives a real content-Changed event
            // and (re-)connects to the server.  Using File.SetLastWriteTimeUtc alone
            // does not reliably trigger the watcher on all VS versions; writing the
            // actual content does.  We retry three times to handle slow machines
            // where the server may not be fully up on the first attempt.
            foreach (int waitMs in new[] { 20_000, 25_000, 45_000 })
            {
                await Task.Delay(waitMs);
                await JoinableTaskFactory.SwitchToMainThreadAsync();
                try
                {
                    // Resolve the actual running port from the port file.
                    string portFile = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "XppCopilotCompanion", "mcp-port.txt");
                    string mcpUrl = "http://127.0.0.1:21329/";
                    if (File.Exists(portFile))
                    {
                        string portText = File.ReadAllText(portFile).Trim();
                        int port;
                        if (int.TryParse(portText, out port) && port > 0)
                            mcpUrl = "http://127.0.0.1:" + port + "/";
                    }

                    string expectedJson = BuildMcpConfigJson(mcpUrl);
                    foreach (string path in GetMcpConfigCandidatePaths())
                    {
                        if (File.Exists(path))
                            File.WriteAllText(path, expectedJson);
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Ensures a .mcp.json file exists in the user's home directory so that
        /// VS discovers the embedded MCP server and surfaces the xpp_create_object
        /// tool in the Copilot tool picker.
        /// </summary>
        internal string RegisterMcpServer(bool includeProbe = true)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var report = new StringBuilder();
            try
            {
                string vsVersion = GetVisualStudioVersionText();
                report.AppendLine("VS Version: " + vsVersion);
                if (!IsMcpSupportedVsVersion(vsVersion))
                {
                    report.AppendLine("WARNING: This Visual Studio version is below 17.14.");
                    report.AppendLine("External MCP tools may not appear in Copilot tool picker on this version.");
                }

                // Locate the MCP server exe shipped alongside the VSIX DLL
                string extensionDir = Path.GetDirectoryName(
                    typeof(XppCopilotPackage).Assembly.Location);
                string shippedExePath = Path.Combine(extensionDir, "XppMcpServer.exe");

                report.AppendLine("ExtensionDir: " + extensionDir);
                report.AppendLine("ShippedExePath: " + shippedExePath);

                if (!File.Exists(shippedExePath))
                {
                    string msg = "MCP server exe not found: " + shippedExePath;
                    System.Diagnostics.Debug.WriteLine("[XppCopilot] " + msg);
                    report.AppendLine(msg);
                    return report.ToString();
                }

                string defaultMcpUrl = "http://127.0.0.1:21329/";
                string stableDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "XppCopilotCompanion");
                string stableMcpExePath = Path.Combine(stableDir, "XppMcpServer.exe");

                // If an MCP process is running from an unexpected location (for example,
                // a manually launched debug build), kill it so VS always uses the
                // extension-owned binary and tool catalog.
                bool hadUnexpectedProcess = KillUnexpectedMcpProcesses(
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        stableMcpExePath,
                        shippedExePath
                    },
                    report);

                // Fast alive-check: if a server is already running and has ALL expected tools, skip kill/copy/start.
                // This prevents the circular-kill problem when something triggers a second registration.
                bool serverAlreadyAlive = false;
                try
                {
                    string aliveDetails = hadUnexpectedProcess
                        ? "Skipped probe after killing unexpected MCP process."
                        : string.Empty;
                    bool aliveOk = !hadUnexpectedProcess && ProbeMcpHttp(defaultMcpUrl, out aliveDetails);
                    if (aliveOk)
                    {
                        serverAlreadyAlive = true;
                        report.AppendLine("Existing MCP server on " + defaultMcpUrl + " is alive and current — skipping restart.");
                        report.AppendLine(aliveDetails);
                    }
                    else
                    {
                        report.AppendLine("Existing server probe incomplete — will restart. " + aliveDetails);
                    }
                }
                catch { }

                string mcpUrl = defaultMcpUrl;

                if (!serverAlreadyAlive)
                {
                    // MUST kill old processes BEFORE copying — the old exe is file-locked while running
                    report.AppendLine("Killing stale MCP servers before copy...");
                    try
                    {
                        // Graceful HTTP shutdown first
                        try
                        {
                            using (var client = new System.Net.WebClient())
                            {
                                client.Encoding = Encoding.UTF8;
                                client.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json";
                                client.UploadString("http://127.0.0.1:21329/",
                                    "{\"jsonrpc\":\"2.0\",\"id\":99,\"method\":\"shutdown\",\"params\":{}}");
                            }
                            System.Threading.Thread.Sleep(500);
                        }
                        catch { }

                        // Force kill all by name
                        foreach (var p in System.Diagnostics.Process.GetProcessesByName("XppMcpServer"))
                        {
                            try
                            {
                                report.AppendLine("Pre-copy kill pid=" + p.Id);
                                p.Kill();
                                p.WaitForExit(2000);
                            }
                            catch { }
                            finally { p.Dispose(); }
                        }

                        // Clean stale port/pid files
                        string companionDir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "XppCopilotCompanion");
                        foreach (string f in new[] { "mcp-port.txt", "mcp-server.pid" })
                        {
                            string fp = Path.Combine(companionDir, f);
                            if (File.Exists(fp)) try { File.Delete(fp); } catch { }
                        }
                    }
                    catch (Exception killEx)
                    {
                        report.AppendLine("Pre-copy cleanup error: " + killEx.Message);
                    }

                    // NOW copy exe to stable path (old process is dead, file is unlocked)
                    Directory.CreateDirectory(stableDir);
                    string mcpExePath = Path.Combine(stableDir, "XppMcpServer.exe");
                    try
                    {
                        File.Copy(shippedExePath, mcpExePath, overwrite: true);
                        report.AppendLine("StableExePath: " + mcpExePath + " => copied OK");
                    }
                    catch (Exception copyEx)
                    {
                        report.AppendLine("StableExePath: " + mcpExePath + " => copy failed: " + copyEx.Message
                            + " — falling back to shipped exe");
                        mcpExePath = shippedExePath;
                    }

                    // Start MCP server as HTTP (persistent background process)
                    string startedUrl = StartMcpHttpServer(mcpExePath, out string startDetails);
                    report.AppendLine(startDetails);

                    if (string.IsNullOrWhiteSpace(startedUrl))
                    {
                        report.AppendLine("MCP HTTP server failed to start. Tools will not appear.");
                        return report.ToString();
                    }

                    mcpUrl = startedUrl;
                } // end if (!serverAlreadyAlive)

                report.AppendLine("McpUrl: " + mcpUrl);

                // Clean up old/corrupted configs from wrong locations
                foreach (string stale in GetStaleMcpConfigPaths())
                {
                    string cleanResult = CleanStaleMcpConfig(stale);
                    report.AppendLine(stale + " => " + cleanResult);
                }

                // Purge VS Copilot MCP cache so it re-discovers tools fresh.
                // Without this, switching solutions can show stale/missing tools.
                int cacheDeleted = CleanVsCopilotMcpCache(report);
                if (cacheDeleted > 0)
                    report.AppendLine("Purged " + cacheDeleted + " stale VS Copilot MCP cache file(s).");

                // Write .mcp.json pointing to HTTP URL
                string expectedJson = BuildMcpConfigJson(mcpUrl);

                foreach (string path in GetMcpConfigCandidatePaths())
                {
                    string result = UpsertMcpConfig(path, expectedJson, null);
                    report.AppendLine(path + " => " + result);
                }

                if (includeProbe)
                {
                    bool probeOk = ProbeMcpHttp(mcpUrl, out string probeDetails);
                    report.AppendLine("MCP Probe => " + (probeOk ? "OK" : "FAILED"));
                    report.AppendLine(probeDetails);
                    if (!probeOk)
                    {
                        report.AppendLine("Failover => Use X++ AI menu commands (Generate Code / Create Object) until MCP tools appear.");
                    }
                }
                else
                {
                    report.AppendLine("MCP Probe => skipped on startup (non-blocking initialization).");
                }

                report.AppendLine("MCP registration completed.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[XppCopilot] Failed to register MCP server: " + ex.Message);
                report.AppendLine("Failed to register MCP server: " + ex.Message);
            }

            return report.ToString();
        }

        internal string GetMcpDiagnosticsReport()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var report = new StringBuilder();
            report.AppendLine("=== MCP Diagnostics ===");

            try
            {
                string vsVersion = GetVisualStudioVersionText();
                report.AppendLine("VS Version: " + vsVersion);
                if (!IsMcpSupportedVsVersion(vsVersion))
                {
                    report.AppendLine("WARNING: VS version is below 17.14. External MCP tool surfacing may be unsupported.");
                }

                string extensionDir = Path.GetDirectoryName(typeof(XppCopilotPackage).Assembly.Location);
                string mcpExePath = Path.Combine(extensionDir, "XppMcpServer.exe");
                report.AppendLine("ExtensionDir: " + extensionDir);
                report.AppendLine("McpExePath: " + mcpExePath);
                report.AppendLine("McpExeExists: " + File.Exists(mcpExePath));

                report.AppendLine("Config candidates:");
                foreach (string path in GetMcpConfigCandidatePaths())
                {
                    bool exists = File.Exists(path);
                    report.AppendLine("- " + path + " => " + (exists ? "exists" : "missing"));
                }

                // Probe the running HTTP server (don't launch a new process!)
                string probeUrl = "http://127.0.0.1:21329/";
                string portFile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "XppCopilotCompanion", "mcp-port.txt");
                if (File.Exists(portFile))
                {
                    string portText = File.ReadAllText(portFile).Trim();
                    int port;
                    if (int.TryParse(portText, out port) && port > 0)
                        probeUrl = "http://127.0.0.1:" + port + "/";
                }

                {
                    bool probeOk = ProbeMcpHttp(probeUrl, out string probeDetails);
                    report.AppendLine("MCP HTTP Probe (" + probeUrl + ") => " + (probeOk ? "OK" : "FAILED"));
                    report.AppendLine(probeDetails);
                    if (!probeOk)
                        report.AppendLine("Failover => Use in-process X++ AI menu commands while troubleshooting MCP discovery.");
                }

                string mcpLogPath = Path.Combine(Path.GetTempPath(), "XppMcpServer.log");
                report.AppendLine("MCP Log: " + mcpLogPath + " => " + (File.Exists(mcpLogPath) ? "exists" : "missing"));
                if (File.Exists(mcpLogPath))
                {
                    report.AppendLine("MCP Log (tail):");
                    foreach (string line in ReadLastLines(mcpLogPath, 20))
                    {
                        report.AppendLine("  " + line);
                    }
                }
            }
            catch (Exception ex)
            {
                report.AppendLine("Diagnostics failed: " + ex.Message);
            }

            return report.ToString();
        }

        private static bool IsMcpSupportedVsVersion(string versionText)
        {
            if (string.IsNullOrWhiteSpace(versionText)) return false;

            // ProductVersion can include extra suffixes; parse the leading numeric token only.
            string numeric = ExtractLeadingVersion(versionText);
            if (string.IsNullOrWhiteSpace(numeric)) return false;

            Version parsed;
            if (!Version.TryParse(numeric, out parsed))
                return false;

            // MCP tool surfacing in VS Copilot is expected on 17.14+
            var min = new Version(17, 14);
            return parsed >= min;
        }

        private string GetVisualStudioVersionText()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                string product = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileVersionInfo?.ProductVersion;
                if (!string.IsNullOrWhiteSpace(product))
                    return product;
            }
            catch
            {
            }

            try
            {
                var dte = GetService(typeof(DTE)) as DTE;
                if (!string.IsNullOrWhiteSpace(dte?.Version))
                    return dte.Version;
            }
            catch
            {
            }

            return "0.0";
        }

        private static string ExtractLeadingVersion(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            int i = 0;
            while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.')) i++;
            string token = text.Substring(0, i).Trim('.');
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }

        private IEnumerable<string> GetMcpConfigCandidatePaths()
        {
            string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // Canonical location only.
            // Writing one file avoids duplicate-server collisions across scopes,
            // which can prevent tools from surfacing in the VS picker.
            return new[] { Path.Combine(userHome, ".mcp.json") };
        }

        /// <summary>
        /// Paths where previous versions incorrectly wrote MCP configs.
        /// These are not valid VS discovery locations and must be removed.
        /// </summary>
        private static IEnumerable<string> GetStaleMcpConfigPaths()
        {
            string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var result = new List<string>
            {
                // Legacy compatibility path.
                Path.Combine(userHome, "mcp.json"),

                // Wrong ecosystem-specific locations written by older builds.
                Path.Combine(userHome, ".vscode", "mcp.json"),
                Path.Combine(userHome, ".vscode", ".mcp.json"),
                Path.Combine(userHome, ".cursor", "mcp.json"),
            };

            try
            {
                // Also clean duplicates under the active solution to avoid
                // conflicting definitions of the same server id.
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = Instance?.GetService(typeof(DTE)) as DTE;
                string slnPath = dte?.Solution?.FullName;
                if (!string.IsNullOrWhiteSpace(slnPath))
                {
                    string slnDir = Path.GetDirectoryName(slnPath);
                    if (!string.IsNullOrWhiteSpace(slnDir))
                    {
                        string vsDir = Path.Combine(slnDir, ".vs");
                        result.Add(Path.Combine(vsDir, "mcp.json"));
                        result.Add(Path.Combine(vsDir, ".mcp.json"));
                        result.Add(Path.Combine(slnDir, ".mcp.json"));
                        result.Add(Path.Combine(slnDir, "mcp.json"));
                        result.Add(Path.Combine(slnDir, ".vscode", "mcp.json"));
                        result.Add(Path.Combine(slnDir, ".cursor", "mcp.json"));
                    }
                }
            }
            catch
            {
                // Best-effort cleanup list; ignore DTE unavailability.
            }

            return result;
        }

        private static string CleanStaleMcpConfig(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return "not present";

                string content = File.ReadAllText(path);
                if (content.Contains("xpp-copilot-companion"))
                {
                    File.Delete(path);
                    return "removed (was ours)";
                }

                return "skipped (not ours)";
            }
            catch (Exception ex)
            {
                return "cleanup failed: " + ex.Message;
            }
        }

        /// <summary>
        /// Deletes VS Copilot MCP cache files that contain our server name.
        /// Cache lives at %LOCALAPPDATA%\Microsoft\VisualStudio\Copilot\McpServers\*.cache.
        /// VS recreates these on the next tools/list round-trip, so deleting is safe
        /// and forces VS to re-discover the current tool set from the live server.
        /// </summary>
        private static int CleanVsCopilotMcpCache(StringBuilder report)
        {
            int deleted = 0;
            try
            {
                string cacheDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "VisualStudio", "Copilot", "McpServers");

                if (!Directory.Exists(cacheDir))
                    return 0;

                foreach (string file in Directory.GetFiles(cacheDir, "*.cache"))
                {
                    try
                    {
                        // Binary MessagePack files — search raw bytes for our server id.
                        byte[] raw = File.ReadAllBytes(file);
                        string text = System.Text.Encoding.UTF8.GetString(raw);
                        if (text.IndexOf("xpp", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            File.Delete(file);
                            report.AppendLine("Cache purged: " + Path.GetFileName(file));
                            deleted++;
                        }
                    }
                    catch (Exception ex)
                    {
                        report.AppendLine("Cache cleanup failed (" + Path.GetFileName(file) + "): " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                report.AppendLine("VS Copilot cache cleanup error: " + ex.Message);
            }
            return deleted;
        }

        private static string UpsertMcpConfig(string configPath, string expectedJson, object _unused)
        {
            try
            {
                string dir = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                // Always overwrite to fix stale/corrupted configs.
                File.WriteAllText(configPath, expectedJson);
                System.Diagnostics.Debug.WriteLine("[XppCopilot] MCP server registered: " + configPath);
                return "written";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[XppCopilot] Failed to register MCP server at '" + configPath + "': " + ex.Message);
                return "failed: " + ex.Message;
            }
        }

        private static string BuildMcpConfigJson(string mcpUrl)
        {
            // Official VS .mcp.json schema — HTTP transport uses "url" field.
            // https://learn.microsoft.com/en-us/visualstudio/ide/mcp-servers
            return "{\n"
                + "  \"servers\": {\n"
                + "    \"xpp-copilot-companion\": {\n"
                + "      \"type\": \"http\",\n"
                + "      \"url\": \"" + mcpUrl + "\"\n"
                + "    }\n"
                + "  }\n"
                + "}\n";
        }

        private static bool KillUnexpectedMcpProcesses(HashSet<string> expectedExePaths, StringBuilder report)
        {
            bool killedAny = false;
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcessesByName("XppMcpServer"))
                {
                    try
                    {
                        string path = null;
                        try { path = p.MainModule?.FileName; } catch { }

                        bool expected = !string.IsNullOrWhiteSpace(path) && expectedExePaths.Contains(path);
                        if (!expected)
                        {
                            report.AppendLine("Killing unexpected MCP process pid=" + p.Id + " path=" + (path ?? "<unknown>"));
                            p.Kill();
                            p.WaitForExit(2000);
                            killedAny = true;
                        }
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }

            return killedAny;
        }

        private string StartMcpHttpServer(string exePath, out string details)
        {
            var sb = new StringBuilder();
            try
            {
                // Old processes were already killed in RegisterMcpServer before the exe copy.
                // Just start the new server.

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                };

                var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null)
                {
                    details = sb.ToString() + "Failed to start MCP HTTP server process.";
                    return null;
                }

                _mcpServerProcess = proc;
                sb.AppendLine("Started MCP HTTP server pid=" + proc.Id);

                // Write PID file so we can clean up orphans even after a VS crash
                try
                {
                    string pidFile = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "XppCopilotCompanion", "mcp-server.pid");
                    File.WriteAllText(pidFile, proc.Id.ToString());
                }
                catch { }

                // Wait for port file to appear (server writes it on startup)
                string portFile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "XppCopilotCompanion", "mcp-port.txt");

                int port = 0;
                for (int i = 0; i < 30; i++) // up to 3 seconds
                {
                    System.Threading.Thread.Sleep(100);
                    if (File.Exists(portFile))
                    {
                        string portText = File.ReadAllText(portFile).Trim();
                        if (int.TryParse(portText, out port) && port > 0)
                            break;
                    }
                }

                if (port <= 0)
                {
                    details = sb.ToString() + "Port file not found after 3s.";
                    return null;
                }

                string url = "http://127.0.0.1:" + port + "/";
                sb.AppendLine("MCP HTTP URL: " + url);
                details = sb.ToString();
                return url;
            }
            catch (Exception ex)
            {
                details = sb.ToString() + "Exception: " + ex.Message;
                return null;
            }
        }

        private static bool ProbeMcpHttp(string url, out string details)
        {
            var sb = new StringBuilder();
            try
            {
                using (var client = new System.Net.WebClient())
                {
                    client.Encoding = Encoding.UTF8;
                    client.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json";

                    // ── Step 1: version check (GET /) ────────────────────────────
                    // The server responds to GET with {"name":"...","version":"X.Y.Z"}.
                    // If the version doesn't match what was shipped with this VSIX,
                    // the cached process is stale and must be restarted.
                    try
                    {
                        string infoResp = client.DownloadString(url);
                        bool versionOk = infoResp.Contains(ExpectedMcpServerVersion);
                        sb.AppendLine("Version: " + (versionOk
                            ? "OK (" + ExpectedMcpServerVersion + ")"
                            : "MISMATCH (expected " + ExpectedMcpServerVersion + ") — server will be restarted"));
                        if (!versionOk)
                        {
                            details = sb.ToString();
                            return false;
                        }
                    }
                    catch (Exception vEx)
                    {
                        sb.AppendLine("Version GET failed: " + vEx.Message + " — will restart server");
                        details = sb.ToString();
                        return false;
                    }

                    // ── Step 2: initialize ───────────────────────────────────────
                    string initJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{},\"clientInfo\":{\"name\":\"xpp-copilot-probe\",\"version\":\"0.3.0\"}}}";
                    string initResp = client.UploadString(url, initJson);
                    bool initOk = !string.IsNullOrWhiteSpace(initResp) && initResp.Contains("\"result\"");
                    sb.AppendLine("Initialize: " + (initOk ? "OK" : "FAILED"));

                    // ── Step 3: tools/list — all expected tools present? ─────────
                    // Check for tools that were absent in older (≤5-tool) server builds.
                    // If any are missing the binary is outdated and must be replaced.
                    string listJson = "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}";
                    string listResp = client.UploadString(url, listJson);
                    bool listOk = !string.IsNullOrWhiteSpace(listResp) && listResp.Contains("xpp_create_object");
                    bool hasAllTools = listOk
                        && listResp.Contains("xpp_search_docs")
                        && listResp.Contains("xpp_list_models")
                        && listResp.Contains("xpp_add_to_project")
                        && listResp.Contains("xpp_get_environment");
                    sb.AppendLine("ToolsList: " + (listOk ? "OK" : "FAILED")
                        + (listOk && !hasAllTools ? " (missing tools — outdated server, will restart)" : ""));

                    details = sb.ToString();
                    return initOk && hasAllTools;
                }
            }
            catch (Exception ex)
            {
                details = sb.ToString() + "Probe exception: " + ex.Message;
                return false;
            }
        }

        private static IEnumerable<string> ReadLastLines(string path, int maxLines)
        {
            try
            {
                var queue = new Queue<string>(maxLines);
                foreach (string line in File.ReadLines(path))
                {
                    if (queue.Count == maxLines)
                        queue.Dequeue();
                    queue.Enqueue(line);
                }
                return queue;
            }
            catch
            {
                return new[] { "<unable to read log file>" };
            }
        }

        private void StartMetaModelBridge()
        {
            try
            {
                _metaModelBridge = new MetaModel.MetaModelBridge();
                _bridgeServer = new MetaModelBridgeServer(_metaModelBridge);
                _bridgeServer.Start();
                System.Diagnostics.Debug.WriteLine("[XppCopilot] MetaModel bridge started.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[XppCopilot] MetaModel bridge failed: " + ex.Message);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Shut down the MetaModel bridge server.
                if (_bridgeServer != null)
                {
                    try { _bridgeServer.Dispose(); } catch { }
                    _bridgeServer = null;
                }

                // Do NOT kill the MCP server or delete the companion folder.
                // The server must persist between VS sessions so that VS reads
                // .mcp.json on next startup and connects to the already-running
                // server BEFORE the extension loads — solving the discovery timing issue.
                if (_mcpServerProcess != null)
                {
                    try { _mcpServerProcess.Dispose(); } catch { }
                    _mcpServerProcess = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
