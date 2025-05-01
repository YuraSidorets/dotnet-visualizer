using DotNetGraph.Core;
using DotNetGraph.Extensions;
using Microsoft.Build.Graph;
using NuGet.Common;
using NuGet.ProjectModel;
using System.Text.RegularExpressions;

namespace DotnetVisualizer.Core;

public static class ProjectGraphBuilder
{
    private const string projectColour = "#1E90FF";
    private const string testColour = "#3CB371";
    private const string packageColour = "#D3D3D3";
    private const string collapseColour = "#8A2BE2";

    public static IEnumerable<(string name, DotGraph graph)> BuildSubgraphsPerProject(
            string[] roots,
            bool includePackages,
            bool directPackagesOnly,
            bool edgeLabel,
            Regex[] excludePatterns,
            bool collapseMatching,
            string selfRefMode)
    {
        var fullGraph = BuildMany(
            roots,
            includePackages,
            directPackagesOnly,
            edgeLabel,
            excludePatterns,
            collapseMatching,
            selfRefMode);

        var nodeMap = new Dictionary<string, DotNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in fullGraph.Elements.OfType<DotNode>())
        {
            nodeMap[node.Identifier.Value] = node;
        }
        var edges = fullGraph.Elements.OfType<DotEdge>().ToList();

        foreach (var proj in roots.Distinct())
        {
            var rootId = Path.GetFileNameWithoutExtension(proj);
            if (!nodeMap.ContainsKey(rootId))
                continue; // project not part of graph after excludes

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootId };
            var queue = new Queue<string>();
            queue.Enqueue(rootId);

            while (queue.Count > 0)
            {
                var curr = queue.Dequeue();
                foreach (var e in edges)
                {
                    var from = e.From.Value;
                    var to = e.To.Value;
                    // only follow outgoing edges
                    if (string.Equals(from, curr, StringComparison.OrdinalIgnoreCase)
                        && visited.Add(to))
                    {
                        queue.Enqueue(to);
                    }
                }
            }

            var sub = new DotGraph()
                .WithIdentifier(rootId)
                .Directed()
                .WithRankDir(DotRankDir.LR);

            // Add only the visited nodes
            foreach (var id in visited)
            {
                if (nodeMap.TryGetValue(id, out var n))
                    sub.Add(n);
            }

            // Add edges between visited nodes
            foreach (var e in edges)
            {
                var f = e.From.Value;
                var t = e.To.Value;
                if (visited.Contains(f) && visited.Contains(t))
                    sub.Add(e);
            }

