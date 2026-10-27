using System.Text.Json;
using QuantumMatter.CryptographyLab;
using QuantumMatter.CryptographyLab.Sessions;

if (!LabCommandLine.TryParse(args, out LabCommandLine? parsed)) { return Usage(); }
LabCommandLine options = parsed!;
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
try
{
    LabReport report = PqcExperiments.Run(options.Repetitions, options.Warmup, options.IncludeSlhDsa, cancellation.Token);
    if (options.Session) { report = report with { Session = AuthenticatedSessionExperiment.Run(cancellation.Token) }; }
    string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    if (options.Output is null) { Console.WriteLine(json); }
    else
    {
        try
        {
            string? directory = Path.GetDirectoryName(options.Output);
            if (!string.IsNullOrEmpty(directory)) { Directory.CreateDirectory(directory); }
            await File.WriteAllTextAsync(options.Output, json, cancellation.Token);
        }
        catch (IOException)
        {
            Console.Error.WriteLine("Unable to write the requested report; path details are withheld.");
            return 5;
        }
        catch (UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Unable to write the requested report; path details are withheld.");
            return 5;
        }
        Console.WriteLine($"Report saved to {Path.GetFileName(options.Output)}");
    }
    int exitCode = options.ExitCode(report);
    if (exitCode != 0) { Console.Error.WriteLine("Required PQC validation did not pass; inspect algorithm/session status in the report."); }
    return exitCode;
}
catch (System.Security.Cryptography.CryptographicException)
{
    Console.Error.WriteLine("PQC cryptographic validation failed; no successful report was returned.");
    return 4;
}
catch (InvalidOperationException)
{
    Console.Error.WriteLine("PQC validation failed; no successful report was returned.");
    return 4;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.Error.WriteLine("PQC experiment canceled; no completed report was returned.");
    return 130;
}

static int Usage()
{
    Console.Error.WriteLine("Usage: QuantumMatter.CryptographyLab [--repetitions 1..1000] [--warmup 0..100] [--include-slh-dsa] [--session] [--strict] [--require algorithm] [--output report.json]");
    return 2;
}
