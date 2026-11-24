using System.Text.Json;

namespace EquipmentLibrary.Architecture.Tests;

internal sealed record ModulePolicy(string[] Exports, string[] Imports);

internal sealed record BoundaryPolicy(string CompositionRoot, Dictionary<string, ModulePolicy> Modules)
{
    internal static BoundaryPolicy Load(string root)
    {
        string path = Path.Combine(root, "tests", "Architecture", "module-policy.json");
        return JsonSerializer.Deserialize<BoundaryPolicy>(File.ReadAllText(path), JsonSerializerOptions.Web)
            ?? throw new InvalidDataException("Expected a module policy.");
    }

    internal List<string> Check(IReadOnlyList<ProductionProject> projects)
    {
        var paths = projects.ToDictionary(p => p.Path, StringComparer.Ordinal);
        var assemblies = projects.ToDictionary(p => p.Assembly, StringComparer.Ordinal);
        var diagnostics = new List<string>();
        var graph = projects.ToDictionary(p => p.Assembly, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach ((string module, ModulePolicy policy) in Modules)
        {
            if (!projects.Any(p => ModuleOf(p) == module))
            {
                diagnostics.Add($"Module inventory has no projects for {module}.");
            }

            foreach (string exported in policy.Exports)
            {
                if (!assemblies.TryGetValue(exported, out ProductionProject? project) || ModuleOf(project) != module)
                {
                    diagnostics.Add($"Export {exported} must resolve to a project owned by {module}.");
                }
            }

            foreach (string imported in policy.Imports)
            {
                if (!Modules.Any(pair => pair.Key != module && pair.Value.Exports.Contains(imported, StringComparer.Ordinal)))
                {
                    diagnostics.Add($"Import {imported} for {module} is not another module's published contract.");
                }
            }
        }

        foreach (ProductionProject source in projects)
        {
            string? owner = ModuleOf(source);
            if (source.Path != CompositionRoot && (owner is null || !Modules.ContainsKey(owner)))
            {
                diagnostics.Add($"Unregistered production module or project: {source.Path}.");
            }

            foreach (string path in source.Declared)
            {
                if (paths.TryGetValue(path, out ProductionProject? destination))
                {
                    CheckEdge(source, destination, "project reference", graph, diagnostics);
                }
                else
                {
                    diagnostics.Add($"{source.Path} -> {path}: project reference leaves the production graph.");
                }
            }

            foreach (string assembly in source.Compiled)
            {
                if (assemblies.TryGetValue(assembly, out ProductionProject? destination))
                {
                    CheckEdge(source, destination, "compiled reference", graph, diagnostics);
                }
                else if (assembly.StartsWith("EquipmentLibrary.", StringComparison.Ordinal))
                {
                    diagnostics.Add($"{source.Path} -> {assembly}: compiled reference to an unregistered product assembly.");
                }
                else if ((IsDomain(source) || IsApplication(source)) && (assembly.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
                    assembly.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) || assembly == "Npgsql"))
                {
                    string layer = IsDomain(source) ? "domain" : "application";
                    diagnostics.Add($"{source.Path} -> {assembly}: {layer} cannot depend on infrastructure.");
                }
            }
        }

        CheckCycles(graph, diagnostics);
        return diagnostics;
    }

    private void CheckEdge(ProductionProject source, ProductionProject destination, string kind,
        Dictionary<string, HashSet<string>> graph, List<string> diagnostics)
    {
        graph[source.Assembly].Add(destination.Assembly);
        if (source.Path == CompositionRoot)
        {
            return;
        }

        string? owner = ModuleOf(source);
        string? target = ModuleOf(destination);
        if (owner is null || !Modules.TryGetValue(owner, out ModulePolicy? policy))
        {
            return;
        }

        string? rule = null;
        if (IsDomain(source) && (!IsDomain(destination) || owner != target))
        {
            rule = "domain cannot depend outward";
        }
        else if (IsApplication(source) && owner == target && !IsDomain(destination) &&
            !IsApplication(destination) && !policy.Exports.Contains(destination.Assembly, StringComparer.Ordinal))
        {
            rule = "application cannot depend on a persistence or HTTP adapter";
        }
        else if (owner == target && policy.Exports.Contains(source.Assembly, StringComparer.Ordinal) &&
            !policy.Exports.Contains(destination.Assembly, StringComparer.Ordinal))
        {
            rule = "public contract cannot expose its implementation";
        }
        else if (owner != target && !policy.Imports.Contains(destination.Assembly, StringComparer.Ordinal))
        {
            rule = "use a declared public contract";
        }

        if (rule is not null)
        {
            diagnostics.Add($"{source.Path} -> {destination.Path}: {kind}, {rule}.");
        }
    }

    private static string? ModuleOf(ProductionProject project)
    {
        string[] parts = project.Path.Split('/');
        return parts is ["src", "Modules", var name, ..] ? name : null;
    }

    private static bool IsDomain(ProductionProject project) => project.Assembly.EndsWith(".Domain", StringComparison.Ordinal);

    private static bool IsApplication(ProductionProject project) =>
        project.Assembly.EndsWith(".Application", StringComparison.Ordinal);

    private static void CheckCycles(Dictionary<string, HashSet<string>> graph, List<string> diagnostics)
    {
        var finished = new HashSet<string>(StringComparer.Ordinal);
        var stack = new List<string>();
        foreach (string node in graph.Keys)
        {
            Visit(node);
        }

        void Visit(string node)
        {
            int start = stack.IndexOf(node);
            if (start >= 0)
            {
                diagnostics.Add($"Dependency cycle: {string.Join(" -> ", stack.Skip(start).Append(node))}.");
                return;
            }

            if (!finished.Add(node))
            {
                return;
            }

            stack.Add(node);
            foreach (string target in graph[node])
            {
                Visit(target);
            }

            stack.RemoveAt(stack.Count - 1);
        }
    }
}
