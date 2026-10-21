using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Dynamics.AX.Metadata.MetaModel;
using Microsoft.Dynamics.Framework.Tools.ProjectSystem;
using Microsoft.VisualStudio.Shell;
using EnvDTE;

namespace XppAiCopilotCompanion.MetaModel
{
    /// <summary>
    /// The active D365FO project in Visual Studio, with the operations the agent tools need
    /// (same surface as the VS Code host's project): items, add, remove, model.
    /// </summary>
    internal sealed class VsXppProject
    {
        private readonly MetaModelBridge _bridge;
        private readonly Project _project;
        private readonly VSProjectNode _node;

        public VsXppProject(MetaModelBridge bridge, Project project)
        {
            _bridge = bridge;
            _project = project;
            ThreadHelper.ThrowIfNotOnUIThread();
            _node = project?.Object as VSProjectNode;
        }

        public string Caption
        {
            get { ThreadHelper.ThrowIfNotOnUIThread(); return _project.Name; }
        }

        public string FullName
        {
            get { ThreadHelper.ThrowIfNotOnUIThread(); return _project.FullName; }
        }

        public ModelInfo GetProjectsModelInfo() => _node?.GetProjectsModelInfo();

        public string ModelName => GetProjectsModelInfo()?.Name;

        /// <summary>Project items with their metadata type, from Visual Studio's view of the project.</summary>
        public List<ProjectItemInfo> GetItems()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var items = new List<ProjectItemInfo>();
            Collect(_project.ProjectItems, items);
            if (items.Count == 0 || items.All(i => i.ObjectType == null))
                return ReadProjectFile();
            return items;
        }

        public bool Contains(string objectType, string objectName) =>
            GetItems().Any(i => string.Equals(i.ObjectType, objectType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(i.Name, objectName, StringComparison.OrdinalIgnoreCase));

        public bool AddItem(string objectType, string objectName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Contains(objectType, objectName)) return false;
            return _bridge.AddToProject(objectType, objectName).Success;
        }

        /// <summary>Removes the item from the project (the object stays in the model), like Exclude From Project.</summary>
        public bool RemoveItem(string objectType, string objectName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var item = Find(_project.ProjectItems, objectType, objectName);
            if (item == null) return false;
            item.Remove();
            return true;
        }

