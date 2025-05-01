using DotNetGraph.Core;
using DotNetGraph.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace DotnetVisualizer.Core;

public static class DIGraphBuilder
{
    public static DotGraph Build(IServiceCollection services)
    {
        var dot = new DotGraph().WithIdentifier("DotnetVisualizer").Directed();
        var cache = new Dictionary<string, DotNode>();

        DotNode N(string id)
        {
            if (cache.TryGetValue(id, out var n)) return n;
            var nn = new DotNode().WithIdentifier(id).WithShape(DotNodeShape.Box);
            cache[id] = nn;
            dot.Add(nn);
            return nn;
        }

        foreach (var sd in services)
        {
            var serviceId = sd.ServiceType.FullName ?? sd.ServiceType.Name;
            var implId = sd.ImplementationType?.FullName
                         ?? sd.ImplementationInstance?.GetType().FullName
                         ?? "Factory";

            var sNode = N(serviceId);
            var iNode = N(implId);

            // color impl nodes by lifetime
            iNode = sd.Lifetime switch
            {
                ServiceLifetime.Singleton => iNode.WithFillColor(DotColor.LightGreen),
                ServiceLifetime.Scoped => iNode.WithFillColor(DotColor.Gold),
                ServiceLifetime.Transient => iNode.WithFillColor(DotColor.LightPink),
                _ => iNode
            };

            dot.Add(new DotEdge().From(sNode).To(iNode));
        }
        return dot;
    }

    public static IServiceCollection DumpDependencyGraph(this IServiceCollection svc,
                                                         string dotPath,
                                                         bool svg = false)
    {
        var graph = Build(svc);
        GraphvizRenderer.WriteDot(graph, dotPath);

        if (svg)
            GraphvizRenderer.RenderSvg(dotPath, Path.ChangeExtension(dotPath, ".svg"));
        return svc;
    }
}