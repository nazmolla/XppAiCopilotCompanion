using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;
using System.Xml;

namespace XppAiCopilotCompanion.McpServer
{
    /// <summary>
    /// Minimal MCP (Model Context Protocol) server over stdio.
    /// Exposes the xpp_create_object tool so GitHub Copilot in VS can call it.
    /// Protocol: JSON-RPC 2.0 over stdin/stdout with Content-Length headers.
    /// </summary>
    internal static class Program
    {
        private const string ServerName = "xpp-copilot-companion";
        private const string ServerVersion = "0.2.0";

        static void Main()
        {
            // MCP uses stdout for responses — redirect stderr for diagnostics
            Console.Error.WriteLine($"[{ServerName}] MCP server starting...");

            while (true)
            {
                string message = ReadMessage();
                if (message == null) break; // stdin closed

                string response = HandleMessage(message);
                if (response != null)
                    WriteMessage(response);
            }

            Console.Error.WriteLine($"[{ServerName}] MCP server exiting.");
        }

        // ── Message framing (Content-Length header + JSON body) ──

        static string ReadMessage()
        {
            // Read headers until blank line
            int contentLength = -1;
            while (true)
            {
                string headerLine = Console.ReadLine();
                if (headerLine == null) return null; // EOF
                if (headerLine.Length == 0) break;   // blank line = end of headers

                if (headerLine.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    string val = headerLine.Substring("Content-Length:".Length).Trim();
                    int.TryParse(val, out contentLength);
                }
            }

            if (contentLength <= 0) return null;

            // Read exactly contentLength bytes
            char[] buffer = new char[contentLength];
            int totalRead = 0;
            while (totalRead < contentLength)
            {
                int read = Console.In.Read(buffer, totalRead, contentLength - totalRead);
                if (read == 0) return null;
                totalRead += read;
            }

            return new string(buffer, 0, totalRead);
        }

        static void WriteMessage(string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            string header = "Content-Length: " + bytes.Length + "\r\n\r\n";
            Console.Out.Write(header);
            Console.Out.Write(json);
            Console.Out.Flush();
        }

        // ── JSON-RPC dispatch ──

        static string HandleMessage(string json)
        {
            string id = ExtractJsonString(json, "id");
            string method = ExtractJsonString(json, "method");

            Console.Error.WriteLine($"[{ServerName}] method={method} id={id}");

            switch (method)
            {
                case "initialize":
                    return BuildInitializeResponse(id);

                case "initialized":
                    // Notification — no response
                    return null;

                case "tools/list":
                    return BuildToolsListResponse(id);

                case "tools/call":
                    return HandleToolCall(id, json);

                case "shutdown":
                    return BuildResult(id, "{}");

                default:
                    return BuildError(id, -32601, "Method not found: " + method);
            }
        }

        // ── MCP Handlers ──

        static string BuildInitializeResponse(string id)
        {
            string result = @"{
  ""protocolVersion"": ""2024-11-05"",
  ""capabilities"": { ""tools"": {} },
  ""serverInfo"": {
    ""name"": """ + ServerName + @""",
    ""version"": """ + ServerVersion + @"""
  }
}";
            return BuildResult(id, result);
        }

