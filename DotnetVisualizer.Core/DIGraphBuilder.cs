using DotNetGraph.Core;
using DotNetGraph.Extensions;
using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;

namespace DotnetVisualizer.Core;

/// <summary>
/// Produces a dependency graph for an <see cref="IServiceCollection"/>.
/// </summary>
public static class DIGraphBuilder
{
    private static readonly DotColor _defaultFill = DotColor.Gray;
    private static readonly DotColor _singleton = DotColor.LightGreen;
    private static readonly DotColor _scoped = DotColor.Gold;
    private static readonly DotColor _transient = DotColor.LightPink;

    /// <summary>
    /// Build a <c>service -> implementation</c> graph, colouring implementation nodes by lifetime.
    /// </summary>
    /// <param name="services">The service collection to analyse.</param>
    /// <param name="excludePatterns">Shell‑style glob patterns to omit (e.g. <c>"Microsoft.*"</c>).</param>
    public static DotGraph Build(IServiceCollection services, params string[] excludePatterns)
    {
        var excludeRegexes = CompilePatterns(excludePatterns);

        var dot = new DotGraph().WithIdentifier("DIServices").Directed().WithRankDir(DotRankDir.LR);
        var cache = new Dictionary<string, DotNode>(StringComparer.OrdinalIgnoreCase);

        DotNode Node(string id, DotColor colour)
        {
            if (cache.TryGetValue(id, out var cached)) return cached;
            var node = new DotNode()
                .WithIdentifier(id)
                .WithShape(DotNodeShape.Box)
                .WithStyle(DotNodeStyle.Filled)
                .WithFillColor(colour);

            cache[id] = node;
            dot.Add(node);
            return node;
        }

        foreach (var sd in services)
        {
            var serviceId = sd.ServiceType.FullName ?? sd.ServiceType.Name;
            if (IsExcluded(serviceId, excludeRegexes)) continue;

            var implId = GetImplementationId(sd);
            if (IsExcluded(implId, excludeRegexes)) continue;

            var serviceNode = Node(serviceId, _defaultFill);
            var colour = GetNodeColor(sd);
            var implementationNode = Node(implId, colour);

            dot.Add(new DotEdge().From(serviceNode).To(implementationNode));
        }

        return dot;
    }

    /// <summary>
    /// Dump a graph for this <see cref="IServiceCollection"/> to disk and optionally render SVG via Graphviz.
    /// </summary>
    public static async Task<IServiceCollection> DumpDependencyGraphAsync(
        this IServiceCollection services,
        string dotPath,
        bool svg = false,
        params string[] excludePatterns)
    {
        var graph = Build(services, excludePatterns);
        await GraphvizRenderer.WriteDotAsync(graph, dotPath);
        if (svg) GraphvizRenderer.RenderSvg(dotPath, Path.ChangeExtension(dotPath, ".svg"));
        return services;
    }

    private static DotColor GetNodeColor(ServiceDescriptor sd) => sd.Lifetime switch
    {
        ServiceLifetime.Singleton => _singleton,
        ServiceLifetime.Scoped => _scoped,
        ServiceLifetime.Transient => _transient,
        _ => _defaultFill
    };

    private static string GetImplementationId(ServiceDescriptor sd) => sd switch
    {
        { ImplementationType: not null } => sd.ImplementationType!.FullName!,
        { ImplementationInstance: not null } => sd.ImplementationInstance!.GetType().FullName!,
        { ImplementationFactory: not null }
            => sd.ImplementationFactory!.Method is var m &&
                m.ReturnType != typeof(object) ?
                    m.ReturnType.FullName! :
                    $"{m.DeclaringType!.FullName}.{m.Name}",
        _ => "<unknown>"
    };

    private static bool IsExcluded(string id, IReadOnlyList<Regex> patterns)
        => patterns.Any(r => r.IsMatch(id));

    private static Regex[] CompilePatterns(IEnumerable<string> patterns)
        => patterns
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => "^" + Regex.Escape(p.Trim())
                                     .Replace("\\*", ".*")
                                     .Replace("\\?", ".") + "$")
            .Select(expr => new Regex(expr, RegexOptions.IgnoreCase))
            .ToArray();
}