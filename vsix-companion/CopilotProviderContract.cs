namespace XppAiCopilotCompanion
{
    // Minimal contract used by a Visual Studio Copilot integration layer.
    // A concrete provider can call this from an ICopilotContextProvider implementation.
    public interface ICopilotContextBridge
    {
        string BuildContext(string workspaceRoot, string query);
    }
}
