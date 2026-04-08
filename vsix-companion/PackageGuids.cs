using System;

namespace XppAiCopilotCompanion
{
    public static class PackageGuids
    {
        public const string PackageGuidString = "b8e3f1a0-7c24-4d5b-9e1f-2a3b4c5d6e7f";
        public const string CommandSetGuidString = "c9f4e2b1-8d35-4e6c-af20-3b4c5d6e7f80";

        public static readonly Guid CommandSetGuid = new Guid(CommandSetGuidString);
    }

    public static class CommandIds
    {
        public const int RefreshContext = 0x0100;
        public const int GenerateCode = 0x0101;
        public const int CreateObject = 0x0102;
        public const int RegisterMcp = 0x0103;
        public const int McpDiagnostics = 0x0104;
    }
}
