using CommandLine;
using CommandLine.Text;
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

    private static Task<int> Main(string[] args)
    {
        Console.WriteLine(Banner);

        MSBuildLocator.RegisterDefaults();
        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        AppDomain.CurrentDomain.AssemblyResolve += ResolveMsBuildAssemblies;

        var parser = new Parser(config =>
        {
            config.CaseInsensitiveEnumValues = true;
            config.AutoVersion = false;
            config.AutoHelp = true;
            config.AutoVersion = false;
        });

        var result = parser.ParseArguments<CliOptions>(args);

        return result
            .MapResult(
                SafeRun,
                errs => ShowHelpAndExit(result, errs));
    }

    private static async Task<int> SafeRun(CliOptions opt)
    {
        try
        {
            await (opt.PerProject ? RunPerProjectAsync(opt) : RunSingleGraphAsync(opt));
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] {0}", ex.Message);
            return 1;
        }
    }

    private static Task<int> ShowHelpAndExit<T>(ParserResult<T> result, IEnumerable<Error> errs)
    {
        var help = HelpText.AutoBuild(result, h =>
        {
            h.AdditionalNewLineAfterOption = false;
            h.Heading = "dotviz – .NET dependency graph generator";
            h.Copyright = "";
            return HelpText.DefaultParsingErrorsHandler(result, h);
        }, _ => _);

        Console.Error.WriteLine(help);
        return Task.FromResult(1);
    }

    private static async Task RunSingleGraphAsync(CliOptions opt)
    {
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("green bold"))
            .StartAsync("Building dependency graph...", _ => BuildSingleAsync(opt));
    }

    private static async Task BuildSingleAsync(CliOptions opt)
    {
        var (roots, excludeRx, includePkgs, directOnly) = ParseOptions(opt);
        var graph = ProjectGraphBuilder.BuildMany(
            roots,
            includePkgs,
            directOnly,
            opt.EdgeLabel,
            excludeRx,
            opt.CollapseMatching,
            opt.SelfReferenceMode);

        var dotFile = DetermineOutputPath(opt, roots);
        if (!opt.Mermaid)
        {
            await GraphvizRenderer.WriteDotAsync(graph, dotFile);
            AnsiConsole.MarkupLine($"[green]✔ DOT written:[/] {dotFile}");
        }
        if (opt.Mermaid)
        {
            var mmd = Path.ChangeExtension(dotFile, ".mmd");
            await MermaidExporter.WriteMermaidAsync(graph, mmd);
            AnsiConsole.MarkupLine($"[green]✔ Mermaid written:[/] {mmd}");
        }
        if (opt.RenderSvg)
        {
            var svg = Path.ChangeExtension(dotFile, ".svg");
            GraphvizRenderer.RenderSvg(dotFile, svg);
            AnsiConsole.MarkupLine($"[green]✔ SVG written:[/] {svg}");
        }
    }

    private static async Task RunPerProjectAsync(CliOptions opt)
    {
        AnsiConsole.MarkupLine("Building dependency graph...");

        var (roots, excludeRx, includePkgs, directOnly) = ParseOptions(opt);
        var subgraphs = ProjectGraphBuilder.BuildSubgraphsPerProject(
            roots,
            includePkgs,
            directOnly,
            opt.EdgeLabel,
            excludeRx,
            opt.CollapseMatching,
            opt.SelfReferenceMode
        ).ToList();

        await AnsiConsole.Progress()
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn(),
            })
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Writing graphs", maxValue: subgraphs.Count);
                foreach (var (name, graph) in subgraphs)
                {
                    var dot = $"{name}.dot";
                    await GraphvizRenderer.WriteDotAsync(graph, dot);
                    if (opt.RenderSvg) GraphvizRenderer.RenderSvg(dot, $"{name}.svg");
                    if (opt.Mermaid)
                        await MermaidExporter.WriteMermaidAsync(graph, $"{name}.mmd");
                    task.Increment(1);
                }
            });
    }

    private static (List<string> roots, Regex[] excludeRx, bool includePkgs, bool directOnly) ParseOptions(CliOptions opt)
    {
        var roots = new List<string>();
        if (opt.Folder is not null)
            roots.AddRange(Directory.EnumerateFiles(opt.Folder, "*.csproj", SearchOption.AllDirectories));
        roots.AddRange(opt.Inputs);
        if (roots.Count == 0)
            throw new ArgumentException("Nothing to analyse: supply paths or --folder.");

        var excludeRx = CompileExcludes(opt.Exclude);
        var includePkgs = opt.IncludePackages;
        var directOnly = !string.Equals(opt.PackageScope, "all", StringComparison.OrdinalIgnoreCase);
        return (roots, excludeRx, includePkgs, directOnly);
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