using DotnetVisualizer.Cli;
using System.Reflection;
using Xunit;

namespace DotnetVisualizer.Tests;

public class ProgramUtilityTests
{
    private static object Call(string name, params object[] args) =>
        typeof(Program).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!
                       .Invoke(null, args);

    [Fact]
    public void CompileExcludes_GeneratesCorrectRegex()
    {
        var rxs = (System.Text.RegularExpressions.Regex[])Call("CompileExcludes", "Foo*")!;
        Assert.Single(rxs);
        Assert.True(rxs[0].IsMatch("Foobar"));
        Assert.False(rxs[0].IsMatch("Bar"));
    }

    [Theory]
    [InlineData("out.dot", "out.dot", new[] { "X.csproj" })]
    [InlineData("", "X.dot", new[] { "X.csproj" })]
    public void DetermineOutputPath_PicksExpected(string outputOpt, string expected, string[] roots)
    {
        var cli = new CliOptions { Output = outputOpt, Folder = "", Inputs = roots };
        var path = (string)Call("DetermineOutputPath", cli, roots)!;
        Assert.EndsWith(expected, path);
    }
}