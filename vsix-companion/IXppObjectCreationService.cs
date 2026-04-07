namespace XppAiCopilotCompanion
{
    public enum XppObjectType
    {
        AxClass,
        AxTable,
        AxForm,
        AxEdt,
        AxEnum
    }

    public sealed class XppObjectCreateRequest
    {
        public XppObjectType ObjectType { get; set; }
        public string ObjectName { get; set; }
        public string TargetDirectory { get; set; }
        public bool AddToActiveProject { get; set; } = true;
        public string SuggestedCode { get; set; }
        public string BaseObjectName { get; set; }
    }

    public sealed class XppObjectCreateResult
    {
        public string FilePath { get; set; }
        public string ObjectName { get; set; }
        public bool AddedToProject { get; set; }
    }

    public interface IXppObjectCreationService
    {
        XppObjectCreateResult CreateObject(XppObjectCreateRequest request);
    }
}
