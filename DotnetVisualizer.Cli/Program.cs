using CommandLine;
using DotnetVisualizer.Core;
using Microsoft.Build.Locator;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

internal class Program
{
    private static string MSBuildAssemblyDir;

    private static async Task Main(string[] args)
    {
        Console.WriteLine("""

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
            var graph = ProjectGraphBuilder.Build(opt.Input, opt.IncludePackages, opt.PackageScope == "direct", opt.EdgeLabel);

            var dotFile = string.IsNullOrWhiteSpace(opt.Output)
                        ? Path.ChangeExtension(opt.Input, ".dot")
                        : opt.Output;

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
}

public sealed class CliOptions
{
    [Option('i', "input", Required = true, HelpText = "Solution (.sln) or project file.")]
    public string Input { get; set; } = "";

    [Option('o', "output", HelpText = "Output .dot (defaults to <input>.dot)")]
    public string? Output { get; set; }

    [Option("packages", Default = false, HelpText = "Include NuGet packages")]
    public bool IncludePackages { get; set; }

    [Option("svg", Default = false, HelpText = "Render SVG via Graphviz")]
    public bool RenderSvg { get; set; }

    [Option("package-scope", Default = "direct", HelpText = "direct (only <PackageReference/>) | all (transitive) . Ignored if --packages is false.")]
    public string PackageScope { get; set; } = "direct";

    [Option("edge-label", Default = false, HelpText = "Write 'PackageReference' or 'Reference' labels on edges.")]
    public bool EdgeLabel { get; set; }
}