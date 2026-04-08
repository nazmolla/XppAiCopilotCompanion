using System;
using System.IO;
using System.Security;
using System.Text;

namespace XppAiCopilotCompanion
{
    public sealed class XppObjectCreationService : IXppObjectCreationService
    {
        private readonly IVisualStudioSessionService _vsSession;

        public XppObjectCreationService(IVisualStudioSessionService vsSession)
        {
            _vsSession = vsSession ?? throw new ArgumentNullException(nameof(vsSession));
        }

        public XppObjectCreateResult CreateObject(XppObjectCreateRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.ObjectName))
                throw new ArgumentException("ObjectName is required", nameof(request));
            if (string.IsNullOrWhiteSpace(request.TargetDirectory))
                throw new ArgumentException("TargetDirectory is required", nameof(request));

            // Ensure the target directory uses the correct D365FO subfolder
            // e.g. AxClass, AxTable, AxForm, etc.
            string objectSubfolder = GetObjectSubfolder(request.ObjectType);
            string targetDir = Path.Combine(request.TargetDirectory, objectSubfolder);
            Directory.CreateDirectory(targetDir);

            string xml = BuildObjectXml(request);
            string filePath = Path.Combine(targetDir, request.ObjectName + ".xml");
            File.WriteAllText(filePath, xml, new UTF8Encoding(true));

            bool added = false;
            if (request.AddToActiveProject)
            {
                _vsSession.AddExistingFileToActiveProject(filePath);
                added = true;
            }

