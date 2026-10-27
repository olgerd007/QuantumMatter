using QuantumMatter.Application;
using QuantumMatter.Core;
using QuantumMatter.Core.Noise;

namespace QuantumMatter.Demo.Cryptography;

internal static class CryptographyExamples
{
    internal static void RunBb84(IStateVectorBackend backend, INoiseChannelBackend noiseBackend, SimulationOptions options,
        bool noisy, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("BB84 teaching simulation: independent signals; disclosed bits are removed; no secure key is produced.");
        var experiment = new Bb84Experiment(backend, noiseBackend, options.MemoryBudgetBytes);
        double[] interceptions = noisy ? [0, 1] : [0, 0.25, 0.5, 0.75, 1];
        double[] noises = noisy ? [0, 0.1, 0.3, 1] : [0];
        foreach (double f in interceptions)
        {
            foreach (double p in noises)
            {
                Bb84Result result = experiment.Run(100_000, f, p, cancellationToken: cancellationToken);
                double? ideal = result.Z.Qber.HasValue && result.X.Qber.HasValue
                    ? Bb84Experiment.IdealSecretFraction(result.Z.Qber.Value, result.X.Qber.Value) : null;
                Console.WriteLine(FormattableString.Invariant(
                    $"f={f:F2}, p={p:F2}: sifted={result.Sifted}, disclosed={result.Disclosed}, errors={result.Errors}, retained candidates={result.Retained}, QBER={result.Qber:P3}; predicted={(1 - p) * f / 4 + p / 2:P3}"));
                Console.WriteLine(FormattableString.Invariant(
                    $"  Z QBER={result.Z.Qber:P3}, X QBER={result.X.Qber:P3}; idealized asymptotic secret-fraction bound={ideal:F4}"));
                foreach (var (basis, statistics) in new[] { ("Z", result.Z), ("X", result.X) })
                {
                    if (statistics.HoeffdingInterval() is { } interval)
                    {
                        Console.WriteLine(FormattableString.Invariant(
                            $"  {basis} 99% Hoeffding interval: [{interval.Lower:P3}, {interval.Upper:P3}] (independent disclosed bits)."));
                    }
                }
            }
        }

        Console.WriteLine("The bound assumes ideal reconciliation and asymptotic phase-error estimation; it is not a finite-key security claim.");
    }

    internal static void RunChsh(IStateVectorBackend backend, INoiseChannelBackend noiseBackend, SimulationOptions options,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine("Bell/CHSH teaching experiment; this does not certify device-independent security.");
        var experiment = new ChshExperiment(backend, noiseBackend, options.MemoryBudgetBytes);
        foreach (double p in new[] { 0, 0.2, 0.5, 1 })
        {
            ChshResult result = experiment.Run(depolarization: p, cancellationToken: cancellationToken);
            Console.WriteLine(FormattableString.Invariant(
                $"p={p:F2}: S={result.Statistic:F5}, estimated SE={result.EstimatedStandardError:F5}, analytic={result.AnalyticStatistic:F5}; local bound |S|<=2"));
        }
    }

    internal static void RunToyGrover(StateVectorSimulator simulator, CancellationToken cancellationToken = default)
    {
        const int plaintext = 3;
        // The key is used only to generate the public challenge; the oracle gets (P,C).
        int ciphertext = ToyKeySearch.Encrypt(plaintext, 5);
        QuantumCircuit circuit = ToyKeySearch.CreateSearch(plaintext, ciphertext, cancellationToken: cancellationToken);
        SimulationResult result = simulator.Run(circuit, 1024, 42, cancellationToken);
        int key = Enumerable.Range(0, 8).MaxBy(result.FinalState.Probability);
        int matches = Enumerable.Range(0, 8).Count(candidate => ToyKeySearch.Encrypt(plaintext, candidate) == ciphertext);
        Console.WriteLine("Grover with a reversible three-bit toy cipher verifier; the cipher is trivially classically invertible.");
        Console.WriteLine($"Public challenge: P={plaintext}, C={ciphertext}; matching keys={matches}.");
        Console.WriteLine(FormattableString.Invariant(
            $"Most probable key={key}; probability={result.FinalState.Probability(key):F8}; physical qubits={circuit.QubitCount}, gates={circuit.Operations.Count}."));
        Console.WriteLine("Gate count includes cipher computation, comparison, uncomputation and diffusion. No classical speedup is claimed.");
    }

    internal static void RunShor(StateVectorSimulator simulator, CancellationToken cancellationToken = default)
    {
        ShorResult result = ShorExperiment.Run(simulator, cancellationToken: cancellationToken);
        Console.WriteLine("Small Shor experiment: coherent controlled modular multiplication, inverse QFT, then classical continued fractions.");
        Console.WriteLine($"N=15, a=2; physical qubits={result.PhysicalQubits}, gates={result.Gates}; counting register is the low eight bits.");
        foreach (var (outcome, count) in result.CountingOutcomes) { Console.WriteLine($"  y={outcome}: {count}"); }
        Console.WriteLine(result.Factors is { } factors
            ? $"Recovered factors: {factors.First} * {factors.Second} = 15."
            : "No nontrivial factors recovered; this is a legitimate sampling/postprocessing failure.");
        Console.WriteLine("Arithmetic synthesis is bounded to odd N<=15. This is not a resource estimate for real RSA/ECC.");
    }
}
