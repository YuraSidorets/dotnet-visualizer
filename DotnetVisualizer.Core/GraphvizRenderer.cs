using DotNetGraph.Compilation;
using DotNetGraph.Core;
using System.Diagnostics;
using System.Text;

namespace DotnetVisualizer.Core;

public static class GraphvizRenderer
{
    public static void WriteDot(DotGraph g, string path)
    {
        using var writer = new StringWriter(new StringBuilder(4096));
        var ctx = new CompilationContext(writer, new CompilationOptions());
        g.CompileAsync(ctx).GetAwaiter().GetResult();
        File.WriteAllText(path, writer.ToString());
    }

    public static void RenderSvg(string dotPath, string svgPath)
    {
        var p = Process.Start(new ProcessStartInfo
        {
            FileName = "dot",
            Arguments = $"-Tsvg \"{dotPath}\" -o \"{svgPath}\"",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new($"Graphviz failed:\n{p.StandardError.ReadToEnd()}");
    }
}