            return new XppObjectCreateResult
            {
                FilePath = filePath,
                ObjectName = request.ObjectName,
                AddedToProject = added
            };
        }

        private static string GetObjectSubfolder(XppObjectType objectType)
        {
            // The subfolder name matches the enum name for all types
            return objectType.ToString();
        }

        private static string BuildObjectXml(XppObjectCreateRequest request)
        {
            string name = EscapeXml(request.ObjectName);
            string code = request.SuggestedCode ?? string.Empty;
            string metadata = request.MetadataXml ?? string.Empty;

            switch (request.ObjectType)
            {
                // Objects with SourceCode (Declaration + Methods)
                case XppObjectType.AxClass:
                    return BuildAxClass(name, code, request.Methods, metadata);
                case XppObjectType.AxTable:
                case XppObjectType.AxTableExtension:
                    return BuildAxTable(name, code, request.Methods, metadata);
                case XppObjectType.AxView:
                case XppObjectType.AxDataEntityView:
                case XppObjectType.AxCompositeDataEntityView:
                case XppObjectType.AxMap:
                    return BuildAxTable(name, code, request.Methods, metadata);
                case XppObjectType.AxForm:
                case XppObjectType.AxFormExtension:
                    return BuildAxForm(name, code, request.Methods, metadata);

                // EDTs and Enums
                case XppObjectType.AxEdt:
                case XppObjectType.AxEdtExtension:
                    return BuildAxEdt(name, metadata);
                case XppObjectType.AxEnum:
                case XppObjectType.AxEnumExtension:
                    return BuildAxEnum(name, metadata);

                // Menu system
                case XppObjectType.AxMenuItemDisplay:
                case XppObjectType.AxMenuItemOutput:
                case XppObjectType.AxMenuItemAction:
                    return BuildAxMenuItem(request.ObjectType, name, metadata);
                case XppObjectType.AxMenu:
                case XppObjectType.AxMenuExtension:
                    return BuildSimpleXml(request.ObjectType.ToString(), name, metadata);

                // Queries
                case XppObjectType.AxQuery:
                case XppObjectType.AxQuerySimpleExtension:
                    return BuildSimpleXml(request.ObjectType.ToString(), name, metadata);

                // Security
                case XppObjectType.AxSecurityPrivilege:
                case XppObjectType.AxSecurityDuty:
                case XppObjectType.AxSecurityRole:
                case XppObjectType.AxSecurityPolicy:
                    return BuildSimpleXml(request.ObjectType.ToString(), name, metadata);

                // Services
                case XppObjectType.AxService:
                case XppObjectType.AxServiceGroup:
                    return BuildSimpleXml(request.ObjectType.ToString(), name, metadata);

                // Workflow
                case XppObjectType.AxWorkflowCategory:
                case XppObjectType.AxWorkflowType:
                case XppObjectType.AxWorkflowApproval:
                case XppObjectType.AxWorkflowTask:
                case XppObjectType.AxWorkflowAutomatedTask:
                    return BuildSimpleXml(request.ObjectType.ToString(), name, metadata);

                // Analytics & Reporting
                case XppObjectType.AxSsrsReport:
                case XppObjectType.AxAggregateMeasurement:
                case XppObjectType.AxAggregateDimension:
                case XppObjectType.AxKpi:
                    return BuildSimpleXml(request.ObjectType.ToString(), name, metadata);

                // Configuration & Licensing
                case XppObjectType.AxConfigurationKey:
                case XppObjectType.AxConfigurationKeyGroup:
                case XppObjectType.AxLicenseCode:
                    return BuildSimpleXml(request.ObjectType.ToString(), name, metadata);

                // Tiles, number sequences, resources
                case XppObjectType.AxTile:
                case XppObjectType.AxNumberSequenceModule:
                case XppObjectType.AxResource:
                    return BuildSimpleXml(request.ObjectType.ToString(), name, metadata);

                default:
                    return BuildSimpleXml(request.ObjectType.ToString(), name, metadata);
            }
        }

        private static string BuildSourceCode(string name, string defaultDeclaration,
            string suggestedCode, string[] methods)
        {
            string declaration = string.IsNullOrWhiteSpace(suggestedCode)
                ? defaultDeclaration
                : suggestedCode;

            var sb = new StringBuilder();
            sb.AppendLine("  <SourceCode>");
            sb.AppendLine("    <Declaration><![CDATA[" + declaration + "]]></Declaration>");

            if (methods != null && methods.Length > 0)
            {
                sb.AppendLine("    <Methods>");
                foreach (string method in methods)
                {
                    if (string.IsNullOrWhiteSpace(method)) continue;
                    string methodName = ExtractMethodName(method);
                    sb.AppendLine("      <Method>");
                    sb.AppendLine("        <Name>" + EscapeXml(methodName) + "</Name>");
                    sb.AppendLine("        <Source><![CDATA[" + method + "]]></Source>");
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

        /// <summary>
        /// Extracts the method name from X++ source by finding the identifier before '('.
        /// Handles modifiers like public/private/static/void/display/edit and return types.
        /// </summary>
        private static string ExtractMethodName(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return "unknownMethod";

            // Strip XML doc comments and regular comments, then find methodName(
            string trimmed = source.TrimStart();

            // Skip lines that start with /// or //
            var lines = trimmed.Split(new[] { '\n' }, StringSplitOptions.None);
            var sigLine = new StringBuilder();
            foreach (string line in lines)
            {
                string l = line.Trim();
                if (l.StartsWith("///") || l.StartsWith("//") || l.StartsWith("/*") || l.StartsWith("*") || l.Length == 0)
                    continue;
                // Skip attributes like [PreHandlerFor(...)]
                if (l.StartsWith("["))
                    continue;
                sigLine.Append(l);
                if (l.Contains("(")) break;
            }

            string sig = sigLine.ToString();
            int parenIdx = sig.IndexOf('(');
            if (parenIdx <= 0) return "unknownMethod";

            // Everything before '(' — split by spaces, the last token is the method name
            string beforeParen = sig.Substring(0, parenIdx).Trim();
            string[] tokens = beforeParen.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return "unknownMethod";

            return tokens[tokens.Length - 1];
        }

        private static string BuildAxClass(string name, string suggestedCode,
            string[] methods, string metadata)
        {
            string defaultDecl = "class " + name + "\n{\n}";
            string sourceCode = BuildSourceCode(name, defaultDecl, suggestedCode, methods);

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<AxClass xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\">");
            sb.AppendLine("  <Name>" + name + "</Name>");
            sb.AppendLine(sourceCode);
            if (!string.IsNullOrWhiteSpace(metadata))
                sb.AppendLine(metadata);
            sb.Append("</AxClass>");
            return sb.ToString();
        }

        private static string BuildAxTable(string name, string suggestedCode,
            string[] methods, string metadata)
        {
            string defaultDecl = "public class " + name + " extends common\n{\n}";
            string sourceCode = BuildSourceCode(name, defaultDecl, suggestedCode, methods);

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<AxTable xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\">");
            sb.AppendLine("  <Name>" + name + "</Name>");
            sb.AppendLine(sourceCode);
            if (!string.IsNullOrWhiteSpace(metadata))
                sb.AppendLine(metadata);
            sb.Append("</AxTable>");
            return sb.ToString();
        }

        private static string BuildAxForm(string name, string suggestedCode,
            string[] methods, string metadata)
        {
            string defaultDecl = "class " + name + " extends FormRun\n{\n}";
            string sourceCode = BuildSourceCode(name, defaultDecl, suggestedCode, methods);

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<AxForm xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\">");
            sb.AppendLine("  <Name>" + name + "</Name>");
            sb.AppendLine(sourceCode);
            if (!string.IsNullOrWhiteSpace(metadata))
            {
                sb.AppendLine(metadata);
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

        private static string BuildAxEdt(string name, string metadata)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            if (!string.IsNullOrWhiteSpace(metadata))
            {
                sb.AppendLine("<AxEdt xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\">");
                sb.AppendLine("  <Name>" + name + "</Name>");
                sb.AppendLine(metadata);
                sb.Append("</AxEdt>");
            }
            else
            {
                sb.AppendLine("<AxEdt xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\" i:type=\"AxEdtString\">");
                sb.AppendLine("  <Name>" + name + "</Name>");
                sb.Append("</AxEdt>");
            }
            return sb.ToString();
        }

        private static string BuildAxEnum(string name, string metadata)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<AxEnum xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\">");
            sb.AppendLine("  <Name>" + name + "</Name>");
            if (!string.IsNullOrWhiteSpace(metadata))
                sb.AppendLine(metadata);
            sb.Append("</AxEnum>");
            return sb.ToString();
        }

        private static string BuildAxMenuItem(XppObjectType type, string name, string metadata)
        {
            string rootTag;
            switch (type)
            {
                case XppObjectType.AxMenuItemDisplay: rootTag = "AxMenuItemDisplay"; break;
                case XppObjectType.AxMenuItemOutput: rootTag = "AxMenuItemOutput"; break;
                case XppObjectType.AxMenuItemAction: rootTag = "AxMenuItemAction"; break;
                default: rootTag = "AxMenuItemDisplay"; break;
            }

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<" + rootTag + " xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\">");
            sb.AppendLine("  <Name>" + name + "</Name>");
            if (!string.IsNullOrWhiteSpace(metadata))
                sb.AppendLine(metadata);
            sb.Append("</" + rootTag + ">");
            return sb.ToString();
        }

        private static string BuildAxMenuExtension(string name, string metadata)
        {
            return BuildSimpleXml("AxMenuExtension", name, metadata);
        }

        private static string BuildSimpleXml(string rootTag, string name, string metadata)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<" + rootTag + " xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\">");
            sb.AppendLine("  <Name>" + name + "</Name>");
            if (!string.IsNullOrWhiteSpace(metadata))
                sb.AppendLine(metadata);
            sb.Append("</" + rootTag + ">");
            return sb.ToString();
        }

        private static string EscapeXml(string value)
        {
            return SecurityElement.Escape(value) ?? string.Empty;
        }
    }
}
