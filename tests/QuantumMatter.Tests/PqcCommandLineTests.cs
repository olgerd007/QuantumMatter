#if NET10_0_OR_GREATER
using QuantumMatter.CryptographyLab;
using QuantumMatter.CryptographyLab.Sessions;
using Xunit;

namespace QuantumMatter.Tests;

public sealed class PqcCommandLineTests
{
    [Theory]
    [InlineData("--unknown")]
    [InlineData("--require")]
    [InlineData("--require", "unknown-algorithm")]
    [InlineData("--repetitions", "0")]
    [InlineData("--repetitions", "1001")]
    [InlineData("--warmup", "-1")]
    [InlineData("--warmup", "101")]
    [InlineData("--output", "--session")]
    public void Invalid_arguments_are_usage_errors(params string[] arguments) =>
        Assert.False(LabCommandLine.TryParse(arguments, out _));

    [Fact]
    public void Requirements_are_canonical_and_enable_the_explicit_optional_algorithm()
    {
        Assert.True(LabCommandLine.TryParse(["--require", "ml-kem-768", "--require", "ML-DSA-65", "--strict", "--session", "--output", "report.json"], out var options));
        Assert.Equal(new[] { "ML-DSA-65", "ML-KEM-768" }, options!.RequiredAlgorithms);
        Assert.True(options.Strict && options.Session);
        Assert.Equal("report.json", options.Output);
        Assert.True(LabCommandLine.TryParse(["--require", "slh-dsa-sha2-128F"], out options));
        Assert.True(options!.IncludeSlhDsa);
    }

    [Fact]
    public void Discovery_strict_and_explicit_requirements_have_distinct_exit_policies()
    {
        var passed = new PrimitiveResult("ML-KEM-768", "passed", "test", 1, 1, 1, "test", []);
        var unsupported = PqcExperiments.Unsupported("ML-DSA-65");
        var report = new LabReport(DateTimeOffset.UnixEpoch, "net10.0", "Release", 1024, 1, 0, "test", [passed, unsupported]);
        Assert.Equal(0, Parse([]).ExitCode(report));
        Assert.Equal(3, Parse(["--strict"]).ExitCode(report));
        Assert.Equal(0, Parse(["--require", "ML-KEM-768"]).ExitCode(report));
        Assert.Equal(3, Parse(["--require", "ML-DSA-65"]).ExitCode(report));
        Assert.Equal(3, Parse(["--require", "ML-KEM-1024"]).ExitCode(report));
        Assert.Equal(3, Parse(["--session"]).ExitCode(report));
        Assert.Equal(0, Parse(["--session"]).ExitCode(report with { Session = new("passed", "test", "test", 2, []) }));
        Assert.Equal(3, Parse(["--session"]).ExitCode(report with { Session = new("unsupported", "test", "test", 0, []) }));
        Assert.Equal(3, Parse(["--strict"]).ExitCode(report with { Primitives = [] }));
        string[] names = ["ML-KEM-512", "ML-KEM-768", "ML-KEM-1024", "ML-DSA-44", "ML-DSA-65", "ML-DSA-87"];
        var complete = report with { Primitives = names.Select(name => passed with { Algorithm = name }).ToArray() };
        Assert.Equal(0, Parse(["--strict"]).ExitCode(complete));
        Assert.Equal(3, Parse(["--strict"]).ExitCode(complete with { Primitives = complete.Primitives.Skip(1).ToArray() }));
        Assert.Equal(3, Parse(["--strict", "--include-slh-dsa"]).ExitCode(complete));
    }

    private static LabCommandLine Parse(string[] arguments)
    {
        Assert.True(LabCommandLine.TryParse(arguments, out var options));
        return options!;
    }
}
#endif
