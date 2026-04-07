namespace XppAiCopilotCompanion
{
    // In-process context pipeline used by Copilot provider adapter.
    public interface IXppContextPipelineService
    {
        XppContextBundle BuildBundle(string workspaceRoot, string query);
    }
}