            yield return (rootId, sub);
        }
    }

    public static DotGraph Build(string root,
                                 bool includePackages,
                                 bool directPackagesOnly,
                                 bool edgeLabel,
                                 Regex[] excludeRegexes,
                                 bool collapseMatching,
                                 string selfRefMode)
        => BuildMany(new[] { root }, includePackages, directPackagesOnly, edgeLabel, excludeRegexes, collapseMatching, selfRefMode);

    public static DotGraph BuildMany(string[] roots,
                                     bool includePackages,
                                     bool directPackagesOnly,
                                     bool edgeLabel,
                                     Regex[] excludeRegexes,
                                     bool collapseMatching,
                                     string selfRefMode)
    {
        var paths = roots.Select(Path.GetFullPath).ToArray();

        var pg = new ProjectGraph(paths);

        var dot = new DotGraph()
                    .WithIdentifier("DotnetVisualizer")
                    .Directed()
                    .WithRankDir(DotRankDir.LR);

        var projectColour = ProjectGraphBuilder.projectColour;
        var testColour = ProjectGraphBuilder.testColour;
        var packageColour = ProjectGraphBuilder.packageColour;
        var collapseColour = ProjectGraphBuilder.collapseColour;

        var projectNames = pg.ProjectNodes
                     .Select(n => Path.GetFileNameWithoutExtension(
                                      n.ProjectInstance.FullPath))
                     .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var nodeCache = new Dictionary<string, DotNode>(StringComparer.OrdinalIgnoreCase);

        DotNode Node(string id, DotNodeShape shape = DotNodeShape.Box, string? fill = null)
        {
            if (nodeCache.TryGetValue(id, out var n)) return n;
            var nn = new DotNode().WithIdentifier(id).WithShape(shape);
            if (fill is not null)
                nn.WithFillColor(fill).WithStyle(DotNodeStyle.Filled);
            nodeCache[id] = nn;
            dot.Add(nn);
            return nn;
        }

        foreach (var p in pg.ProjectNodes)
        {
            var projId = Path.GetFileNameWithoutExtension(p.ProjectInstance.FullPath);

            if (IsExcluded(projId, excludeRegexes)) continue;

            var isTest = projId.Contains(".Tests", StringComparison.OrdinalIgnoreCase) ||
                           projId.Contains(".UnitTests", StringComparison.OrdinalIgnoreCase);

            var projNode = Node(projId, DotNodeShape.Box,
                                isTest ? testColour : projectColour);

            // project → project
            foreach (var r in p.ProjectReferences)
            {
                var refId = Path.GetFileNameWithoutExtension(r.ProjectInstance.FullPath);

                if (refId.Equals(projId, StringComparison.OrdinalIgnoreCase))
                {
                    switch (selfRefMode.ToLowerInvariant())
                    {
                        case "hide":
                            continue;
                        case "highlight":
                            {
                                var edge = new DotEdge().From(projNode).To(projNode)
                                                     .WithColor("#DC143C")
                                                     .WithStyle(DotEdgeStyle.Dotted);
                                edge.WithLabel("Self Reference");
                                dot.Add(edge);
                                continue;
                            }
                    }
                }

                if (IsExcluded(refId, excludeRegexes)) continue;

                var refNode = Node(refId, DotNodeShape.Box,
                                   refId.Contains("Test", StringComparison.OrdinalIgnoreCase)
                                            ? testColour : projectColour);

                var e = new DotEdge().From(projNode).To(refNode);
                if (edgeLabel) e.WithLabel("Reference");
                dot.Add(e);
            }

            // packages
            if (includePackages)
                AddPackages(p, projNode, Node, dot,
                            directPackagesOnly, edgeLabel,
                            packageColour, excludeRegexes,
                            collapseMatching, projectNames, collapseColour);
        }

        return dot;
    }

    private static void AddPackages(ProjectGraphNode p,
                                    DotNode projNode,
                                    Func<string, DotNodeShape, string?, DotNode> node,
                                    DotGraph dot,
                                    bool directOnly,
                                    bool edgeLabel,
                                    string packageColour,
                                    Regex[] excludeRegexes,
                                    bool collapseMatching,
                                    ISet<string> projectNames,
                                    string collapseColour)
    {
        var obj = Path.Combine(Path.GetDirectoryName(p.ProjectInstance.FullPath)!, "obj");
        var assetsPath = Path.Combine(obj, "project.assets.json");
        if (!File.Exists(assetsPath)) return;

        var lockFile = LockFileUtilities.GetLockFile(assetsPath, NullLogger.Instance);
        if (lockFile is null) return;

        HashSet<string>? direct = null;
        if (directOnly)
        {
            direct = p.ProjectInstance
                      .GetItems("PackageReference")
                      .Select(i => i.EvaluatedInclude)
                      .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var lib in lockFile.Libraries.Where(l => l.Type == "package"))
        {
            if (directOnly && !direct!.Contains(lib.Name)) continue;

            var id = $"{lib.Name}:{lib.Version}";
            if (IsExcluded(lib.Name, excludeRegexes) || IsExcluded(id, excludeRegexes))
                continue;

            if (collapseMatching && projectNames.Contains(lib.Name))
            {
                var targetProj = node(lib.Name, DotNodeShape.Box,
                                      projectNames.Contains($"{lib.Name}.Tests")
                                           ? testColour : projectColour);

                var e = new DotEdge().From(projNode).To(targetProj)
                                     .WithColor(collapseColour);
                if (edgeLabel) e.WithLabel("Pkg→Proj");
                dot.Add(e);
                continue;                                       // skip package node
            }

            var pkgNode = node(id, DotNodeShape.Ellipse, packageColour);

            var edge = new DotEdge().From(projNode).To(pkgNode);
            if (edgeLabel) edge.WithLabel("PackageReference");
            dot.Add(edge);
        }
    }

    private static bool IsExcluded(string id, IReadOnlyList<Regex> patterns)
        => patterns.Any(r => r.IsMatch(id));
}