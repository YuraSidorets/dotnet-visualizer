using DotNetGraph.Core;
using DotNetGraph.Extensions;
using Microsoft.Build.Graph;
using NuGet.Common;
using NuGet.ProjectModel;

namespace DotnetVisualizer.Core;

public static class ProjectGraphBuilder
{
    /// <summary>
    /// Create a DOT graph for a solution or single project.
    /// </summary>
    /// <param name="path">.sln or .csproj</param>
    /// <param name="includePackages">Include NuGet package nodes.</param>
    /// <param name="directPackagesOnly">
    /// true = only the project’s own &lt;PackageReference/&gt;; false = include transitives.
    /// </param>
    /// <param name="edgeLabel">
    /// true = annotate edges with "Reference" (project) or "PackageReference" (package).
    /// </param>
    public static DotGraph Build(string path,
                                 bool includePackages,
                                 bool directPackagesOnly = false,
                                 bool edgeLabel = false)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(path);

        var pg = Path.GetExtension(path) == ".sln"
               ? new ProjectGraph(path)
               : new ProjectGraph(new[] { path });

        var dot = new DotGraph()
                    .WithIdentifier("DotnetVisualizer")
                    .Directed();

        var nodeCache = new Dictionary<string, DotNode>();

        DotNode Node(string id, DotNodeShape shape = DotNodeShape.Box)
        {
            if (nodeCache.TryGetValue(id, out var n)) return n;
            var nn = new DotNode()
                         .WithIdentifier(id)
                         .WithShape(shape);

            nodeCache[id] = nn;
            dot.Add(nn);
            return nn;
        }

        foreach (var p in pg.ProjectNodes)
        {
            var projId = Path.GetFileNameWithoutExtension(p.ProjectInstance.FullPath);
            var projNode = Node(projId);

            foreach (var r in p.ProjectReferences)
            {
                var refId = Path.GetFileNameWithoutExtension(r.ProjectInstance.FullPath);
                var refNode = Node(refId);
                var edge = new DotEdge().From(projNode).To(refNode);
                if (edgeLabel) edge.WithLabel("Reference");
                dot.Add(edge);
            }

            if (includePackages)
                AddPackages(p, projNode, Node, dot, directPackagesOnly, edgeLabel);
        }
        return dot;
    }

    private static void AddPackages(ProjectGraphNode p,
                                    DotNode projNode,
                                    Func<string, DotNodeShape, DotNode> node,
                                    DotGraph dot,
                                    bool directOnly,
                                    bool edgeLabel)
    {
        var objDir = Path.Combine(Path.GetDirectoryName(p.ProjectInstance.FullPath)!, "obj");
        var lockFile = Path.Combine(objDir, "project.assets.json");
        if (!File.Exists(lockFile)) return;

        var assets = LockFileUtilities.GetLockFile(lockFile, NullLogger.Instance);
        if (assets is null) return;

        HashSet<string>? direct = null;
        if (directOnly)
        {
            direct = new HashSet<string>(
            p.ProjectInstance.GetItems("PackageReference")
                                     .Select(i => i.EvaluatedInclude),
            StringComparer.OrdinalIgnoreCase);
        }

        foreach (var lib in assets.Libraries.Where(l => l.Type == "package"))
        {
            if (directOnly && !direct!.Contains(lib.Name))
                continue;                               // skip transitives

            var pkgId = $"{lib.Name}:{lib.Version}";
            var pkgNode = node(pkgId, DotNodeShape.Ellipse)
                            .WithFillColor(DotColor.LightGray);

            var edge = new DotEdge().From(projNode).To(pkgNode);
            if (edgeLabel) edge.WithLabel("PackageReference");
            dot.Add(edge);
        }
    }
}