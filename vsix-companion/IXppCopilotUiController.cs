namespace XppAiCopilotCompanion
{
    // UI command surface used by tool window / menu commands.
    public interface IXppCopilotUiController
    {
        string RefreshContextFromUi(string userQuery);
        void ApplyAiEditToOpenDocument(string newContent);
        void AddFileToActiveProject(string filePath);
    }
}
