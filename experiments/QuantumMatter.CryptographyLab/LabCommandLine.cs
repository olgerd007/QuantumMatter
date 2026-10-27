using System.Globalization;

namespace QuantumMatter.CryptographyLab;

internal sealed record LabCommandLine(int Repetitions, int Warmup, bool IncludeSlhDsa, bool Session,
    bool Strict, IReadOnlyList<string> RequiredAlgorithms, string? Output)
{
    private static readonly string[] KnownAlgorithms =
    [
        "ML-KEM-512", "ML-KEM-768", "ML-KEM-1024", "ML-DSA-44", "ML-DSA-65", "ML-DSA-87", "SLH-DSA-SHA2-128f"
    ];

    internal static bool TryParse(string[] args, out LabCommandLine? options)
    {
        int repetitions = 10, warmup = 2;
        bool slh = false, session = false, strict = false;
        string? output = null;
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        options = null;
        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--include-slh-dsa": slh = true; break;
                case "--session": session = true; break;
                case "--strict": strict = true; break;
                case "--require" when index + 1 < args.Length:
                    string requested = args[++index];
                    string? name = KnownAlgorithms.FirstOrDefault(value => value.Equals(requested, StringComparison.OrdinalIgnoreCase));
                    if (name is null) { return false; }
                    required.Add(name);
                    if (name.StartsWith("SLH-DSA", StringComparison.Ordinal)) { slh = true; }
                    break;
                case "--repetitions" when index + 1 < args.Length && int.TryParse(args[index + 1], CultureInfo.InvariantCulture, out int value):
                    repetitions = value; index++; break;
                case "--warmup" when index + 1 < args.Length && int.TryParse(args[index + 1], CultureInfo.InvariantCulture, out int value):
                    warmup = value; index++; break;
                case "--output" when index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal):
                    output = args[++index];
                    if (string.IsNullOrWhiteSpace(output)) { return false; }
                    break;
                default: return false;
            }
        }

        if (repetitions is < 1 or > 1000 || warmup is < 0 or > 100) { return false; }
        options = new(repetitions, warmup, slh, session, strict, required.Order(StringComparer.Ordinal).ToArray(), output);
        return true;
    }

    // Discovery retains v1.7 behavior. Explicit requirements and --session fail closed.
    internal int ExitCode(LabReport report)
    {
        foreach (string algorithm in RequiredAlgorithms)
        {
            if (!report.Primitives.Any(row => row.Algorithm.Equals(algorithm, StringComparison.OrdinalIgnoreCase) && row.Status == "passed")) { return 3; }
        }

        if (Strict)
        {
            foreach (string algorithm in KnownAlgorithms.Take(IncludeSlhDsa ? 7 : 6))
            {
                if (!report.Primitives.Any(row => row.Algorithm == algorithm && row.Status == "passed")) { return 3; }
            }

            if (report.Primitives.Any(row => row.Status != "passed")) { return 3; }
        }
        if (Session && report.Session?.Status != "passed") { return 3; }
        return 0;
    }
}
