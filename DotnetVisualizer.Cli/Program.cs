using CommandLine;
using DotnetVisualizer.Core;
using Microsoft.Build.Locator;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

internal class Program
{
    private static string MSBuildAssemblyDir;

    private static async Task Main(string[] args)
    {
        Console.WriteLine(
"""

________          __                 __           .__                    .__  .__
\______ \   _____/  |_  ____   _____/  |_   ___  _|__| ________ _______  |  | |__|_______ ___________
 |    |  \ /  _ \   __\/    \_/ __ \   __\  \  \/ /  |/  ___/  |  \__  \ |  | |  \___   // __ \_  __ \
 |    `   (  <_> )  | |   |  \  ___/|  |     \   /|  |\___ \|  |  // __ \|  |_|  |/    /\  ___/|  | \/
/_______  /\____/|__| |___|  /\___  >__|      \_/ |__/____  >____/(____  /____/__/_____ \\___  >__|
        \/                 \/     \/                      \/           \/              \/    \/

""");

        MSBuildLocator.RegisterDefaults();

        Thread.GetDomain().AssemblyLoad += RegisterMSBuildAssemblyPath;
        Thread.GetDomain().AssemblyResolve += RedirectMSBuildAssemblies;

        await Parser.Default.ParseArguments<CliOptions>(args)
                  .WithParsedAsync(RunAsync);

        return;

        static async Task RunAsync(CliOptions opt)
        {
            var roots = new List<string>();
            if (opt.Folder is not null)
                roots.AddRange(Directory.EnumerateFiles(opt.Folder, "*.csproj", SearchOption.AllDirectories));

            roots.AddRange(opt.Inputs);

            if (roots.Count == 0)
                throw new ArgumentException("Nothing to analyse: supply paths or --folder.");

            var excludes = (opt.Exclude ?? "")
               .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
               .Select(s => s.Trim())
               .Where(s => s.Length > 0)
               .ToArray();

            var excludeRegexes = CompileExcludes(excludes);

            var graph = ProjectGraphBuilder.BuildMany(
                roots.ToArray(),
                opt.IncludePackages,
                opt.PackageScope.Equals("direct", StringComparison.OrdinalIgnoreCase),
                opt.EdgeLabel,
                excludeRegexes,
                opt.CollapseMatching,
                opt.SelfReferenceMode
            );

            string dotFile;
            if (!string.IsNullOrWhiteSpace(opt.Output))
            {
                dotFile = opt.Output;
            }
            else if (opt.Inputs.Any())
            {
                dotFile = Path.ChangeExtension(opt.Inputs.First(), ".dot");
            }
            else if (!string.IsNullOrWhiteSpace(opt.Folder))
            {
                var stem = new DirectoryInfo(opt.Folder).Name;
                dotFile = Path.Combine(opt.Folder, $"{stem}.dot");
            }
            else
            {
                dotFile = "deps.dot";
            }

            GraphvizRenderer.WriteDot(graph, dotFile);
            Console.WriteLine($"DOT => {dotFile}");

            if (opt.RenderSvg)
            {
                var svg = Path.ChangeExtension(dotFile, ".svg");
                GraphvizRenderer.RenderSvg(dotFile, svg);
                Console.WriteLine($"SVG => {svg}");
            }
            await Task.CompletedTask;
        }

        static void RegisterMSBuildAssemblyPath(object sender, AssemblyLoadEventArgs args)
        {
            var assemblyPath = args.LoadedAssembly.Location;

            if (Path.GetFileName(assemblyPath) == "Microsoft.Build.dll")
                MSBuildAssemblyDir = Path.GetDirectoryName(assemblyPath);
        }

        static Assembly RedirectMSBuildAssemblies(object sender, ResolveEventArgs args)
        {
            if (MSBuildAssemblyDir == null)
                return null;

            try
            {
                var assemblyFilename = $"{args.Name.Split(',')[0]}.dll";
                var potentialAssemblyPath = Path.Combine(MSBuildAssemblyDir, assemblyFilename);

                return Assembly.LoadFrom(potentialAssemblyPath);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    private static Regex[] CompileExcludes(string[]? raw)
    {
        if (raw is null || raw.Length == 0) return Array.Empty<Regex>();

        return raw.Select(p =>
                "^" + Regex.Escape(p)
                           .Replace(@"\*", ".*")
                           .Replace(@"\?", ".") + "$")
                  .Select(expr => new Regex(expr, RegexOptions.IgnoreCase))
                  .ToArray();
    }
}

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
               "name; draw a special coloured project→project edge instead.")]
    public bool CollapseMatching { get; set; }

    [Option("self-ref",
    HelpText =
      "Behavior for project→same-project edges: " +
      "hide (default) | show | highlight",
    Default = "hide")]
    public string SelfReferenceMode { get; set; } = "hide";
}