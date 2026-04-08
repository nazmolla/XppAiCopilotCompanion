namespace XppAiCopilotCompanion
{
    public enum XppObjectType
    {
        // Data Model
        AxClass,
        AxTable,
        AxTableExtension,
        AxView,
        AxDataEntityView,
        AxCompositeDataEntityView,
        AxMap,
        AxEdt,
        AxEdtExtension,
        AxEnum,
        AxEnumExtension,

        // Forms & UI
        AxForm,
        AxFormExtension,
        AxTile,

        // Menu System
        AxMenu,
        AxMenuExtension,
        AxMenuItemDisplay,
        AxMenuItemOutput,
        AxMenuItemAction,

        // Queries
        AxQuery,
        AxQuerySimpleExtension,

        // Security
        AxSecurityPrivilege,
        AxSecurityDuty,
        AxSecurityRole,
        AxSecurityPolicy,

        // Services
        AxService,
        AxServiceGroup,

        // Workflow
        AxWorkflowCategory,
        AxWorkflowType,
        AxWorkflowApproval,
        AxWorkflowTask,
        AxWorkflowAutomatedTask,

        // Analytics & Reporting
        AxSsrsReport,
        AxAggregateMeasurement,
        AxAggregateDimension,
        AxKpi,

        // Configuration & Licensing
        AxConfigurationKey,
        AxConfigurationKeyGroup,
        AxLicenseCode,

        // Other
        AxNumberSequenceModule,
        AxResource
    }

    public sealed class XppObjectCreateRequest
    {
        public XppObjectType ObjectType { get; set; }
        public string ObjectName { get; set; }
        public string TargetDirectory { get; set; }
        public bool AddToActiveProject { get; set; } = true;

        /// <summary>
        /// Complete X++ source code for the object (class declaration + methods).
        /// The service wraps this into the proper SourceCode/Declaration CDATA block.
        /// </summary>
        public string SuggestedCode { get; set; }

        /// <summary>
        /// Additional X++ methods as raw CDATA blocks. Each entry is the full source
        /// of one method (including the method signature). The service wraps each one
        /// inside a &lt;Method&gt;&lt;Source&gt;&lt;![CDATA[...]]&gt;&lt;/Source&gt;&lt;/Method&gt; element.
        /// </summary>
        public string[] Methods { get; set; }

        /// <summary>
        /// Raw XML fragment to inject inside the root element AFTER SourceCode.
        /// Use this for metadata that the service does not generate automatically:
        /// table fields, indexes, relations, field groups, form designs, data sources,
        /// enum values, EDT properties, menu item properties, query structure, etc.
        /// The fragment is inserted verbatim — it must be valid XML.
        /// </summary>
        public string MetadataXml { get; set; }

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
