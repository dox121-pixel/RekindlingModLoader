using System;
using System.Collections.Generic;
using System.Linq;

namespace Rekindling.ModLoader
{
    /// <summary>
    /// Turns a flat list of discovered mods into a safe load order, and disables the ones whose
    /// requirements cannot be met.
    /// </summary>
    /// <remarks>
    /// The ordering is a depth-first topological sort. Ties break on mod id so the order is
    /// stable between runs - a mod that only works because of an accidental ordering should
    /// fail the same way every time, not intermittently.
    /// </remarks>
    internal static class DependencyResolver
    {
        /// <summary>
        /// Validates requirements and returns the mods that should load, in load order.
        /// Mods that are disabled or part of a dependency cycle are left in
        /// <paramref name="all"/> with <see cref="LoadedMod.FailureReason"/> set.
        /// </summary>
        public static List<LoadedMod> Resolve(List<LoadedMod> all, Version loaderVersion)
        {
            Dictionary<string, LoadedMod> byId = Index(all);

            RejectLoaderVersionMismatches(all, loaderVersion);
            RejectMissingRequirements(all, byId);

            List<LoadedMod> candidates = all.Where(m => !m.Failed).ToList();
            Dictionary<string, LoadedMod> liveById = Index(candidates);

            return TopologicalSort(candidates, liveById);
        }

        private static Dictionary<string, LoadedMod> Index(IEnumerable<LoadedMod> mods)
        {
            var byId = new Dictionary<string, LoadedMod>(StringComparer.OrdinalIgnoreCase);

            foreach (LoadedMod mod in mods)
            {
                if (byId.TryGetValue(mod.Id, out LoadedMod existing))
                {
                    // Two folders claiming the same id: keep the higher version, disable the other.
                    LoadedMod loser = mod.Manifest.ParsedVersion > existing.Manifest.ParsedVersion ? existing : mod;
                    LoadedMod winner = ReferenceEquals(loser, existing) ? mod : existing;

                    loser.Fail(
                        $"Duplicate mod id '{mod.Id}'. Using version {winner.Manifest.Version} from " +
                        $"'{System.IO.Path.GetFileName(winner.Manifest.Directory)}' instead.");

                    byId[mod.Id] = winner;
                    continue;
                }

                byId[mod.Id] = mod;
            }

            return byId;
        }

        private static void RejectLoaderVersionMismatches(List<LoadedMod> all, Version loaderVersion)
        {
            foreach (LoadedMod mod in all)
            {
                string required = mod.Manifest.MinLoaderVersion;
                if (string.IsNullOrWhiteSpace(required))
                    continue;

                Version needed = ModVersion.Parse(required);
                if (loaderVersion < needed)
                {
                    mod.Fail($"Needs mod loader {needed} or newer; this is {loaderVersion}.");
                }
            }
        }

        private static void RejectMissingRequirements(List<LoadedMod> all, Dictionary<string, LoadedMod> byId)
        {
            // Iterate to a fixed point: disabling one mod can orphan another that depended on it.
            bool changed = true;
            while (changed)
            {
                changed = false;

                foreach (LoadedMod mod in all)
                {
                    if (mod.Failed)
                        continue;

                    foreach (KeyValuePair<string, string> requirement in mod.Manifest.Requires)
                    {
                        if (!byId.TryGetValue(requirement.Key, out LoadedMod dependency) || dependency.Failed)
                        {
                            mod.Fail($"Requires '{requirement.Key}', which is not installed or failed to load.");
                            changed = true;
                            break;
                        }

                        if (!ModVersion.Satisfies(dependency.Manifest.Version, requirement.Value))
                        {
                            mod.Fail(
                                $"Requires '{requirement.Key}' {requirement.Value} or newer, " +
                                $"but version {dependency.Manifest.Version} is installed.");
                            changed = true;
                            break;
                        }
                    }
                }
            }
        }

        private static List<LoadedMod> TopologicalSort(
            List<LoadedMod> mods,
            Dictionary<string, LoadedMod> byId)
        {
            Dictionary<string, HashSet<string>> edges = BuildEdges(mods, byId);

            var sorted = new List<LoadedMod>(mods.Count);
            var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // 0 unvisited, 1 visiting, 2 done
            var path = new Stack<string>();

            foreach (LoadedMod mod in mods.OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase))
                Visit(mod.Id);

            return sorted;

            void Visit(string id)
            {
                if (!byId.TryGetValue(id, out LoadedMod mod) || mod.Failed)
                    return;

                state.TryGetValue(id, out int current);
                if (current == 2)
                    return;

                if (current == 1)
                {
                    // Cycle: disable every mod on it rather than picking an arbitrary winner.
                    var cycle = path.Reverse().SkipWhile(x => !x.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList();
                    cycle.Add(id);
                    string description = string.Join(" -> ", cycle);

                    foreach (string member in cycle)
                    {
                        if (byId.TryGetValue(member, out LoadedMod part))
                            part.Fail($"Circular dependency: {description}");
                    }

                    return;
                }

                state[id] = 1;
                path.Push(id);

                if (edges.TryGetValue(id, out HashSet<string> dependencies))
                {
                    foreach (string dependency in dependencies.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                        Visit(dependency);
                }

                path.Pop();
                state[id] = 2;

                // A mod disabled while walking its own subtree must not be emitted.
                if (!mod.Failed)
                    sorted.Add(mod);
            }
        }

        /// <summary>
        /// Builds "must load after" edges from <c>requires</c>, <c>loadAfter</c> and the
        /// <c>loadBefore</c> of every other mod. Edges pointing at absent mods are dropped.
        /// </summary>
        private static Dictionary<string, HashSet<string>> BuildEdges(
            List<LoadedMod> mods,
            Dictionary<string, LoadedMod> byId)
        {
            var edges = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (LoadedMod mod in mods)
                edges[mod.Id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (LoadedMod mod in mods)
            {
                foreach (string dependency in mod.Manifest.Requires.Keys)
                    AddEdge(mod.Id, dependency);

                foreach (string dependency in mod.Manifest.LoadAfter)
                    AddEdge(mod.Id, dependency);

                // "I load before X" is the same as "X loads after me".
                foreach (string dependent in mod.Manifest.LoadBefore)
                    AddEdge(dependent, mod.Id);
            }

            return edges;

            void AddEdge(string dependent, string dependency)
            {
                if (!byId.ContainsKey(dependent) || !byId.ContainsKey(dependency))
                    return;

                if (dependent.Equals(dependency, StringComparison.OrdinalIgnoreCase))
                    return;

                edges[dependent].Add(dependency);
            }
        }
    }
}
