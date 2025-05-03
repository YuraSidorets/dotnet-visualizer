using CommandLine;
using DotnetVisualizer.Core;
using System;
using System.Collections.Generic;

namespace DotnetVisualizer.Cli;

public sealed class CliOptions
{
    [Value(0, Required = false, HelpText = "One or more .sln / .csproj paths. If omitted, --folder must be supplied.")]
    public IEnumerable<string> Inputs { get; set; } = Array.Empty<string>();

    [Option("folder", HelpText = "Scan folder recursively for *.csproj")]
    public string Folder { get; set; }

    [Option('o', "output", HelpText = "Output .dot (defaults to <input>.dot)")]
    public string Output { get; set; }

    [Option("packages", Default = false, HelpText = "Include NuGet packages")]
    public bool IncludePackages { get; set; }

    [Option("svg", Default = false, HelpText = "Render SVG via Graphviz")]
    public bool RenderSvg { get; set; }

    [Option("package-scope", Default = "direct", HelpText = "direct (only <PackageReference/>) | all (transitive) . Ignored if --packages is false.")]
    public string PackageScope { get; set; } = "direct";

    [Option("edge-label", Default = false, HelpText = "Write 'PackageReference' or 'Reference' labels on edges.")]
    public bool EdgeLabel { get; set; }

    [Option("exclude", HelpText = "Comma-separated substrings. Matching projects or packages are omitted from the graph.")]
    public string Exclude { get; set; }

    [Option("collapse-matching",
    HelpText = "Collapse a NuGet package when its name matches a project " +
               "name; draw a special coloured project => project edge instead.")]
    public bool CollapseMatching { get; set; }

    [Option("self-ref", Default = SelfReferenceMode.Hide, HelpText = "Hide | Show | Highlight")]
    public SelfReferenceMode SelfReferenceMode { get; set; }

    [Option("per-project", HelpText = "With --folder: emit one graph per each .csproj in that folder.")]
    public bool PerProject { get; set; }

    [Option("mermaid", Default = false, HelpText = "Generate a Mermaid .mmd file instead of / in addition to DOT")]
    public bool Mermaid { get; set; }
}