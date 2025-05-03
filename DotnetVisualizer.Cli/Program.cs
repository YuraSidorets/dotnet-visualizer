using CommandLine;
using DotnetVisualizer.Core;
using Microsoft.Build.Locator;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DotnetVisualizer.Cli;

public static class Program
{
    private static string _msbuildAssemblyDir;

    private static async Task Main(string[] args)
    {
        Console.WriteLine(Banner);

        MSBuildLocator.RegisterDefaults();
        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        AppDomain.CurrentDomain.AssemblyResolve += ResolveMsBuildAssemblies;

        var parser = new Parser(config =>
        {
            config.CaseInsensitiveEnumValues = true;
        });

        await parser
            .ParseArguments<CliOptions>(args)
            .WithParsedAsync(RunAsync);
    }

    private static async Task RunAsync(CliOptions opt)
    {
        await AnsiConsole
            .Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("green bold"))
            .StartAsync("Building dependency graph...", ctx => BuildDependencyGraph(opt, ctx));
    }

    private static async Task BuildDependencyGraph(CliOptions opt, StatusContext statusContext)
    {
        var roots = new List<string>();
        if (opt.Folder is not null)
        {
            roots.AddRange(Directory.EnumerateFiles(opt.Folder, "*.csproj", SearchOption.AllDirectories));
        }
        roots.AddRange(opt.Inputs);

        if (roots.Count == 0) throw new ArgumentException("Nothing to analyse: supply paths or --folder.");

        var excludeRegexes = CompileExcludes(opt.Exclude);
        var includePackages = opt.IncludePackages;
        var directOnly = !string.Equals(opt.PackageScope, "all", StringComparison.OrdinalIgnoreCase);

        if (opt.PerProject)
        {
            await AnsiConsole.Progress()
                .AutoClear(true)
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new SpinnerColumn(),
                })
                .StartAsync(ctx => BuildGraphsPerProject(opt, ctx, roots, excludeRegexes, includePackages, directOnly));
            return;
        }

        var fullGraph = ProjectGraphBuilder.BuildMany(
            roots,
            includePackages,
            directOnly,
            opt.EdgeLabel,
            excludeRegexes,
            opt.CollapseMatching,
            opt.SelfReferenceMode
        );

        var outputDot = DetermineOutputPath(opt, roots);

        if (!opt.Mermaid)
        {
            await GraphvizRenderer.WriteDotAsync(fullGraph, outputDot);
            AnsiConsole.MarkupLine($"[green]✔ DOT written to[/] [underline]{outputDot}[/]");
        }
        if (opt.Mermaid)
        {
            var mmdPath = Path.ChangeExtension(outputDot, ".mmd");
            await MermaidExporter.WriteMermaidAsync(fullGraph, mmdPath);
            AnsiConsole.MarkupLine($"[green]✔ Mermaid written to[/] [underline]{mmdPath}[/]");
        }
        if (opt.RenderSvg)
        {
            var svg = Path.ChangeExtension(outputDot, ".svg");
            GraphvizRenderer.RenderSvg(outputDot, svg);

            AnsiConsole.MarkupLine($"[green]✔ SVG written to[/] [underline]{svg}[/]");
        }
    }

    private static async Task BuildGraphsPerProject(CliOptions opt, ProgressContext ctx, List<string> roots, Regex[] excludeRegexes, bool includePackages, bool directOnly)
    {
        var pg = ProjectGraphBuilder.BuildSubgraphsPerProject(
            roots,
            includePackages,
            directOnly,
            opt.EdgeLabel,
            excludeRegexes,
            opt.CollapseMatching,
            opt.SelfReferenceMode);

        var task = ctx.AddTask("Parsing projects", maxValue: pg.Count());

        foreach (var (name, graph) in pg)
        {
            var dotPath = $"{name}.dot";
            await GraphvizRenderer.WriteDotAsync(graph, dotPath);

            AnsiConsole.MarkupLine($"[green]✔ Graph for {name} written to[/] [underline]{dotPath}[/]");

            if (opt.RenderSvg)
            {
                GraphvizRenderer.RenderSvg(dotPath, $"{name}.svg");
            }

            task.Increment(1);
        }
    }

    private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs e)
    {
        if (Path.GetFileName(e.LoadedAssembly.Location) is "Microsoft.Build.dll")
            _msbuildAssemblyDir = Path.GetDirectoryName(e.LoadedAssembly.Location);
    }

    private static Assembly ResolveMsBuildAssemblies(object sender, ResolveEventArgs args)
    {
        if (_msbuildAssemblyDir is null) return null;
        var file = Path.Combine(_msbuildAssemblyDir, $"{args.Name.Split(',')[0]}.dll");
        return File.Exists(file) ? Assembly.LoadFrom(file) : null;
    }

    private static Regex[] CompileExcludes(string rawPatterns)
    {
        if (string.IsNullOrWhiteSpace(rawPatterns)) return Array.Empty<Regex>();

        return rawPatterns
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => "^" + Regex.Escape(p.Trim())
                                  .Replace(@"\*", ".*")
                                  .Replace(@"\?", ".") + "$")
            .Select(expr => new Regex(expr, RegexOptions.IgnoreCase))
            .ToArray();
    }

    private static string DetermineOutputPath(CliOptions opt, IReadOnlyList<string> roots)
    {
        if (!string.IsNullOrWhiteSpace(opt.Output)) return opt.Output;
        if (opt.Inputs.Any()) return Path.ChangeExtension(roots[0], ".dot");
        if (!string.IsNullOrWhiteSpace(opt.Folder))
        {
            var stem = new DirectoryInfo(opt.Folder).Name;
            return Path.Combine(opt.Folder, $"{stem}.dot");
        }
        return "deps.dot";
    }

    private const string Banner = """

________          __                 __           .__                    .__  .__
\______ \   _____/  |_  ____   _____/  |_   ___  _|__| ________ _______  |  | |__|_______ ___________
 |    |  \ /  _ \   __\/    \_/ __ \   __\  \  \/ /  |/  ___/  |  \__  \ |  | |  \___   // __ \_  __ \
 |    `   (  <_> )  | |   |  \  ___/|  |     \   /|  |\___ \|  |  // __ \|  |_|  |/    /\  ___/|  | \/
/_______  /\____/|__| |___|  /\___  >__|      \_/ |__/____  >____/(____  /____/__/_____ \\___  >__|
        \/                 \/     \/                      \/           \/              \/    \/

""";
}