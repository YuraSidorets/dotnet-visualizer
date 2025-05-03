using DotNetGraph.Core;
using DotNetGraph.Extensions;
using DotnetVisualizer.Core;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace DotnetVisualizer.Tests;

public class MermaidExporterTests
{
    [Fact]
    public async Task Exports_SimpleGraph_ToValidMermaid()
    {
        var g = new DotGraph().WithIdentifier("t").Directed();
        var a = new DotNode().WithIdentifier("A").WithShape(DotNodeShape.Box);
        var b = new DotNode().WithIdentifier("B").WithShape(DotNodeShape.Ellipse);

        g.Add(a)
         .Add(b)
         .Add(new DotEdge()
            .From(a)
            .To(b)
            .WithStyle(DotEdgeStyle.Dotted)
            .WithLabel("edge")
         );

        var path = Path.GetTempFileName();
        await MermaidExporter.WriteMermaidAsync(g, path);
        var text = await File.ReadAllTextAsync(path);

        Assert.Contains("flowchart LR", text);
        Assert.Contains("A[\"A\"]", text);
        Assert.Contains("B((\"B\"))", text);
        Assert.Contains("A-.->|edge|B", text);
    }
}