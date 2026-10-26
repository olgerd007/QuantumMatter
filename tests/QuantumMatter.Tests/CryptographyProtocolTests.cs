using System.Numerics;
using QuantumMatter.Application;
using QuantumMatter.Application.Noise;
using QuantumMatter.Core;
using QuantumMatter.Demo.Cryptography;
using QuantumMatter.Infrastructure;
using Xunit;

namespace QuantumMatter.Tests;

public sealed class CryptographyProtocolTests
{
    [Fact]
    public void Every_BB84_setting_matches_independent_measurement_branches_including_phase()
    {
        var backend = new CpuStateVectorBackend();
        var noise = new CpuNoiseChannelBackend();
        foreach (int bit in new[] { 0, 1 })
        {
            foreach (bool alice in new[] { false, true })
            {
                foreach (bool bob in new[] { false, true })
                {
                    foreach (bool? eve in new bool?[] { null, false, true })
                    {
                        foreach (double draw in new[] { 0.25, 0.75 })
                        {
                            // A separate real two-component oracle enumerates the
                            // projective branches; no production circuit/kernel is reused.
                            double[] expected = bit == 0 ? [1, 0] : [0, 1];
                            if (alice) { Hadamard(expected); }
                            int eveBit = 0;
                            if (eve.HasValue)
                            {
                                if (eve.Value) { Hadamard(expected); }
                                eveBit = Project(expected, draw);
                                if (eve.Value) { Hadamard(expected); }
                            }

                            if (bob) { Hadamard(expected); }
                            int bobBit = Project(expected, draw);
                            var actual = new StateVector(1);
                            int memory = actual.EvolveNoisyDynamic(Bb84Experiment.CreateSignal(bit, alice, bob, eve),
                                backend, noise, new FixedRandom(draw));
                            Assert.Equal(eveBit | (bobBit << 1), memory);
                            for (int index = 0; index < 2; index++)
                            {
                                Assert.InRange(Complex.Abs(actual.Amplitude(index) - expected[index]), 0, 1e-12);
                            }
                        }
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 0.4)]
    [InlineData(0.6, 0.2)]
    [InlineData(1, 1)]
    public void Disclosed_QBER_matches_interception_and_noise_prediction(double interception, double noise)
    {
        var experiment = new Bb84Experiment(new CpuStateVectorBackend(), new CpuNoiseChannelBackend());
        Bb84Result result = experiment.Run(80_000, interception, noise, seed: 173, disclosureProbability: 0.5);
        double expected = (1 - noise) * interception / 4 + noise / 2;
        Assert.InRange(result.Sifted, 38_000, 42_000);
        Assert.Equal(result.Sifted, result.Disclosed + result.Retained);
        // Each basis has about 10,000 disclosed independent bits. Hoeffding's
        // bound at epsilon=.03 is < 1.9e-7 per basis when n>=9,000.
        foreach (var basis in new[] { result.Z, result.X })
        {
            Assert.True(basis.Disclosed >= 9_000);
            Assert.InRange(Math.Abs(basis.Qber!.Value - expected), 0, 0.03);
        }

        if (interception == 0 && noise == 0) { Assert.Equal(0, result.Errors); }
    }

    [Fact]
    public void BB84_handles_no_disclosure_and_validates_probability_budget_and_cancellation()
    {
        var experiment = new Bb84Experiment(new CpuStateVectorBackend(), new CpuNoiseChannelBackend());
        Bb84Result result = experiment.Run(100, disclosureProbability: 0);
        Assert.Null(result.Qber);
        Assert.Null(result.Z.HoeffdingInterval());
        Assert.Equal(0, result.Disclosed);
        Assert.Equal(result.Sifted, result.Retained);
        Assert.Equal(1, Bb84Experiment.IdealSecretFraction(0, 0));
        Assert.Equal(0, Bb84Experiment.IdealSecretFraction(0.5, 0.5));
        Assert.InRange(Bb84Experiment.IdealSecretFraction(0.11, 0.11), 0, 0.001);
        var interval = new Bb84BasisStatistics(100, 100, 0).HoeffdingInterval()!.Value;
        Assert.Equal(0, interval.Lower);
        Assert.InRange(interval.Upper, 0.16, 0.17);
        Assert.Throws<ArgumentOutOfRangeException>(() => result.Z.HoeffdingInterval(0));
        foreach (double invalid in new[] { -0.1, 1.1, double.NaN, double.PositiveInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => experiment.Run(1, interception: invalid));
            Assert.Throws<ArgumentOutOfRangeException>(() => experiment.Run(1, depolarization: invalid));
            Assert.Throws<ArgumentOutOfRangeException>(() => experiment.Run(1, disclosureProbability: invalid));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => experiment.Run(Bb84Experiment.MaximumSignals + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Bb84Experiment(new CpuStateVectorBackend(), new CpuNoiseChannelBackend(), 31).Run(1));
        Assert.Throws<OperationCanceledException>(() => experiment.Run(1, cancellationToken: new CancellationToken(true)));
    }

    [Fact]
    public void Every_CHSH_setting_matches_analytic_complex_Bell_amplitudes()
    {
        var simulator = new NoisyStateVectorSimulator(new CpuStateVectorBackend(), new CpuNoiseChannelBackend());
        for (int alice = 0; alice < 2; alice++)
        {
            for (int bob = 0; bob < 2; bob++)
            {
                StateVector state = simulator.SimulateTrajectory(ChshExperiment.CreateSetting(alice, bob));
                double difference = alice * Math.PI / 2 - (bob == 0 ? Math.PI / 4 : -Math.PI / 4);
                double c = Math.Cos(difference / 2) / Math.Sqrt(2), s = Math.Sin(difference / 2) / Math.Sqrt(2);
                // Local rotations on Phi+ give [cos(d/2),-sin(d/2),sin(d/2),cos(d/2)]/sqrt(2).
                double[] expected = [c, -s, s, c];
                for (int index = 0; index < 4; index++)
                {
                    Assert.InRange(Complex.Abs(state.Amplitude(index) - expected[index]), 0, 1e-12);
                }
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.2)]
    [InlineData(1)]
    public void CHSH_counts_follow_one_sided_depolarization(double noise)
    {
        var experiment = new ChshExperiment(new CpuStateVectorBackend(), new CpuNoiseChannelBackend());
        ChshResult result = experiment.Run(20_000, noise, seed: 379);
        // Four bounded independent correlations: a .10 aggregate tolerance
        // allows far more than five aggregate standard errors.
        Assert.InRange(Math.Abs(result.Statistic - 2 * Math.Sqrt(2) * (1 - noise)), 0, 0.10);
        Assert.InRange(result.EstimatedStandardError, 0, 0.015);
        Assert.Throws<OperationCanceledException>(() => experiment.Run(cancellationToken: new CancellationToken(true)));
        Assert.Throws<ArgumentOutOfRangeException>(() => experiment.Run(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => experiment.Run(depolarization: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ChshExperiment(new CpuStateVectorBackend(), new CpuNoiseChannelBackend(), 63).Run(1));
    }

    private static void Hadamard(double[] state)
    {
        (double first, double second) = (state[0], state[1]);
        state[0] = (first + second) / Math.Sqrt(2);
        state[1] = (first - second) / Math.Sqrt(2);
    }

    private static int Project(double[] state, double draw)
    {
        double total = state[0] * state[0] + state[1] * state[1];
        int outcome = draw * total < state[0] * state[0] ? 0 : 1;
        double phase = Math.Sign(state[outcome]);
        state[0] = outcome == 0 ? phase : 0;
        state[1] = outcome == 1 ? phase : 0;
        return outcome;
    }

    private sealed class FixedRandom(double draw) : Random
    {
        public override double NextDouble() => draw;
    }

    [Fact]
    public void Cancellation_inside_a_signal_never_returns_a_successful_partial_run()
    {
        using var cancellation = new CancellationTokenSource();
        var experiment = new Bb84Experiment(new CancellingBackend(cancellation), new CpuNoiseChannelBackend());
        Assert.Throws<OperationCanceledException>(() => experiment.Run(100, cancellationToken: cancellation.Token));
    }

    private sealed class CancellingBackend(CancellationTokenSource cancellation) : IStateVectorBackend
    {
        public void Execute(QuantumCircuit circuit, Memory<Complex> amplitudes, CancellationToken cancellationToken = default)
        {
            new CpuStateVectorBackend().Execute(circuit, amplitudes, cancellationToken);
            cancellation.Cancel();
        }
    }
}
