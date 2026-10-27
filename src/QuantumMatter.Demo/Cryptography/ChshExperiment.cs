using QuantumMatter.Core;
using QuantumMatter.Core.Noise;

namespace QuantumMatter.Demo.Cryptography;

internal sealed class ChshExperiment(IStateVectorBackend backend, INoiseChannelBackend noiseBackend,
    long memoryBudgetBytes = StateVector.DefaultMemoryBudgetBytes)
{
    internal ChshResult Run(int shotsPerSetting = 10_000, double depolarization = 0, int seed = 42,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(noiseBackend);
        if (shotsPerSetting is < 1 or > 1_000_000) { throw new ArgumentOutOfRangeException(nameof(shotsPerSetting)); }
        Bb84Experiment.ValidateProbability(depolarization, nameof(depolarization));
        cancellationToken.ThrowIfCancellationRequested();
        var state = new StateVector(2, memoryBudgetBytes);
        var random = new Random(seed);
        double statistic = 0, variance = 0;
        for (int alice = 0; alice < 2; alice++)
        {
            for (int bob = 0; bob < 2; bob++)
            {
                NoisyCircuit circuit = CreateSetting(alice, bob, depolarization);
                int signedSum = 0;
                for (int shot = 0; shot < shotsPerSetting; shot++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    state.Reset(cancellationToken);
                    state.EvolveTrajectory(circuit, backend, noiseBackend, random, cancellationToken);
                    int outcome = state.MeasureAll(random, cancellationToken);
                    signedSum += ((outcome & 1) == ((outcome >> 1) & 1)) ? 1 : -1;
                }

                double correlation = (double)signedSum / shotsPerSetting;
                statistic += alice == 1 && bob == 1 ? -correlation : correlation;
                variance += (1 - correlation * correlation) / shotsPerSetting;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new ChshResult(statistic, Math.Sqrt(Math.Max(0, variance)), 2 * Math.Sqrt(2) * (1 - depolarization));
    }

    internal static NoisyCircuit CreateSetting(int alice, int bob, double depolarization = 0)
    {
        if (alice is < 0 or > 1) { throw new ArgumentOutOfRangeException(nameof(alice)); }
        if (bob is < 0 or > 1) { throw new ArgumentOutOfRangeException(nameof(bob)); }
        Bb84Experiment.ValidateProbability(depolarization, nameof(depolarization));
        return new NoisyCircuit(2).Append(new QuantumCircuit(2).H(0).CNot(0, 1))
            .ApplyNoise(NoiseChannel.Depolarizing(depolarization), 1)
            .Append(new QuantumCircuit(2).Ry(0, -alice * Math.PI / 2).Ry(1, bob == 0 ? -Math.PI / 4 : Math.PI / 4));
    }
}

internal sealed record ChshResult(double Statistic, double EstimatedStandardError, double AnalyticStatistic);
