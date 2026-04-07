namespace XppAiCopilotCompanion
{
    // Must be implemented using Visual Studio APIs (DTE/RDT/project system/editor buffers).
    public interface IVisualStudioSessionService
    {
        string GetActiveDocumentPath();
        string GetActiveDocumentText();
        string GetActiveSolutionDirectory();
        void ReplaceActiveDocumentText(string newContent);
        void AddExistingFileToActiveProject(string filePath);
    }
}
