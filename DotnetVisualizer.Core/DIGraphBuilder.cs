using DotNetGraph.Core;
using DotNetGraph.Extensions;
using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;

namespace DotnetVisualizer.Core;

public static class DIGraphBuilder
{
    public static DotGraph Build(
           IServiceCollection services,
           params string[] excludePatterns)
    {
        var excludeRegexes = excludePatterns
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => "^" + Regex.Escape(p)
                                   .Replace("\\*", ".*")
                                   .Replace("\\?", ".")
                                  + "$")
            .Select(pat => new Regex(pat, RegexOptions.IgnoreCase))
            .ToArray();

        var dot = new DotGraph().WithIdentifier("DITree").Directed();
        var cache = new Dictionary<string, DotNode>(StringComparer.OrdinalIgnoreCase);

        DotNode N(string id)
        {
            if (cache.TryGetValue(id, out var n)) return n;
            var nn = new DotNode().WithIdentifier(id)
                                  .WithShape(DotNodeShape.Box)
                                  .WithStyle(DotNodeStyle.Filled)
                                  .WithFillColor("#F0F0F0");
            cache[id] = nn;
            dot.Add(nn);
            return nn;
        }

        foreach (var sd in services)
        {
            var serviceId = sd.ServiceType.FullName ?? sd.ServiceType.Name;

            string implId;
            if (sd.ImplementationType != null)
            {
                implId = sd.ImplementationType.FullName!;
            }
            else if (sd.ImplementationInstance != null)
            {
                implId = sd.ImplementationInstance.GetType().FullName!;
            }
            else if (sd.ImplementationFactory != null)
            {
                var m = sd.ImplementationFactory.Method;
                var rt = m.ReturnType;
                if (rt != typeof(object))
                    implId = rt.FullName!;
                else
                    implId = $"{m.DeclaringType.FullName}.{m.Name}";
            }
            else
            {
                implId = "UnknownFactory";
            }

            if (excludeRegexes.Any(rx => rx.IsMatch(serviceId)))
                continue;
            if (excludeRegexes.Any(rx => rx.IsMatch(implId)))
                continue;

            var sNode = N(serviceId);
            var iNode = N(implId);
            switch (sd.Lifetime)
            {
                case ServiceLifetime.Singleton:
                    iNode.WithFillColor(DotColor.LightGreen);
                    break;

                case ServiceLifetime.Scoped:
                    iNode.WithFillColor(DotColor.Gold);
                    break;

                case ServiceLifetime.Transient:
                    iNode.WithFillColor(DotColor.LightPink);
                    break;
            }

            dot.Add(new DotEdge().From(sNode).To(iNode));
        }

        return dot;
    }

    public static IServiceCollection DumpDependencyGraph(
        this IServiceCollection svc,
        string dotPath,
        bool svg = false,
        params string[] excludePatterns)
    {
        var graph = Build(svc, excludePatterns);
        GraphvizRenderer.WriteDot(graph, dotPath);
        if (svg)
            GraphvizRenderer.RenderSvg(dotPath, Path.ChangeExtension(dotPath, ".svg"));
        return svc;
    }
}