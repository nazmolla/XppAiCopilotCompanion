using System;
using Microsoft.VisualStudio.Shell;
using XppAiCopilotCompanion.MetaModel;

namespace XppAiCopilotCompanion
{
    /// <summary>
    /// Implements IXppObjectCreationService by routing all creates through
    /// the MetaModelBridge (strongly-typed MetaModel API).
    /// Replaces XppObjectCreationService which wrote raw files to disk.
    /// </summary>
    public sealed class MetaModelObjectCreationService : IXppObjectCreationService
    {
        private readonly IMetaModelBridge _bridge;

        public MetaModelObjectCreationService(IMetaModelBridge bridge)
        {
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        }

        public XppObjectCreateResult CreateObject(XppObjectCreateRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.ObjectName))
                throw new ArgumentException("ObjectName is required", nameof(request));

            ThreadHelper.ThrowIfNotOnUIThread();

            string objectType = MapObjectType(request.ObjectType);

            var bridgeRequest = new CreateObjectRequest
            {
                ObjectType = objectType,
                ObjectName = request.ObjectName.Trim(),
                Declaration = request.SuggestedCode,
                Methods = request.Methods,
                ModelName = null // bridge will use active project's model
            };

            var result = _bridge.CreateObject(bridgeRequest);

            if (!result.Success)
                throw new InvalidOperationException("MetaModel create failed: " + result.Message);

            return new XppObjectCreateResult
            {
                ObjectName = request.ObjectName,
                AddedToProject = true
            };
        }

        private static string MapObjectType(XppObjectType objectType)
        {
            switch (objectType)
            {
                case XppObjectType.AxClass: return "AxClass";
                case XppObjectType.AxTable: return "AxTable";
                case XppObjectType.AxView: return "AxView";
                case XppObjectType.AxDataEntityView: return "AxDataEntityView";
                case XppObjectType.AxMap: return "AxMap";
                case XppObjectType.AxEdt: return "AxEdt";
                case XppObjectType.AxEnum: return "AxEnum";
                case XppObjectType.AxForm: return "AxForm";
                case XppObjectType.AxTile: return "AxTile";
                case XppObjectType.AxMenu: return "AxMenu";
                case XppObjectType.AxMenuItemDisplay: return "AxMenuItemDisplay";
                case XppObjectType.AxMenuItemOutput: return "AxMenuItemOutput";
                case XppObjectType.AxMenuItemAction: return "AxMenuItemAction";
                case XppObjectType.AxQuery: return "AxQuery";
                case XppObjectType.AxSecurityPrivilege: return "AxSecurityPrivilege";
                case XppObjectType.AxSecurityDuty: return "AxSecurityDuty";
                case XppObjectType.AxSecurityRole: return "AxSecurityRole";
                case XppObjectType.AxService: return "AxService";
                case XppObjectType.AxServiceGroup: return "AxServiceGroup";
                case XppObjectType.AxConfigurationKey: return "AxConfigurationKey";

                // Extension types
                case XppObjectType.AxTableExtension: return "AxTableExtension";
                case XppObjectType.AxFormExtension: return "AxFormExtension";
                case XppObjectType.AxEnumExtension: return "AxEnumExtension";
                case XppObjectType.AxEdtExtension: return "AxEdtExtension";
                case XppObjectType.AxViewExtension: return "AxViewExtension";
                case XppObjectType.AxMenuExtension: return "AxMenuExtension";
                case XppObjectType.AxMenuItemDisplayExtension: return "AxMenuItemDisplayExtension";
                case XppObjectType.AxMenuItemOutputExtension: return "AxMenuItemOutputExtension";
                case XppObjectType.AxMenuItemActionExtension: return "AxMenuItemActionExtension";
                case XppObjectType.AxQuerySimpleExtension: return "AxQuerySimpleExtension";
                case XppObjectType.AxSecurityDutyExtension: return "AxSecurityDutyExtension";
                case XppObjectType.AxSecurityRoleExtension: return "AxSecurityRoleExtension";

                default:
                    throw new NotSupportedException(
                        "Object type '" + objectType + "' is not supported through the MetaModel API. "
                        + "Extension types and other specialized types must be created through the D365FO designer.");
            }
        }
    }
}
