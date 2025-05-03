using DotNetGraph.Core;
using DotNetGraph.Extensions;
using DotnetVisualizer.Core;
using DotnetVisualizer.IntegrationTests;
using Microsoft.Build.Locator;
using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace DotnetVisualizer.Tests;

public class ProjectGraphBuilderTests
{
    private static T InvokePrivate<T>(string name, params object[] args) =>
        (T)typeof(ProjectGraphBuilder)
            .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, args)!;

    [Fact]
    public void HandleSelfReference_Highlight_AddsDottedEdge()
    {
        var g = new DotGraph().Directed();
        var n = new DotNode().WithIdentifier("N");
        g.Add(n);

        typeof(ProjectGraphBuilder)
            .GetMethod("HandleSelfReference", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object?[] { SelfReferenceMode.Highlight, n, g, false });

        var e = g.Elements.OfType<DotEdge>().Single();
        Assert.Contains("dotted", e.Style.Value, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(n.Identifier, e.From);
        Assert.Equal(n.Identifier, e.To);
    }

    [Fact]
    public void IsExcluded_RespectsPatterns()
    {
        var rx = new[] { new Regex("^Foo$") };
        Assert.True(InvokePrivate<bool>("IsExcluded", "Foo", rx));
        Assert.False(InvokePrivate<bool>("IsExcluded", "Bar", rx));
    }

    [Fact]
    public void BuildMany_Generates_All_Edges_And_Packages()
    {
        MSBuildLocator.RegisterDefaults();

        using var sln = new MiniSolution();

        var dot = ProjectGraphBuilder.BuildMany(
            new[] { sln.A },
            includePackages: false,
            directPackagesOnly: true,
            edgeLabel: true,
            excludePatterns: Array.Empty<Regex>(),
            collapseMatching: false,
            selfRefMode: SelfReferenceMode.Highlight);

        Assert.Equivalent(
            new[] { "A->B", "B->C" },
            dot.Elements.OfType<DotEdge>().Select(e => $"{e.From.Value}->{e.To.Value}"));

        var edges = dot.Elements.OfType<DotEdge>().ToList();
        Assert.Equal(3, edges.Count);

        var labels = edges.Select(e => e.Label!.Value).Distinct().ToArray();
        Assert.Single(labels);
        Assert.Equal("Reference", labels[0]);
    }
}