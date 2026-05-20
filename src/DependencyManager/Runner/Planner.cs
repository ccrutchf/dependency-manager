using DependencyManager.Config;
using DependencyManager.Util;

namespace DependencyManager.Runner;

public static class Planner
{
    public static ResolvedPlan Plan(ConfigFile config, PlatformInfo platform)
    {
        var resolved = new Dictionary<(ManagerKind, string), ResolvedPackage>();
        var ppas = new List<string>();
        var seenPpas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var aptSources = new Dictionary<string, ResolvedAptSource>(StringComparer.OrdinalIgnoreCase);
        var aptSourceOrder = new List<string>();
        var requirements = new List<ResolvedRequirement>();

        foreach (var (blockName, block) in config.Blocks)
        {
            if (!BlockFilter.Matches(block, platform)) continue;
            Flatten(block.Apt, ManagerKind.Apt, blockName, resolved);
            Flatten(block.Snap, ManagerKind.Snap, blockName, resolved);
            Flatten(block.Flatpak, ManagerKind.Flatpak, blockName, resolved);
            Flatten(block.Deb, ManagerKind.Deb, blockName, resolved);
            Flatten(block.Pip, ManagerKind.Pip, blockName, resolved);
            Flatten(block.Pipx, ManagerKind.Pipx, blockName, resolved);
            Flatten(block.Script, ManagerKind.Script, blockName, resolved);
            Flatten(block.Vscode, ManagerKind.VsCode, blockName, resolved);
            Flatten(block.Cargo, ManagerKind.Cargo, blockName, resolved);
            Flatten(block.Nvm, ManagerKind.Nvm, blockName, resolved);

            if (block.Requires is not null)
            {
                foreach (var name in block.Requires)
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    requirements.Add(new ResolvedRequirement(name, blockName, PathLookup.Exists(name)));
                }
            }

            if (block.AptSources is not null)
            {
                foreach (var (name, source) in block.AptSources)
                {
                    if (!aptSources.ContainsKey(name)) aptSourceOrder.Add(name);
                    aptSources[name] = new ResolvedAptSource(name, source ?? new AptSource(), blockName);
                }
            }

            if (block.Ppas is null) continue;
            foreach (var ppa in block.Ppas)
            {
                if (seenPpas.Add(ppa)) ppas.Add(ppa);
            }
        }

        var resolvedSources = aptSourceOrder.Select(n => aptSources[n]).ToList();
        return new ResolvedPlan(TopoSort(resolved.Values.ToList()), ppas, resolvedSources, requirements);
    }

    private static void Flatten(
        Dictionary<string, PackageSpec>? section,
        ManagerKind kind,
        string blockName,
        Dictionary<(ManagerKind, string), ResolvedPackage> resolved)
    {
        if (section is null) return;
        foreach (var (id, spec) in section)
        {
            resolved[(kind, id)] = new ResolvedPackage(kind, id, spec ?? new PackageSpec(), blockName);
        }
    }

    private static List<ResolvedPackage> TopoSort(List<ResolvedPackage> packages)
    {
        var byName = new Dictionary<string, ResolvedPackage>(StringComparer.OrdinalIgnoreCase);
        foreach (var pkg in packages)
        {
            var name = pkg.Spec.Name ?? pkg.Id;
            byName[name] = pkg;
        }

        var result = new List<ResolvedPackage>(packages.Count);
        var visited = new HashSet<(ManagerKind, string)>();
        var visiting = new HashSet<(ManagerKind, string)>();

        void Visit(ResolvedPackage pkg)
        {
            var key = (pkg.Manager, pkg.Id);
            if (visited.Contains(key)) return;
            if (!visiting.Add(key))
                throw new InvalidOperationException($"Dependency cycle detected at {pkg.Manager}/{pkg.Id}");

            foreach (var depName in pkg.Spec.Dependencies ?? new List<string>())
            {
                if (byName.TryGetValue(depName, out var dep))
                    Visit(dep);
            }

            visiting.Remove(key);
            visited.Add(key);
            result.Add(pkg);
        }

        foreach (var pkg in packages) Visit(pkg);
        return result;
    }
}
