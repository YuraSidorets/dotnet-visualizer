using DotNetGraph.Compilation;
using DotNetGraph.Core;
using System.Diagnostics;
using System.Text;

namespace DotnetVisualizer.Core;

public static class GraphvizRenderer
{
    /// <summary>
    /// Write a <see cref="DotGraph"/> to a *.dot file.
    /// </summary>
    public static async Task WriteDotAsync(DotGraph graph, string path, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using var writer = new StringWriter(new StringBuilder(4096));
        var ctx = new CompilationContext(writer, new CompilationOptions());
        await graph.CompileAsync(ctx);
        await File.WriteAllTextAsync(path, writer.ToString(), ct);
    }

    /// <summary>
    /// Invoke the <c>dot</c> CLI to generate an SVG.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when Graphviz exits with a non‑zero code.</exception>
    public static void RenderSvg(string dotPath, string svgPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dot",
            Arguments = $"-Tsvg \"{dotPath}\" -o \"{svgPath}\"",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var p = Process.Start(psi)!;
        p.WaitForExit();

        if (p.ExitCode != 0)
            throw new InvalidOperationException($"Graphviz failed:{Environment.NewLine}{p.StandardError.ReadToEnd()}");
    }
}