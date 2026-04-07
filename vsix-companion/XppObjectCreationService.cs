using System;
using System.IO;
using System.Security;

namespace XppAiCopilotCompanion
{
    // UI-driven object creation service inspired by one-click tooling workflows.
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
            if (string.IsNullOrWhiteSpace(request.ObjectName)) throw new ArgumentException("ObjectName is required", nameof(request));
            if (string.IsNullOrWhiteSpace(request.TargetDirectory)) throw new ArgumentException("TargetDirectory is required", nameof(request));

            Directory.CreateDirectory(request.TargetDirectory);

            string xml = BuildObjectXml(request);
            string filePath = Path.Combine(request.TargetDirectory, request.ObjectName + ".xml");
            File.WriteAllText(filePath, xml);

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

        private static string BuildObjectXml(XppObjectCreateRequest request)
        {
            string name = EscapeXml(request.ObjectName);
            string code = request.SuggestedCode ?? string.Empty;

            switch (request.ObjectType)
            {
                case XppObjectType.AxClass:
                    return BuildAxClass(name, code);
                case XppObjectType.AxTable:
                    return BuildAxTable(name, code);
                case XppObjectType.AxEdt:
                    return BuildAxEdt(name);
                case XppObjectType.AxEnum:
                    return BuildAxEnum(name);
                case XppObjectType.AxForm:
                    return BuildAxForm(name);
                default:
                    throw new InvalidOperationException("Unsupported object type");
            }
        }

        private static string BuildAxClass(string name, string suggestedCode)
        {
            string declarationCode = string.IsNullOrWhiteSpace(suggestedCode)
                ? "class " + name + "\n{\n}\n"
                : suggestedCode;

            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
                + "<AxClass xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\">\n"
                + "  <Name>" + name + "</Name>\n"
                + "  <SourceCode>\n"
                + "    <Declaration><![CDATA[\n" + declarationCode + "\n]]></Declaration>\n"
                + "    <Methods />\n"
                + "  </SourceCode>\n"
                + "</AxClass>\n";
        }

        private static string BuildAxTable(string name, string suggestedCode)
        {
            string declarationCode = string.IsNullOrWhiteSpace(suggestedCode)
                ? "public class " + name + " extends common\n{\n}\n"
                : suggestedCode;

            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
                + "<AxTable xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\">\n"
                + "  <Name>" + name + "</Name>\n"
                + "  <SourceCode>\n"
                + "    <Declaration><![CDATA[\n" + declarationCode + "\n]]></Declaration>\n"
                + "    <Methods />\n"
                + "  </SourceCode>\n"
                + "</AxTable>\n";
        }

        private static string BuildAxEdt(string name)
        {
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
                + "<AxEdt xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns=\"\" i:type=\"AxEdtString\">\n"
                + "  <Name>" + name + "</Name>\n"
                + "  <Extends>Description</Extends>\n"
                + "</AxEdt>\n";
        }

        private static string BuildAxEnum(string name)
        {
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
                + "<AxEnum xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\">\n"
                + "  <Name>" + name + "</Name>\n"
                + "  <UseEnumValue>No</UseEnumValue>\n"
                + "  <EnumValues />\n"
                + "</AxEnum>\n";
        }

        private static string BuildAxForm(string name)
        {
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
                + "<AxForm xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\">\n"
                + "  <Name>" + name + "</Name>\n"
                + "  <Design>\n"
                + "    <Name>Design</Name>\n"
                + "  </Design>\n"
                + "</AxForm>\n";
        }

        private static string EscapeXml(string value)
        {
            return SecurityElement.Escape(value) ?? string.Empty;
        }
    }
}