        static string BuildToolsListResponse(string id)
        {
            string result = @"{
  ""tools"": [
    {
      ""name"": ""xpp_create_object"",
      ""description"": ""Creates a new D365FO X++ metadata object (class, table, EDT, enum, form, menu item, query, etc.) in the correct model folder structure. Call this for EVERY new object you need to create."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""objectType"": {
            ""type"": ""string"",
            ""description"": ""The D365FO object type to create."",
            ""enum"": [""AxClass"", ""AxTable"", ""AxTableExtension"", ""AxView"", ""AxDataEntityView"", ""AxCompositeDataEntityView"", ""AxMap"", ""AxEdt"", ""AxEdtExtension"", ""AxEnum"", ""AxEnumExtension"", ""AxForm"", ""AxFormExtension"", ""AxTile"", ""AxMenu"", ""AxMenuExtension"", ""AxMenuItemDisplay"", ""AxMenuItemOutput"", ""AxMenuItemAction"", ""AxQuery"", ""AxQuerySimpleExtension"", ""AxSecurityPrivilege"", ""AxSecurityDuty"", ""AxSecurityRole"", ""AxSecurityPolicy"", ""AxService"", ""AxServiceGroup"", ""AxWorkflowCategory"", ""AxWorkflowType"", ""AxWorkflowApproval"", ""AxWorkflowTask"", ""AxWorkflowAutomatedTask"", ""AxSsrsReport"", ""AxAggregateMeasurement"", ""AxAggregateDimension"", ""AxKpi"", ""AxConfigurationKey"", ""AxConfigurationKeyGroup"", ""AxLicenseCode"", ""AxNumberSequenceModule"", ""AxResource""]
          },
          ""objectName"": {
            ""type"": ""string"",
            ""description"": ""The name of the object to create. Must follow the model's naming prefix convention.""
          },
          ""suggestedCode"": {
            ""type"": ""string"",
            ""description"": ""The X++ class/table DECLARATION code block.""
          },
          ""methods"": {
            ""type"": ""array"",
            ""items"": { ""type"": ""string"" },
            ""description"": ""Array of individual X++ method source strings. Each string is a complete method with full implementation.""
          },
          ""metadataXml"": {
            ""type"": ""string"",
            ""description"": ""Raw XML fragment for structural metadata (fields, indexes, relations, field groups, form designs, data sources, enum values, etc.). Inserted after SourceCode.""
          },
          ""baseObjectName"": {
            ""type"": ""string"",
            ""description"": ""For extensions: the name of the base object being extended.""
          },
          ""targetDirectory"": {
            ""type"": ""string"",
            ""description"": ""The model directory where the object should be created.""
          }
        },
        ""required"": [""objectType"", ""objectName"", ""targetDirectory""]
      }
    },
    {
      ""name"": ""xpp_read_object"",
      ""description"": ""Reads and parses a D365FO X++ metadata XML file. Returns the object type, name, declaration, methods (name + source), and whether the object is in a standard (read-only) or custom (editable) model. Call this BEFORE editing to understand the current state."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""filePath"": {
            ""type"": ""string"",
            ""description"": ""Absolute path to the D365FO metadata XML file to read.""
          }
        },
        ""required"": [""filePath""]
      }
    },
    {
      ""name"": ""xpp_update_object"",
      ""description"": ""Edits an existing D365FO X++ object by file path. Updates declaration, adds/updates/removes methods, and replaces metadata. ONLY works on custom model objects — automatically blocks edits to standard Microsoft packages (ApplicationSuite, ApplicationPlatform, etc.). Use this for modifying any existing object."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""filePath"": {
            ""type"": ""string"",
            ""description"": ""Absolute path to the D365FO metadata XML file to edit.""
          },
          ""declaration"": {
            ""type"": ""string"",
            ""description"": ""New X++ class/table declaration block. Replaces the existing Declaration CDATA.""
          },
          ""methods"": {
            ""type"": ""array"",
            ""items"": { ""type"": ""string"" },
            ""description"": ""Array of method source strings. Each is a full method implementation. If a method with the same name exists, it is REPLACED. If not, it is ADDED as a new method.""
          },
          ""removeMethodNames"": {
            ""type"": ""array"",
            ""items"": { ""type"": ""string"" },
            ""description"": ""Array of method names to DELETE from the object.""
          },
          ""metadataXml"": {
            ""type"": ""string"",
            ""description"": ""New metadata XML fragment. REPLACES all existing metadata (everything outside Name and SourceCode). Include the complete metadata you want.""
          }
        },
        ""required"": [""filePath""]
      }
    },
    {
      ""name"": ""xpp_update_current_object"",
      ""description"": ""Edits the D365FO X++ object that is currently open in the Visual Studio editor. Same capabilities as xpp_update_object — pass the active document's file path. ONLY works on custom model objects."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""filePath"": {
            ""type"": ""string"",
            ""description"": ""Absolute path to the currently open D365FO metadata XML file.""
          },
          ""declaration"": {
            ""type"": ""string"",
            ""description"": ""New X++ class/table declaration block. Replaces the existing Declaration CDATA.""
          },
          ""methods"": {
            ""type"": ""array"",
            ""items"": { ""type"": ""string"" },
            ""description"": ""Array of method source strings. Existing methods matched by name are REPLACED; new methods are ADDED.""
          },
          ""removeMethodNames"": {
            ""type"": ""array"",
            ""items"": { ""type"": ""string"" },
            ""description"": ""Array of method names to DELETE from the object.""
          },
          ""metadataXml"": {
            ""type"": ""string"",
            ""description"": ""New metadata XML fragment. REPLACES all existing metadata outside Name and SourceCode.""
          }
        },
        ""required"": [""filePath""]
      }
    }
  ]
}";
            return BuildResult(id, result);
        }

        static string HandleToolCall(string id, string json)
        {
            string toolName = ExtractNestedString(json, "params", "name");
            switch (toolName)
            {
                case "xpp_create_object":
                    return HandleCreateObject(id, json);
                case "xpp_read_object":
                    return HandleReadObject(id, json);
                case "xpp_update_object":
                case "xpp_update_current_object":
                    return HandleUpdateObject(id, json);
                default:
                    return BuildError(id, -32602, "Unknown tool: " + toolName);
            }
        }

        static string HandleCreateObject(string id, string json)
        {
            try
            {
                string argsJson = ExtractNestedObject(json, "params", "arguments");

                string objectType = ExtractJsonString(argsJson, "objectType");
                string objectName = ExtractJsonString(argsJson, "objectName");
                string suggestedCode = ExtractJsonString(argsJson, "suggestedCode");
                string metadataXml = ExtractJsonString(argsJson, "metadataXml");
                string baseObjectName = ExtractJsonString(argsJson, "baseObjectName");
                string targetDir = ExtractJsonString(argsJson, "targetDirectory");
                string[] methods = ExtractJsonStringArray(argsJson, "methods");

                if (string.IsNullOrEmpty(objectType) || string.IsNullOrEmpty(objectName))
                    return BuildToolError(id, "objectType and objectName are required.");
                if (string.IsNullOrEmpty(targetDir))
                    return BuildToolError(id, "targetDirectory is required.");

                string subfolder = objectType;
                string fullDir = Path.Combine(targetDir, subfolder);
                Directory.CreateDirectory(fullDir);

                string xml = BuildObjectXml(objectType, objectName, suggestedCode, methods, metadataXml);
                string filePath = Path.Combine(fullDir, objectName + ".xml");
                File.WriteAllText(filePath, xml, new UTF8Encoding(true));

                Console.Error.WriteLine($"[{ServerName}] Created: {filePath}");

                string resultText = "Created " + objectType + " '" + objectName + "' at " + filePath;
                string content = @"{ ""type"": ""text"", ""text"": """ + EscapeJsonString(resultText) + @""" }";
                string result = @"{ ""content"": [" + content + @"], ""isError"": false }";
                return BuildResult(id, result);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{ServerName}] Error: {ex.Message}");
                return BuildToolError(id, "Error creating object: " + ex.Message);
            }
        }

        static string HandleReadObject(string id, string json)
        {
            try
            {
                string argsJson = ExtractNestedObject(json, "params", "arguments");
                string filePath = ExtractJsonString(argsJson, "filePath");

                if (string.IsNullOrEmpty(filePath))
                    return BuildToolError(id, "filePath is required.");
                if (!File.Exists(filePath))
                    return BuildToolError(id, "File not found: " + filePath);

                var doc = new XmlDocument();
                doc.Load(filePath);
                var root = doc.DocumentElement;

                string objectType = root.Name;
                string objectName = GetXmlChildText(root, "Name");
                bool isStandard = IsStandardPackagePath(filePath);

                // Extract declaration
                string declaration = "";
                var declNode = root.SelectSingleNode("SourceCode/Declaration");
                if (declNode != null)
                    declaration = GetCDataText(declNode);

                // Extract methods
                var methodEntries = new List<string>();
                var methodNodes = root.SelectNodes("SourceCode/Methods/Method");
                if (methodNodes != null)
                {
                    foreach (XmlNode mn in methodNodes)
                    {
                        string mName = GetXmlChildText(mn, "Name");
                        string mSource = "";
                        var srcNode = mn.SelectSingleNode("Source");
                        if (srcNode != null)
                            mSource = GetCDataText(srcNode);
                        methodEntries.Add("--- " + mName + " ---\n" + mSource);
                    }
                }

                // Build response
                var sb = new StringBuilder();
                sb.AppendLine("ObjectType: " + objectType);
                sb.AppendLine("Name: " + objectName);
                sb.AppendLine("Editable: " + (isStandard
                    ? "NO — standard/base model object (use extensions to modify)"
                    : "YES — custom model object (direct edits allowed)"));
                sb.AppendLine("FilePath: " + filePath);
                sb.AppendLine();
                sb.AppendLine("=== Declaration ===");
                sb.AppendLine(declaration);
                sb.AppendLine();
                sb.AppendLine("=== Methods (" + methodEntries.Count + ") ===");
                foreach (string entry in methodEntries)
                {
                    sb.AppendLine(entry);
                    sb.AppendLine();
                }

                string resultText = sb.ToString();
                string contentJson = @"{ ""type"": ""text"", ""text"": """ + EscapeJsonString(resultText) + @""" }";
                string resultObj = @"{ ""content"": [" + contentJson + @"], ""isError"": false }";
                return BuildResult(id, resultObj);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{ServerName}] Error reading object: {ex.Message}");
                return BuildToolError(id, "Error reading object: " + ex.Message);
            }
        }

        static string HandleUpdateObject(string id, string json)
        {
            try
            {
                string argsJson = ExtractNestedObject(json, "params", "arguments");
                string filePath = ExtractJsonString(argsJson, "filePath");
                string declaration = ExtractJsonString(argsJson, "declaration");
                string[] methods = ExtractJsonStringArray(argsJson, "methods");
                string metadataXml = ExtractJsonString(argsJson, "metadataXml");
                string[] removeMethodNames = ExtractJsonStringArray(argsJson, "removeMethodNames");

                if (string.IsNullOrEmpty(filePath))
                    return BuildToolError(id, "filePath is required.");
                if (!File.Exists(filePath))
                    return BuildToolError(id, "File not found: " + filePath);
                if (IsStandardPackagePath(filePath))
                    return BuildToolError(id,
                        "BLOCKED: Cannot edit standard/base model objects. " +
                        "Objects in ApplicationSuite, ApplicationPlatform, ApplicationFoundation, " +
                        "and other Microsoft packages are read-only. " +
                        "Use xpp_create_object to create extensions instead (CoC, event handlers, metadata extensions). " +
                        "File: " + filePath);

                var doc = new XmlDocument();
                doc.PreserveWhitespace = false;
                doc.Load(filePath);
                var root = doc.DocumentElement;
                int changes = 0;

                // 1. Update declaration
                if (!string.IsNullOrEmpty(declaration))
                {
                    var declNode = root.SelectSingleNode("SourceCode/Declaration");
                    if (declNode != null)
                    {
                        declNode.RemoveAll();
                        declNode.AppendChild(doc.CreateCDataSection(declaration));
                        changes++;
                    }
                }

                // 2. Update/add methods
                if (methods != null && methods.Length > 0)
                {
                    var sourceCodeNode = root.SelectSingleNode("SourceCode");
                    var methodsNode = root.SelectSingleNode("SourceCode/Methods");

                    if (methodsNode == null && sourceCodeNode != null)
                    {
                        methodsNode = doc.CreateElement("Methods");
                        sourceCodeNode.AppendChild(methodsNode);
                    }

                    if (methodsNode != null)
                    {
                        foreach (string methodSource in methods)
                        {
                            if (string.IsNullOrWhiteSpace(methodSource)) continue;
                            string methodName = ExtractMethodName(methodSource);

                            // Find existing method by name
                            XmlNode existing = null;
                            foreach (XmlNode mn in methodsNode.SelectNodes("Method"))
                            {
                                if (GetXmlChildText(mn, "Name") == methodName)
                                {
                                    existing = mn;
                                    break;
                                }
                            }

                            if (existing != null)
                            {
                                // Replace existing method's Source CDATA
                                var srcNode = existing.SelectSingleNode("Source");
                                if (srcNode != null)
                                {
                                    srcNode.RemoveAll();
                                    srcNode.AppendChild(doc.CreateCDataSection(methodSource));
                                }
                            }
                            else
                            {
                                // Add new method
                                var newMethod = doc.CreateElement("Method");
                                var nameElem = doc.CreateElement("Name");
                                nameElem.InnerText = methodName;
                                newMethod.AppendChild(nameElem);
                                var srcElem = doc.CreateElement("Source");
                                srcElem.AppendChild(doc.CreateCDataSection(methodSource));
                                newMethod.AppendChild(srcElem);
                                methodsNode.AppendChild(newMethod);
                            }
                            changes++;
                        }
                    }
                }

                // 3. Remove methods by name
                if (removeMethodNames != null)
                {
                    var methodsNode = root.SelectSingleNode("SourceCode/Methods");
                    if (methodsNode != null)
                    {
                        foreach (string removeName in removeMethodNames)
                        {
                            if (string.IsNullOrWhiteSpace(removeName)) continue;
                            foreach (XmlNode mn in methodsNode.SelectNodes("Method"))
                            {
                                if (GetXmlChildText(mn, "Name") == removeName)
                                {
                                    methodsNode.RemoveChild(mn);
                                    changes++;
                                    break;
                                }
                            }
                        }
                    }
                }

                // 4. Replace metadata (everything outside Name and SourceCode)
                if (!string.IsNullOrEmpty(metadataXml))
                {
                    var toRemove = new List<XmlNode>();
                    foreach (XmlNode child in root.ChildNodes)
                    {
                        if (child.NodeType == XmlNodeType.Element
                            && child.Name != "Name" && child.Name != "SourceCode")
                            toRemove.Add(child);
                    }
                    foreach (var node in toRemove)
                        root.RemoveChild(node);

                    var fragment = doc.CreateDocumentFragment();
                    fragment.InnerXml = metadataXml;
                    root.AppendChild(fragment);
                    changes++;
                }

                // Save with UTF-8 BOM
                using (var writer = new StreamWriter(filePath, false, new UTF8Encoding(true)))
                {
                    doc.Save(writer);
                }

                string objectName = GetXmlChildText(root, "Name");
                Console.Error.WriteLine($"[{ServerName}] Updated: {root.Name}/{objectName} ({changes} changes)");

                string resultText = "Updated " + root.Name + " '" + objectName
                    + "' — " + changes + " change(s) applied. File: " + filePath;
                string contentJson = @"{ ""type"": ""text"", ""text"": """ + EscapeJsonString(resultText) + @""" }";
                string resultObj = @"{ ""content"": [" + contentJson + @"], ""isError"": false }";
                return BuildResult(id, resultObj);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{ServerName}] Error updating object: {ex.Message}");
                return BuildToolError(id, "Error updating object: " + ex.Message);
            }
        }

        // ── XML helpers (System.Xml) ──

        static string GetXmlChildText(XmlNode parent, string childName)
        {
            var child = parent.SelectSingleNode(childName);
            return child != null ? child.InnerText : "(unknown)";
        }

        static string GetCDataText(XmlNode node)
        {
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType == XmlNodeType.CDATA)
                    return child.Value ?? "";
            }
            return node.InnerText ?? "";
        }

        /// <summary>
        /// Returns true if the file path is inside a standard Microsoft D365FO package.
        /// Objects in these packages must NOT be edited directly — use extensions.
        /// </summary>
        static bool IsStandardPackagePath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            string lower = filePath.Replace('/', '\\').ToLowerInvariant();

            string[] standardPackages =
            {
                "\\applicationsuite\\", "\\applicationplatform\\",
                "\\applicationfoundation\\", "\\applicationcommon\\",
                "\\directory\\", "\\currency\\", "\\dimensions\\",
                "\\generalledger\\", "\\personnelcore\\", "\\taxengine\\",
                "\\retail\\", "\\commerce\\", "\\supplychain\\",
                "\\projectaccounting\\", "\\casemanagement\\",
                "\\contactperson\\", "\\unitofmeasure\\",
                "\\electronicreporting\\", "\\systemadministration\\",
                "\\personnelmanagement\\", "\\expensemanagement\\",
                "\\budgeting\\", "\\accountsreceivable\\",
                "\\accountspayable\\", "\\tax\\", "\\fixedassets\\",
                "\\cashmanagement\\", "\\inventorymanagement\\",
                "\\procurement\\", "\\warehouse\\", "\\transportation\\",
                "\\manufacturing\\", "\\production\\", "\\costaccounting\\"
            };

            foreach (string pkg in standardPackages)
            {
                if (lower.Contains(pkg)) return true;
            }
            return false;
        }

        // ── X++ Object XML Builders ──

        static string BuildObjectXml(string objectType, string name,
            string suggestedCode, string[] methods, string metadataXml)
        {
            string safeName = SecurityElement.Escape(name) ?? name;
            string code = suggestedCode ?? "";
            string meta = metadataXml ?? "";

            switch (objectType)
            {
                // Objects with SourceCode (Declaration + Methods)
                case "AxClass":
                    return BuildAxClassXml(safeName, code, methods, meta);
                case "AxTable":
                case "AxTableExtension":
                case "AxView":
                case "AxDataEntityView":
                case "AxCompositeDataEntityView":
                case "AxMap":
                    return BuildAxTableXml(safeName, code, methods, meta);
                case "AxForm":
                case "AxFormExtension":
                    return BuildAxFormXml(safeName, code, methods, meta);

                // EDTs (special i:type handling)
                case "AxEdt":
                case "AxEdtExtension":
                    return BuildSimpleXml("AxEdt", safeName, meta);
                case "AxEnum":
                case "AxEnumExtension":
                    return BuildSimpleXml("AxEnum", safeName, meta);

                // Menu system
                case "AxMenuItemDisplay":
                    return BuildSimpleXml("AxMenuItemDisplay", safeName, meta);
                case "AxMenuItemOutput":
                    return BuildSimpleXml("AxMenuItemOutput", safeName, meta);
                case "AxMenuItemAction":
                    return BuildSimpleXml("AxMenuItemAction", safeName, meta);

                // All other types → generic simple XML
                default:
                    return BuildSimpleXml(objectType, safeName, meta);
            }
        }

        static string BuildSourceCodeBlock(string defaultDecl, string suggestedCode, string[] methods)
        {
            string decl = string.IsNullOrWhiteSpace(suggestedCode) ? defaultDecl : suggestedCode;

            var sb = new StringBuilder();
            sb.AppendLine("  <SourceCode>");
            sb.AppendLine("    <Declaration><![CDATA[" + decl + "]]></Declaration>");

            if (methods != null && methods.Length > 0)
            {
                sb.AppendLine("    <Methods>");
                foreach (string m in methods)
                {
                    if (string.IsNullOrWhiteSpace(m)) continue;
                    string methodName = ExtractMethodName(m);
                    sb.AppendLine("      <Method>");
                    sb.AppendLine("        <Name>" + (SecurityElement.Escape(methodName) ?? methodName) + "</Name>");
                    sb.AppendLine("        <Source><![CDATA[" + m + "]]></Source>");
                    sb.AppendLine("      </Method>");
                }
                sb.AppendLine("    </Methods>");
            }
            else
            {
                sb.AppendLine("    <Methods />");
            }

            sb.Append("  </SourceCode>");
            return sb.ToString();
        }

        static string ExtractMethodName(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return "unknownMethod";
            string[] lines = source.Split(new[] { '\n' }, StringSplitOptions.None);
            var sigLine = new StringBuilder();
            foreach (string line in lines)
            {
                string l = line.Trim();
                if (l.StartsWith("///") || l.StartsWith("//") || l.StartsWith("/*")
                    || l.StartsWith("*") || l.Length == 0 || l.StartsWith("["))
                    continue;
                sigLine.Append(l);
                if (l.Contains("(")) break;
            }
            string sig = sigLine.ToString();
            int paren = sig.IndexOf('(');
            if (paren <= 0) return "unknownMethod";
            string before = sig.Substring(0, paren).Trim();
            string[] tokens = before.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return tokens.Length > 0 ? tokens[tokens.Length - 1] : "unknownMethod";
        }

        static string BuildAxClassXml(string name, string code, string[] methods, string meta)
        {
            string defaultDecl = "class " + name + "\n{\n}";
            string src = BuildSourceCodeBlock(defaultDecl, code, methods);
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<AxClass xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\">");
            sb.AppendLine("  <Name>" + name + "</Name>");
            sb.AppendLine(src);
            if (!string.IsNullOrWhiteSpace(meta)) sb.AppendLine(meta);
            sb.Append("</AxClass>");
            return sb.ToString();
        }

        static string BuildAxTableXml(string name, string code, string[] methods, string meta)
        {
            string defaultDecl = "public class " + name + " extends common\n{\n}";
            string src = BuildSourceCodeBlock(defaultDecl, code, methods);
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<AxTable xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\">");
            sb.AppendLine("  <Name>" + name + "</Name>");
            sb.AppendLine(src);
            if (!string.IsNullOrWhiteSpace(meta)) sb.AppendLine(meta);
            sb.Append("</AxTable>");
            return sb.ToString();
        }

        static string BuildAxFormXml(string name, string code, string[] methods, string meta)
        {
            string defaultDecl = "class " + name + " extends FormRun\n{\n}";
            string src = BuildSourceCodeBlock(defaultDecl, code, methods);
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<AxForm xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\">");
            sb.AppendLine("  <Name>" + name + "</Name>");
            sb.AppendLine(src);
            if (!string.IsNullOrWhiteSpace(meta))
            {
                sb.AppendLine(meta);
            }
            else
            {
                sb.AppendLine("  <Design>");
                sb.AppendLine("    <Caption>" + name + "</Caption>");
                sb.AppendLine("  </Design>");
            }
            sb.Append("</AxForm>");
            return sb.ToString();
        }

        static string BuildSimpleXml(string rootTag, string name, string meta)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<" + rootTag + " xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\">");
            sb.AppendLine("  <Name>" + name + "</Name>");
            if (!string.IsNullOrWhiteSpace(meta)) sb.AppendLine(meta);
            sb.Append("</" + rootTag + ">");
            return sb.ToString();
        }

        // ── JSON-RPC helpers (minimal, no external dependencies) ──

        static string BuildResult(string id, string resultJson)
        {
            if (id == null) return null;
            // id could be a number or string — keep original format
            string idValue = id.StartsWith("\"") ? id : id;
            return @"{ ""jsonrpc"": ""2.0"", ""id"": " + idValue + @", ""result"": " + resultJson + " }";
        }

        static string BuildError(string id, int code, string message)
        {
            string msg = EscapeJsonString(message);
            string err = @"{ ""code"": " + code + @", ""message"": """ + msg + @""" }";
            if (id == null) return null;
            return @"{ ""jsonrpc"": ""2.0"", ""id"": " + id + @", ""error"": " + err + " }";
        }

        static string BuildToolError(string id, string message)
        {
            string text = EscapeJsonString(message);
            string content = @"{ ""type"": ""text"", ""text"": """ + text + @""" }";
            string result = @"{ ""content"": [" + content + @"], ""isError"": true }";
            return BuildResult(id, result);
        }

        // ── Minimal JSON parsing (no external deps) ──

        static string ExtractJsonString(string json, string key)
        {
            if (json == null) return null;
            string pattern = "\"" + key + "\"";
            int idx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) return null;

            int colonIdx = json.IndexOf(':', idx + pattern.Length);
            if (colonIdx < 0) return null;

            // Skip whitespace after colon
            int valStart = colonIdx + 1;
            while (valStart < json.Length && char.IsWhiteSpace(json[valStart])) valStart++;

            if (valStart >= json.Length) return null;

            // Check if value is a number (for id field)
            if (char.IsDigit(json[valStart]) || json[valStart] == '-')
            {
                int numEnd = valStart;
                while (numEnd < json.Length && (char.IsDigit(json[numEnd]) || json[numEnd] == '.' || json[numEnd] == '-'))
                    numEnd++;
                return json.Substring(valStart, numEnd - valStart);
            }

            if (json[valStart] == 'n') return null; // null

            if (json[valStart] != '"') return null;

            // Parse string value with escape handling
            var sb = new StringBuilder();
            int i = valStart + 1;
            while (i < json.Length)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    char next = json[i + 1];
                    switch (next)
                    {
                        case '"': sb.Append('"'); i += 2; continue;
                        case '\\': sb.Append('\\'); i += 2; continue;
                        case '/': sb.Append('/'); i += 2; continue;
                        case 'n': sb.Append('\n'); i += 2; continue;
                        case 'r': sb.Append('\r'); i += 2; continue;
                        case 't': sb.Append('\t'); i += 2; continue;
                        default: sb.Append(next); i += 2; continue;
                    }
                }
                if (c == '"') break;
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        static string ExtractNestedString(string json, string outerKey, string innerKey)
        {
            string inner = ExtractNestedObject(json, outerKey, null);
            if (inner == null)
            {
                // Try flat extraction
                return ExtractJsonString(json, innerKey);
            }
            return ExtractJsonString(inner, innerKey);
        }

        static string ExtractNestedObject(string json, string key, string unused)
        {
            if (json == null) return null;
            string pattern = "\"" + key + "\"";
            int idx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) return null;

            int colonIdx = json.IndexOf(':', idx + pattern.Length);
            if (colonIdx < 0) return null;

            int braceStart = json.IndexOf('{', colonIdx);
            if (braceStart < 0) return null;

            // Find matching closing brace
            int depth = 0;
            bool inString = false;
            bool escape = false;
            for (int i = braceStart; i < json.Length; i++)
            {
                char c = json[i];
                if (escape) { escape = false; continue; }
                if (c == '\\') { escape = true; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (inString) continue;
                if (c == '{') depth++;
                if (c == '}') { depth--; if (depth == 0) return json.Substring(braceStart, i - braceStart + 1); }
            }
            return null;
        }

        static string[] ExtractJsonStringArray(string json, string key)
        {
            if (json == null) return null;
            string pattern = "\"" + key + "\"";
            int idx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) return null;

            int colonIdx = json.IndexOf(':', idx + pattern.Length);
            if (colonIdx < 0) return null;

            int bracketStart = json.IndexOf('[', colonIdx);
            if (bracketStart < 0) return null;

            // Find matching closing bracket
            int depth = 0;
            bool inString = false;
            bool escape = false;
            int bracketEnd = -1;
            for (int i = bracketStart; i < json.Length; i++)
            {
                char c = json[i];
                if (escape) { escape = false; continue; }
                if (c == '\\') { escape = true; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (inString) continue;
                if (c == '[') depth++;
                if (c == ']') { depth--; if (depth == 0) { bracketEnd = i; break; } }
            }

            if (bracketEnd < 0) return null;

            // Parse out individual strings from the array
            string arrayContent = json.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
            var result = new List<string>();

            int pos = 0;
            while (pos < arrayContent.Length)
            {
                int quoteStart = arrayContent.IndexOf('"', pos);
                if (quoteStart < 0) break;

                // Find end of string (handle escapes)
                var val = new StringBuilder();
                int j = quoteStart + 1;
                while (j < arrayContent.Length)
                {
                    char c = arrayContent[j];
                    if (c == '\\' && j + 1 < arrayContent.Length)
                    {
                        char next = arrayContent[j + 1];
                        switch (next)
                        {
                            case '"': val.Append('"'); break;
                            case '\\': val.Append('\\'); break;
                            case 'n': val.Append('\n'); break;
                            case 'r': val.Append('\r'); break;
                            case 't': val.Append('\t'); break;
                            default: val.Append(next); break;
                        }
                        j += 2;
                        continue;
                    }
                    if (c == '"') break;
                    val.Append(c);
                    j++;
                }
                result.Add(val.ToString());
                pos = j + 1;
            }

            return result.Count > 0 ? result.ToArray() : null;
        }

        static string EscapeJsonString(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
