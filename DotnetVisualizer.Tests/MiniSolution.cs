using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DotnetVisualizer.IntegrationTests;

internal sealed class MiniSolution : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "dv_" + Guid.NewGuid());
    public string A { get; }
    public string B { get; }
    public string C { get; }

    public MiniSolution()
    {
        Directory.CreateDirectory(Root);
        C = CreateProj("C");
        B = CreateProj("B", C);
        A = CreateProj("A", B);
    }

    private string CreateProj(string name, params string[] refs)
    {
        var dir = Path.Combine(Root, name);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{name}.csproj");

        var proj = new XElement("Project",
            new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup",
                new XElement("TargetFramework", "net8.0")));
        if (refs.Any())
        {
            var ig = new XElement("ItemGroup");
            foreach (var r in refs)
                ig.Add(new XElement("ProjectReference", new XAttribute("Include", r)));
            proj.Add(ig);
        }
        proj.Save(path);
        return path;
    }

    public void Dispose()
    { }
}