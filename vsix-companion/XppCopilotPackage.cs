using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
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

            RegisterMcpServer();
        }

        /// <summary>
        /// Ensures a .mcp.json file exists in the user's home directory so that
        /// VS discovers the embedded MCP server and surfaces the xpp_create_object
        /// tool in the Copilot tool picker.
        /// </summary>
        internal string RegisterMcpServer()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var report = new StringBuilder();
            try
            {
                // Locate the MCP server exe shipped alongside the VSIX DLL
                string extensionDir = Path.GetDirectoryName(
                    typeof(XppCopilotPackage).Assembly.Location);
                string mcpExePath = Path.Combine(extensionDir, "XppMcpServer.exe");

                report.AppendLine("ExtensionDir: " + extensionDir);
                report.AppendLine("McpExePath: " + mcpExePath);

                if (!File.Exists(mcpExePath))
                {
                    string msg = "MCP server exe not found: " + mcpExePath;
                    System.Diagnostics.Debug.WriteLine("[XppCopilot] " + msg);
                    report.AppendLine(msg);
                    return report.ToString();
                }

                string expectedJson = BuildMcpConfigJson(mcpExePath);

                foreach (string path in GetMcpConfigCandidatePaths())
                {
                    string result = UpsertMcpConfig(path, expectedJson, mcpExePath);
                    report.AppendLine(path + " => " + result);
                }

                bool probeOk = ProbeMcpServer(mcpExePath, out string probeDetails);
                report.AppendLine("MCP Probe => " + (probeOk ? "OK" : "FAILED"));
                report.AppendLine(probeDetails);
                if (!probeOk)
                {
                    report.AppendLine("Failover => Use X++ AI menu commands (Generate Code / Create Object) until MCP tools appear.");
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
                var dte = GetService(typeof(DTE)) as DTE;
                report.AppendLine("VS Version: " + (dte?.Version ?? "(unknown)"));

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

                if (File.Exists(mcpExePath))
                {
                    bool probeOk = ProbeMcpServer(mcpExePath, out string probeDetails);
                    report.AppendLine("MCP Probe => " + (probeOk ? "OK" : "FAILED"));
                    report.AppendLine(probeDetails);
                    if (!probeOk)
                        report.AppendLine("Failover => Use in-process X++ AI menu commands while troubleshooting MCP discovery.");
                }
            }
            catch (Exception ex)
            {
                report.AppendLine("Diagnostics failed: " + ex.Message);
            }

            return report.ToString();
        }

        private static bool ProbeMcpServer(string exePath, out string details)
        {
            var sb = new StringBuilder();
            details = string.Empty;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var proc = System.Diagnostics.Process.Start(psi))
                {
                    if (proc == null)
                    {
                        details = "Failed to start MCP server process.";
                        return false;
                    }

                    string init = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{},\"clientInfo\":{\"name\":\"xpp-copilot-companion\",\"version\":\"0.2.1\"}}}";
                    WriteFramedMessage(proc.StandardInput, init);
                    string initResp = ReadFramedMessage(proc.StandardOutput, 3000);
                    bool initOk = !string.IsNullOrWhiteSpace(initResp) && initResp.Contains("\"result\"");
                    sb.AppendLine("Initialize: " + (initOk ? "OK" : "FAILED"));

                    string list = "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}";
                    WriteFramedMessage(proc.StandardInput, list);
                    string listResp = ReadFramedMessage(proc.StandardOutput, 3000);
                    bool listOk = !string.IsNullOrWhiteSpace(listResp) && listResp.Contains("xpp_create_object");
                    sb.AppendLine("ToolsList: " + (listOk ? "OK" : "FAILED"));

                    try
                    {
                        string shutdown = "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"shutdown\",\"params\":{}}";
                        WriteFramedMessage(proc.StandardInput, shutdown);
                    }
                    catch
                    {
                    }

                    if (!proc.HasExited)
                    {
                        if (!proc.WaitForExit(500)) proc.Kill();
                    }

                    details = sb.ToString();
                    return initOk && listOk;
                }
            }
            catch (Exception ex)
            {
                details = "Probe exception: " + ex.Message;
                return false;
            }
        }

        private static void WriteFramedMessage(StreamWriter writer, string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            writer.Write("Content-Length: " + bytes.Length + "\r\n\r\n");
            writer.Write(json);
            writer.Flush();
        }

        private static string ReadFramedMessage(StreamReader reader, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            int contentLength = -1;

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                string line = reader.ReadLine();
                if (line == null) break;
                if (line.Length == 0) break;
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    string value = line.Substring("Content-Length:".Length).Trim();
                    int.TryParse(value, out contentLength);
                }
            }

            if (contentLength <= 0) return null;

            char[] buffer = new char[contentLength];
            int total = 0;
            while (total < contentLength && sw.ElapsedMilliseconds < timeoutMs)
            {
                int read = reader.Read(buffer, total, contentLength - total);
                if (read <= 0) break;
                total += read;
            }

            return total > 0 ? new string(buffer, 0, total) : null;
        }

        private IEnumerable<string> GetMcpConfigCandidatePaths()
        {
            var result = new List<string>();
            string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // Visual Studio and generic MCP discovery candidates
            result.Add(Path.Combine(userHome, ".mcp.json"));
            result.Add(Path.Combine(userHome, "mcp.json"));

            // Compatibility with VS Code-style conventions (some Copilot builds look here)
            string vscodeDir = Path.Combine(userHome, ".vscode");
            result.Add(Path.Combine(vscodeDir, "mcp.json"));
            result.Add(Path.Combine(vscodeDir, ".mcp.json"));

            // Solution-local discovery for VS scenarios that prioritize solution scope
            ThreadHelper.ThrowIfNotOnUIThread();
            var dte = GetService(typeof(DTE)) as DTE;
            string slnPath = dte?.Solution?.FullName;
            if (!string.IsNullOrWhiteSpace(slnPath))
            {
                string slnDir = Path.GetDirectoryName(slnPath);
                if (!string.IsNullOrWhiteSpace(slnDir))
                    result.Add(Path.Combine(slnDir, ".mcp.json"));
            }

            return result;
        }

        private static string UpsertMcpConfig(string configPath, string expectedJson, string mcpExePath)
        {
            try
            {
                string dir = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(configPath))
                {
                    string existing = File.ReadAllText(configPath);
                    if (existing.Contains("xpp-copilot-companion") && existing.Contains(mcpExePath.Replace("\\", "\\\\")))
                        return "already up-to-date";
                }

                File.WriteAllText(configPath, expectedJson);
                System.Diagnostics.Debug.WriteLine("[XppCopilot] MCP server registered: " + configPath);
                return "updated";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[XppCopilot] Failed to register MCP server at '" + configPath + "': " + ex.Message);
                return "failed: " + ex.Message;
            }
        }

        private static string BuildMcpConfigJson(string exePath)
        {
            string escapedPath = exePath.Replace("\\", "\\\\");
            return "{\n"
                + "  \"servers\": {\n"
                + "    \"xpp-copilot-companion\": {\n"
                + "      \"type\": \"stdio\",\n"
                + "      \"command\": \"" + escapedPath + "\",\n"
                + "      \"args\": []\n"
                + "    }\n"
                + "  },\n"
                + "  \"mcpServers\": {\n"
                + "    \"xpp-copilot-companion\": {\n"
                + "      \"type\": \"stdio\",\n"
                + "      \"command\": \"" + escapedPath + "\",\n"
                + "      \"args\": []\n"
                + "    }\n"
                + "  }\n"
                + "}\n";
        }
    }
}