        private static void Collect(ProjectItems source, List<ProjectItemInfo> items)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (source == null) return;
            foreach (ProjectItem item in source)
            {
                if (item == null) continue;
                string path = FullPath(item);
                string type = TypeFromPath(path);
                if (type != null)
                    items.Add(new ProjectItemInfo { Name = item.Name, ObjectType = type, FilePath = path });
                try
                {
                    if (item.ProjectItems != null && item.ProjectItems.Count > 0)
                        Collect(item.ProjectItems, items);
                }
                catch { }
            }
        }

        private static ProjectItem Find(ProjectItems source, string objectType, string objectName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (source == null) return null;
            foreach (ProjectItem item in source)
            {
                if (item == null) continue;
                if (string.Equals(item.Name, objectName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(TypeFromPath(FullPath(item)), objectType, StringComparison.OrdinalIgnoreCase))
                    return item;
                try
                {
                    var nested = item.ProjectItems != null && item.ProjectItems.Count > 0 ? Find(item.ProjectItems, objectType, objectName) : null;
                    if (nested != null) return nested;
                }
                catch { }
            }
            return null;
        }

        private static string FullPath(ProjectItem item)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try { return item.Properties?.Item("FullPath")?.Value as string; }
            catch { return null; }
        }

        // Metadata files live in <package>\<model>\<AxType>\<Name>.xml.
        private static string TypeFromPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) return null;
            string folder = Path.GetFileName(Path.GetDirectoryName(path));
            return folder != null && folder.StartsWith("Ax", StringComparison.Ordinal) ? folder : null;
        }

        private List<ProjectItemInfo> ReadProjectFile()
        {
            var items = new List<ProjectItemInfo>();
            string file = FullName;
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return items;
            XNamespace ns = "http://schemas.microsoft.com/developer/msbuild/2003";
            foreach (var content in XDocument.Load(file).Descendants(ns + "Content"))
            {
                string include = (string)content.Attribute("Include") ?? "";
                int slash = include.IndexOf('\\');
                if (slash <= 0) continue;
                items.Add(new ProjectItemInfo
                {
                    Name = (string)content.Element(ns + "Name") ?? include.Substring(slash + 1),
                    ObjectType = include.Substring(0, slash),
                });
            }
            return items;
        }
    }

    public sealed partial class MetaModelBridge
    {
        private static Dictionary<string, object> Failure(string message) =>
            new Dictionary<string, object> { ["success"] = false, ["message"] = message };

        private static readonly string[] LayerNames = { "SYS", "SYP", "GLS", "GLP", "FPK", "FPP", "SLN", "SLP", "ISV", "ISP", "VAR", "VAP", "CUS", "CUP", "USR", "USP" };

        private VsXppProject GetActiveXppProject()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var project = GetActiveProject();
            return project == null ? null : new VsXppProject(this, project);
        }

        /// <summary>Custom metadata root (where objects of your own models are saved).</summary>
        private static string CustomFolder => DynamicsConfigurationReader.ReadActiveConfiguration().CustomMetadataFolder;

        // ── Project selection (Visual Studio: projects of the open solution) ──

        public string ListProjectsJson(string searchFolder)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var json = new System.Web.Script.Serialization.JavaScriptSerializer();
            var active = GetActiveProject();
            var projects = SolutionProjects().Select(p => new Dictionary<string, object>
            {
                ["name"] = p.Name,
                ["path"] = p.FullName,
                ["model"] = (p.Object as VSProjectNode)?.GetProjectsModelInfo()?.Name ?? "",
                ["active"] = active != null && string.Equals(p.FullName, active.FullName, StringComparison.OrdinalIgnoreCase),
            }).ToList();
            var sb = new System.Text.StringBuilder(projects.Count + " D365FO project(s) in the open solution:\n");
            foreach (var p in projects)
                sb.Append((bool)p["active"] ? "* " : "  ").Append(p["name"]).Append("  model=").Append(p["model"]).Append("  ").Append(p["path"]).Append('\n');
            if (projects.Count == 0)
                sb.Append("Open a solution with a D365FO project, or pass projectFile to add one to the solution.\n");
            return json.Serialize(new Dictionary<string, object> { ["success"] = true, ["message"] = sb.ToString().TrimEnd(), ["projects"] = projects });
        }

        /// <summary>Makes a project the active one: selects it in Solution Explorer (adding it to the solution if needed) and sets it as startup project.</summary>
        public string SelectProjectJson(string projectFile)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var json = new System.Web.Script.Serialization.JavaScriptSerializer();
            if (string.IsNullOrWhiteSpace(projectFile) || !File.Exists(projectFile) || !projectFile.EndsWith(".rnrproj", StringComparison.OrdinalIgnoreCase))
                return json.Serialize(Failure("'" + projectFile + "' is not an existing .rnrproj file. List candidates with xpp_select_project without projectFile."));

            string full = Path.GetFullPath(projectFile);
            var project = SolutionProjects().FirstOrDefault(p => string.Equals(p.FullName, full, StringComparison.OrdinalIgnoreCase))
                ?? Dte?.Solution?.AddFromFile(full);
            if (project == null)
                return json.Serialize(Failure("Could not open " + full + " in the solution."));

            var model = (project.Object as VSProjectNode)?.GetProjectsModelInfo();
            if (model == null)
                return json.Serialize(Failure("Project " + project.Name + " does not target a model that exists in this environment."));

            Dte.Solution.SolutionBuild.StartupProjects = project.UniqueName;
            var hierarchy = Dte.ToolWindows.SolutionExplorer;
            var node = FindHierarchyItem(hierarchy.UIHierarchyItems, project.Name);
            node?.Select(EnvDTE.vsUISelectionType.vsUISelectionTypeSelect);
            InvalidateCaches();

            return json.Serialize(new Dictionary<string, object>
            {
                ["success"] = true,
                ["message"] = "Active project: " + project.Name + " (model " + model.Name + ", " + (IsCustomModel(model.Name) ? "custom" : "NOT custom") + ")\n" + project.FullName,
            });
        }

        private IEnumerable<Project> SolutionProjects()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var result = new List<Project>();
            var projects = Dte?.Solution?.Projects;
            if (projects == null) return result;
            foreach (Project p in projects) AddProjects(p, result);
            return result;
        }

        private static void AddProjects(Project project, List<Project> result)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (project == null) return;
            if (project.Object is VSProjectNode)
            {
                result.Add(project);
                return;
            }
            // Solution folders contain further projects.
            try
            {
                foreach (ProjectItem item in project.ProjectItems)
                    AddProjects(item.SubProject, result);
            }
            catch { }
        }

        private static EnvDTE.UIHierarchyItem FindHierarchyItem(EnvDTE.UIHierarchyItems items, string name)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (items == null) return null;
            foreach (EnvDTE.UIHierarchyItem item in items)
            {
                if (string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)) return item;
                var nested = FindHierarchyItem(item.UIHierarchyItems, name);
                if (nested != null) return nested;
            }
            return null;
        }
    }
}
