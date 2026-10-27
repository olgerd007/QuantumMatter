#if NET10_0_OR_GREATER
using System.Diagnostics;
using System.Text.Json;
using QuantumMatter.CryptographyLab;
using Xunit;

namespace QuantumMatter.Tests;

public sealed class PqcCliProcessTests
{
    [Fact]
    public async Task Cli_creates_a_fresh_report_directory_without_printing_host_paths()
    {
        string root = Path.Combine(Path.GetTempPath(), "quantummatter-pqc-" + Guid.NewGuid().ToString("N"));
        string parent = Path.Combine(root, "nested");
        string output = Path.Combine(parent, "report.json");
        string blocker = Path.Combine(root, "blocked");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[]
        {
            "exec", "--runtimeconfig", Path.ChangeExtension(typeof(PqcCliProcessTests).Assembly.Location, ".runtimeconfig.json"),
            typeof(PqcExperiments).Assembly.Location, "--repetitions", "1", "--warmup", "0", "--output", output
        }) { start.ArgumentList.Add(argument); }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start the lab CLI.");
        try
        {
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
            Assert.Equal(string.Empty, await stderr);
            string printed = await stdout;
            Assert.Contains("Report saved to report.json", printed);
            Assert.DoesNotContain(root, printed);
            using JsonDocument report = JsonDocument.Parse(await File.ReadAllTextAsync(output, timeout.Token));
            Assert.Equal(6, report.RootElement.GetProperty("Primitives").GetArrayLength());
            Assert.False(report.RootElement.TryGetProperty("Session", out _));

            await File.WriteAllTextAsync(blocker, "owned test file", timeout.Token);
            start.ArgumentList[^1] = Path.Combine(blocker, "report.json");
            using Process blocked = Process.Start(start) ?? throw new InvalidOperationException("Unable to start the lab CLI.");
            try
            {
                Task<string> blockedOutput = blocked.StandardOutput.ReadToEndAsync(timeout.Token);
                Task<string> blockedError = blocked.StandardError.ReadToEndAsync(timeout.Token);
                await blocked.WaitForExitAsync(timeout.Token);
                Assert.Equal(5, blocked.ExitCode);
                Assert.Equal(string.Empty, await blockedOutput);
                string error = await blockedError;
                Assert.Contains("Unable to write the requested report", error);
                Assert.DoesNotContain(root, error);
            }
            finally
            {
                if (!blocked.HasExited) { blocked.Kill(entireProcessTree: true); await blocked.WaitForExitAsync(CancellationToken.None); }
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                // Cleanup must wait for our terminated child even after the timeout token expires.
                await process.WaitForExitAsync(CancellationToken.None);
            }

            if (File.Exists(output)) { File.Delete(output); }
            if (File.Exists(blocker)) { File.Delete(blocker); }
            if (Directory.Exists(parent)) { Directory.Delete(parent); }
            if (Directory.Exists(root)) { Directory.Delete(root); }
        }
    }
}
#endif
