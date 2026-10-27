using QuantumMatter.Core;
using QuantumMatter.Core.Dynamic;
using QuantumMatter.Core.Noise;

namespace QuantumMatter.Demo.Cryptography;

// Aggregate-only teaching experiment: no transcript or operational key is retained.
internal sealed class Bb84Experiment(IStateVectorBackend backend, INoiseChannelBackend noiseBackend,
    long memoryBudgetBytes = StateVector.DefaultMemoryBudgetBytes)
{
    internal const int MaximumSignals = 10_000_000;

    internal Bb84Result Run(int signals, double interception = 0, double depolarization = 0,
        int seed = 42, double disclosureProbability = 0.25, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(noiseBackend);
        if (signals is < 1 or > MaximumSignals) { throw new ArgumentOutOfRangeException(nameof(signals)); }
        ValidateProbability(interception, nameof(interception));
        ValidateProbability(depolarization, nameof(depolarization));
        ValidateProbability(disclosureProbability, nameof(disclosureProbability));
        cancellationToken.ThrowIfCancellationRequested();
        var state = new StateVector(1, memoryBudgetBytes);
        var random = new Random(seed);
        var circuits = new NoisyDynamicCircuit[2, 2, 2, 3];
        for (int bit = 0; bit < 2; bit++)
        {
            for (int alice = 0; alice < 2; alice++)
            {
                for (int bob = 0; bob < 2; bob++)
                {
                    for (int eve = 0; eve < 3; eve++)
                    {
                        circuits[bit, alice, bob, eve] = CreateSignal(bit, alice == 1, bob == 1,
                            eve == 0 ? null : eve == 2, depolarization);
                    }
                }
            }
        }

        int[] sifted = new int[2], disclosed = new int[2], errors = new int[2];
        for (int signal = 0; signal < signals; signal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int bit = random.Next(2), alice = random.Next(2), bob = random.Next(2);
            int eve = random.NextDouble() < interception ? random.Next(2) + 1 : 0;
            state.Reset(cancellationToken);
            int memory = state.EvolveNoisyDynamic(circuits[bit, alice, bob, eve], backend, noiseBackend,
                random, cancellationToken);
            if (alice != bob) { continue; }
            sifted[alice]++;
            if (random.NextDouble() < disclosureProbability)
            {
                disclosed[alice]++;
                // Bob is c1; Eve's private c0 is deliberately not used by the protocol.
                if (((memory >> 1) & 1) != bit) { errors[alice]++; }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new Bb84Result(signals, new(sifted[0], disclosed[0], errors[0]), new(sifted[1], disclosed[1], errors[1]));
    }

    internal static NoisyDynamicCircuit CreateSignal(int bit, bool aliceX, bool bobX, bool? eveX,
        double depolarization = 0)
    {
        if (bit is < 0 or > 1) { throw new ArgumentOutOfRangeException(nameof(bit)); }
        ValidateProbability(depolarization, nameof(depolarization));
        var preparation = new QuantumCircuit(1);
        if (bit == 1) { preparation.X(0); }
        if (aliceX) { preparation.H(0); }
        var circuit = new NoisyDynamicCircuit(1, 2).Append(preparation);
        if (eveX.HasValue)
        {
            if (eveX.Value) { circuit.Append(new QuantumCircuit(1).H(0)); }
            circuit.Measure([0], [0]);
            if (eveX.Value) { circuit.Append(new QuantumCircuit(1).H(0)); }
        }

        circuit.ApplyNoise(NoiseChannel.Depolarizing(depolarization), 0);
        if (bobX) { circuit.Append(new QuantumCircuit(1).H(0)); }
        return circuit.Measure([0], [1]);
    }

    internal static double IdealSecretFraction(double zError, double xError)
    {
        ValidateProbability(zError, nameof(zError));
        ValidateProbability(xError, nameof(xError));
        return Math.Max(0, 1 - Entropy(zError) - Entropy(xError));
    }

    private static double Entropy(double p) => p is 0 or 1 ? 0 : -p * Math.Log2(p) - (1 - p) * Math.Log2(1 - p);

    internal static void ValidateProbability(double p, string name)
    {
        if (!double.IsFinite(p) || p is < 0 or > 1) { throw new ArgumentOutOfRangeException(name); }
    }
}

internal sealed record Bb84BasisStatistics(int Sifted, int Disclosed, int Errors)
{
    internal int Retained => Sifted - Disclosed;
    internal double? Qber => Disclosed == 0 ? null : (double)Errors / Disclosed;

    internal (double Lower, double Upper)? HoeffdingInterval(double failureProbability = 0.01)
    {
        if (!double.IsFinite(failureProbability) || failureProbability is <= 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(failureProbability));
        }

        if (!Qber.HasValue) { return null; }
        double radius = Math.Sqrt(Math.Log(2 / failureProbability) / (2 * Disclosed));
        return (Math.Max(0, Qber.Value - radius), Math.Min(1, Qber.Value + radius));
    }
}

internal sealed record Bb84Result(int Signals, Bb84BasisStatistics Z, Bb84BasisStatistics X)
{
    internal int Sifted => Z.Sifted + X.Sifted;
    internal int Disclosed => Z.Disclosed + X.Disclosed;
    internal int Errors => Z.Errors + X.Errors;
    internal int Retained => Z.Retained + X.Retained;
    internal double? Qber => Disclosed == 0 ? null : (double)Errors / Disclosed;
}
