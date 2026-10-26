using System.Globalization;
using System.Text.Json;
using QuantumMatter.CryptographyLab;

int repetitions = 10, warmup = 2;
bool includeSlhDsa = false;
string? output = null;
for (int index = 0; index < args.Length; index++)
{
    switch (args[index])
    {
        case "--include-slh-dsa": includeSlhDsa = true; break;
        case "--repetitions" when index + 1 < args.Length && int.TryParse(args[index + 1], CultureInfo.InvariantCulture, out int value):
            repetitions = value; index++; break;
        case "--warmup" when index + 1 < args.Length && int.TryParse(args[index + 1], CultureInfo.InvariantCulture, out int value):
            warmup = value; index++; break;
        case "--output" when index + 1 < args.Length:
            output = args[++index]; break;
        default: return Usage();
    }
}

if (repetitions is < 1 or > 1000 || warmup is < 0 or > 100) { return Usage(); }
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
try
{
    LabReport report = PqcExperiments.Run(repetitions, warmup, includeSlhDsa, cancellation.Token);
    string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    if (output is null) { Console.WriteLine(json); }
    else { await File.WriteAllTextAsync(output, json, cancellation.Token); Console.WriteLine($"Report saved to {Path.GetFileName(output)}"); }
    // An unsupported provider is explicit report data, never a successful test row.
    return 0;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.Error.WriteLine("PQC experiment canceled; no completed report was returned.");
    return 130;
}

static int Usage()
{
    Console.Error.WriteLine("Usage: QuantumMatter.CryptographyLab [--repetitions 1..1000] [--warmup 0..100] [--include-slh-dsa] [--output report.json]");
    return 2;
}
