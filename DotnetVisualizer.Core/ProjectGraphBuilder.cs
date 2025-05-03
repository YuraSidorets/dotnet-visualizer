using DotNetGraph.Core;
using DotNetGraph.Extensions;
using Microsoft.Build.Graph;
using NuGet.Common;
using NuGet.ProjectModel;
using System.Text.RegularExpressions;

namespace DotnetVisualizer.Core;

/// <summary>
/// Creates <see cref="DotGraph"/> representations of MSBuild dependency graphs.
/// </summary>
public static class ProjectGraphBuilder
{
    private static readonly DotColor _projectColour = DotColor.DodgerBlue;
    private static readonly DotColor _testColour = DotColor.MediumSeaGreen;
    private static readonly DotColor _packageColour = DotColor.LightGrey;
    private static readonly DotColor _collapseColour = DotColor.BlueViolet;

    /// <summary>
    /// Build a separate sub‑graph (project node and all reachable dependencies) for each supplied root project path.
    /// </summary>
    public static IEnumerable<(string Name, DotGraph Graph)> BuildSubgraphsPerProject(
        IEnumerable<string> roots,
        bool includePackages,
        bool directPackagesOnly,
        bool edgeLabel,
        IReadOnlyList<Regex> excludePatterns,
        bool collapseMatching,
        SelfReferenceMode selfRefMode)
    {
        var fullGraph = BuildMany(
            roots,
            includePackages,
            directPackagesOnly,
            edgeLabel,
            excludePatterns,
            collapseMatching,
            selfRefMode);

        var nodeMap = fullGraph.Elements
            .OfType<DotNode>()
            .ToDictionary(n => n.Identifier.Value, n => n, StringComparer.OrdinalIgnoreCase);

        var edges = fullGraph.Elements.OfType<DotEdge>().ToList();

        var projectRoots = roots.Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var projectPath in projectRoots)
        {
            var rootId = Path.GetFileNameWithoutExtension(projectPath);
            if (!nodeMap.ContainsKey(rootId)) continue;

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootId };
            var queue = new Queue<string>([rootId]);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var currentEdges = edges
                    .Where(e => e.From.Value.Equals(current, StringComparison.OrdinalIgnoreCase));
                foreach (var edge in currentEdges)
                {
                    if (visited.Add(edge.To.Value)) queue.Enqueue(edge.To.Value);
                }
            }

            var sub = new DotGraph()
                .WithIdentifier(rootId)
                .Directed()
                .WithRankDir(DotRankDir.LR);

            foreach (var id in visited.Where(nodeMap.ContainsKey)) sub.Add(nodeMap[id]);
            foreach (var e in edges.Where(e => visited.Contains(e.From.Value) && visited.Contains(e.To.Value)))
                sub.Add(e);

            yield return (rootId, sub);
        }
    }

    /// <summary>
    /// Build a single graph containing every provided root and all their dependencies.
    /// </summary>
    public static DotGraph BuildMany(
        IEnumerable<string> roots,
        bool includePackages,
        bool directPackagesOnly,
        bool edgeLabel,
        IReadOnlyList<Regex> excludePatterns,
        bool collapseMatching,
        SelfReferenceMode selfRefMode)
    {
        var rootPaths = roots.Select(Path.GetFullPath).ToArray();
        var pg = new ProjectGraph(rootPaths);

        var dot = new DotGraph()
            .WithIdentifier("DotnetVisualizer")
            .Directed()
            .WithRankDir(DotRankDir.LR);

        var projectNames = pg.ProjectNodes
            .Select(n => Path.GetFileNameWithoutExtension(n.ProjectInstance.FullPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var nodeCache = new Dictionary<string, DotNode>(StringComparer.OrdinalIgnoreCase);

        DotNode Node(string id, DotNodeShape shape, DotColor? fill = null)
        {
            if (nodeCache.TryGetValue(id, out var cached)) return cached;

            var node = new DotNode().WithIdentifier(id).WithShape(shape);
            if (fill is not null) node.WithFillColor(fill.Value).WithStyle(DotNodeStyle.Filled);
            nodeCache[id] = node;
            dot.Add(node);
            return node;
        }

        foreach (var projNode in pg.ProjectNodes)
        {
            var projId = Path.GetFileNameWithoutExtension(projNode.ProjectInstance.FullPath);
            if (IsExcluded(projId, excludePatterns)) continue;

            var isTest = projId.Contains("Tests", StringComparison.OrdinalIgnoreCase) ||
                         projId.Contains("UnitTests", StringComparison.OrdinalIgnoreCase);
            var nodeProject = Node(projId, DotNodeShape.Box, isTest ? _testColour : _projectColour);

            foreach (var reference in projNode.ProjectReferences)
            {
                var refId = Path.GetFileNameWithoutExtension(reference.ProjectInstance.FullPath);
                if (IsExcluded(refId, excludePatterns)) continue;

                if (refId.Equals(projId, StringComparison.OrdinalIgnoreCase))
                {
                    HandleSelfReference(selfRefMode, nodeProject, dot, edgeLabel);
                    continue;
                }

                var nodeRef = Node(refId, DotNodeShape.Box,
                               refId.Contains("Test", StringComparison.OrdinalIgnoreCase)
                                    ? _testColour : _projectColour);

                var edge = new DotEdge().From(nodeProject).To(nodeRef);
                if (edgeLabel) edge.WithLabel("Reference");
                dot.Add(edge);
            }

            if (includePackages)
            {
                AddPackages(
                    projNode,
                    nodeProject,
                    Node,
                    dot,
                    directPackagesOnly,
                    edgeLabel,
                    excludePatterns,
                    collapseMatching,
                    projectNames
                );
            }
        }

        return dot;
    }

    private static void HandleSelfReference(SelfReferenceMode mode, DotNode node, DotGraph graph, bool edgeLabel)
    {
        switch (mode)
        {
            case SelfReferenceMode.Hide:
                return;

            case SelfReferenceMode.Show:
                var showEdge = new DotEdge().From(node).To(node);
                if (edgeLabel) showEdge.WithLabel("Self Reference");
                graph.Add(showEdge);
                return;

            case SelfReferenceMode.Highlight:
                var hlEdge = new DotEdge()
                    .From(node)
                    .To(node)
                    .WithColor(DotColor.Crimson)
                    .WithStyle(DotEdgeStyle.Dotted)
                    .WithLabel("Self Reference");

                graph.Add(hlEdge);
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }

    private static void AddPackages(
        ProjectGraphNode project,
        DotNode projNode,
        Func<string, DotNodeShape, DotColor?, DotNode> node,
        DotGraph dot,
        bool directOnly,
        bool edgeLabel,
        IReadOnlyList<Regex> excludePatterns,
        bool collapseMatching,
        ISet<string> projectNames)
    {
        var assetsPath = Path.Combine(
            Path.GetDirectoryName(project.ProjectInstance.FullPath)!,
            "obj",
            "project.assets.json"
        );
        if (!File.Exists(assetsPath)) return;

        var lockFile = LockFileUtilities.GetLockFile(assetsPath, NullLogger.Instance);
        if (lockFile is null) return;

        HashSet<string> directRefs = null;
        if (directOnly)
        {
            directRefs = project.ProjectInstance
                .GetItems("PackageReference")
                .Select(i => i.EvaluatedInclude)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var lib in lockFile.Libraries.Where(l => l.Type == "package"))
        {
            if (directOnly && (directRefs is null || !directRefs.Contains(lib.Name))) continue;

            var id = $"{lib.Name}:{lib.Version}";
            if (IsExcluded(lib.Name, excludePatterns) || IsExcluded(id, excludePatterns)) continue;

            if (collapseMatching && projectNames.Contains(lib.Name))
            {
                var targetProject = node(
                    lib.Name,
                    DotNodeShape.Box,
                    projectNames.Contains($"{lib.Name}.Tests") ? _testColour : _projectColour
                );
                var edge = new DotEdge()
                    .From(projNode)
                    .To(targetProject)
                    .WithColor(_collapseColour);

                if (edgeLabel) edge.WithLabel("Pkg => Proj");
                dot.Add(edge);
                continue;
            }

            var pkgNode = node(id, DotNodeShape.Ellipse, _packageColour);
            var pkgEdge = new DotEdge().From(projNode).To(pkgNode);
            if (edgeLabel) pkgEdge.WithLabel("PackageReference");
            dot.Add(pkgEdge);
        }
    }

    private static bool IsExcluded(string id, IEnumerable<Regex> patterns)
        => patterns.Any(r => r.IsMatch(id));
}