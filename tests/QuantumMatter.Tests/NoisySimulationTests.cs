using System.Numerics;
using QuantumMatter.Application;
using QuantumMatter.Application.Noise;
using QuantumMatter.Core;
using QuantumMatter.Core.Noise;
using QuantumMatter.Infrastructure;
using Xunit;

namespace QuantumMatter.Tests;

public sealed class NoisySimulationTests
{
    private static NoisyStateVectorSimulator Create(SimulationOptions? options = null) =>
        new(new CpuStateVectorBackend(), new CpuNoiseChannelBackend(), options);

    public static TheoryData<NoiseKind, double> AnalyticCases
    {
        get
        {
            var cases = new TheoryData<NoiseKind, double>();
            foreach (NoiseKind kind in Enum.GetValues<NoiseKind>())
            {
                foreach (double probability in new[] { 0d, 0.2, 0.65, 1 })
                {
                    cases.Add(kind, probability);
                }
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(AnalyticCases))]
    public void Ensemble_estimates_agree_with_analytic_one_qubit_channels_and_report_sample_standard_error(NoiseKind kind, double p)
    {
        var circuit = new NoisyCircuit(1);
        if (kind == NoiseKind.AmplitudeDamping)
        {
            circuit.Append(new QuantumCircuit(1).X(0));
        }
        else if (kind == NoiseKind.PhaseFlip)
        {
            circuit.Append(new QuantumCircuit(1).H(0));
        }

        circuit.ApplyNoise(NoiseChannelTests.Channel(kind, p), 0);
        if (kind == NoiseKind.PhaseFlip)
        {
            circuit.Append(new QuantumCircuit(1).H(0));
        }

        const int trajectories = 20000;
        NoisyProbabilityResult result = Create().EstimateProbabilities(circuit, trajectories, seed: 392);
        double expected = kind == NoiseKind.AmplitudeDamping ? 1 - p : kind == NoiseKind.Depolarizing ? p / 2 : p;
        ProbabilityEstimate estimate = result.Estimate(1);
        // Six worst-case standard deviations, fixed seed; no probabilistic near-equality assertion.
        Assert.InRange(Math.Abs(estimate.Probability - expected), 0, 6 * 0.5 / Math.Sqrt(trajectories));
        Assert.InRange(Math.Abs(result.Estimate(0).Probability + estimate.Probability - 1), 0, 2e-12);
        double expectedError = Math.Sqrt(estimate.Probability * (1 - estimate.Probability) / (trajectories - 1));
        Assert.InRange(Math.Abs(estimate.StandardError - expectedError), 0, 2e-12);
        Assert.Equal(trajectories, result.Trajectories);
        Assert.Equal(1, result.QubitCount);
    }

    [Fact]
    public void Bell_state_damping_preserves_correct_correlations_and_coherence_after_uncomputation()
    {
        var bell = new NoisyCircuit(2).Append(new QuantumCircuit(2).H(0).CNot(0, 1))
            .ApplyNoise(NoiseChannel.AmplitudeDamping(0.4), 1);
        NoisyProbabilityResult result = Create().EstimateProbabilities(bell, 30000, seed: 941);
        double[] expected = [0.5, 0.2, 0, 0.3];
        for (int index = 0; index < 4; index++)
        {
            Assert.InRange(Math.Abs(result.Estimate(index).Probability - expected[index]), 0, 0.02);
        }

        bell.Append(new QuantumCircuit(2).CNot(0, 1).H(0));
        result = Create().EstimateProbabilities(bell, 30000, seed: 941);
        // Fidelity with the original Bell ket after one-qubit damping.
        double fidelity = Math.Pow(1 + Math.Sqrt(0.6), 2) / 4;
        Assert.InRange(Math.Abs(result.Estimate(0).Probability - fidelity), 0, 0.02);
    }

    [Fact]
    public void Each_shot_gets_a_fresh_trajectory_and_seeded_results_are_repeatable_and_read_only()
    {
        var circuit = new NoisyCircuit(1).ApplyNoise(NoiseChannel.BitFlip(0.35), 0);
        NoisyStateVectorSimulator simulator = Create();
        NoisySimulationResult first = simulator.Run(circuit, shots: 20000, seed: 723);
        NoisySimulationResult second = simulator.Run(circuit, shots: 20000, seed: 723);
        Assert.Equal(first.Counts.ToArray(), second.Counts.ToArray());
        Assert.Equal(20000, first.Shots);
        Assert.Equal(first.Shots, first.Counts.Values.Sum());
        Assert.InRange(first.Counts["1"] / (double)first.Shots, 0.33, 0.37);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, int>)first.Counts).Add("x", 1));
        NoisyProbabilityResult a = simulator.EstimateProbabilities(circuit, 1000, seed: 48);
        NoisyProbabilityResult b = simulator.EstimateProbabilities(circuit, 1000, seed: 48);
        Assert.Equal(a.Estimate(1), b.Estimate(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => a.Estimate(2));
        Assert.Equal(simulator.SimulateTrajectory(circuit, seed: 31).Snapshot(), simulator.SimulateTrajectory(circuit, seed: 31).Snapshot());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Zero_noise_equivalence_holds_for_scalar_and_parallel_backends(bool parallel)
    {
        IStateVectorBackend backend = parallel
            ? new ParallelCpuStateVectorBackend(new ParallelCpuOptions { MinimumParallelQubits = 1, MaxDegreeOfParallelism = 3, EnableSimd = true })
            : new CpuStateVectorBackend();
        var circuit = new QuantumCircuit(4).H(0).CNot(0, 3).Ry(1, 0.3).Toffoli(0, 3, 2).CRz(2, 1, -0.8);
        var noisy = new NoisyCircuit(4).Append(circuit);
        foreach (NoiseKind kind in Enum.GetValues<NoiseKind>())
        {
            noisy.ApplyNoise(NoiseChannelTests.Channel(kind, 0), 1);
        }

        var simulator = new NoisyStateVectorSimulator(backend, new CpuNoiseChannelBackend());
        StateVector expected = new StateVectorSimulator(backend).Simulate(circuit);
        Assert.Equal(expected.Snapshot(), simulator.SimulateTrajectory(noisy, seed: 9).Snapshot());
        NoisyProbabilityResult estimate = simulator.EstimateProbabilities(noisy, 2, seed: 9);
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.InRange(Math.Abs(estimate.Estimate(index).Probability - expected.Probability(index)), 0, 2e-12);
            Assert.Equal(0, estimate.Estimate(index).StandardError);
        }

        Assert.Equal(new StateVectorSimulator(backend).Run(circuit, 1000, seed: 9).Counts.ToArray(),
            simulator.Run(noisy, 1000, seed: 9).Counts.ToArray());
    }

    [Fact]
    public void Placement_order_is_explicit_segments_are_snapshots_and_reset_restores_zero_state()
    {
        var source = new QuantumCircuit(2).X(0);
        var circuit = new NoisyCircuit(2).Append(source).ApplyNoise(NoiseChannel.BitFlip(1), 1);
        source.X(0);
        StateVector state = Create().SimulateTrajectory(circuit, seed: 3);
        Assert.Equal(Complex.One, state.Amplitude(3));
        state.Reset();
        Assert.Equal(new Complex[] { 1, 0, 0, 0 }, state.Snapshot());
        state.Evolve(new QuantumCircuit(2).X(1), new CpuStateVectorBackend());
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => state.Reset(canceled.Token));
        Assert.Equal(Complex.One, state.Amplitude(2));
        Assert.Equal(2, circuit.StepCount);

        // Z noise after H is visible to the second H; Z on |0> before H is not.
        var after = new NoisyCircuit(1).Append(new QuantumCircuit(1).H(0))
            .ApplyNoise(NoiseChannel.PhaseFlip(1), 0).Append(new QuantumCircuit(1).H(0));
        var before = new NoisyCircuit(1).ApplyNoise(NoiseChannel.PhaseFlip(1), 0)
            .Append(new QuantumCircuit(1).H(0).H(0));
        Assert.InRange(Create().SimulateTrajectory(after).Probability(1), 1 - 1e-12, 1 + 1e-12);
        Assert.InRange(Create().SimulateTrajectory(before).Probability(0), 1 - 1e-12, 1 + 1e-12);
    }

    [Fact]
    public void Budgets_cover_state_and_both_estimator_arrays_before_allocation()
    {
        var circuit = new NoisyCircuit(1);
        var minimum = Create(new SimulationOptions { MemoryBudgetBytes = 32 });
        Assert.Equal(32, StateVector.RequiredBytes(1));
        Assert.Equal(1, minimum.Run(circuit, 1).Counts["0"]);
        Assert.Equal(Complex.One, minimum.SimulateTrajectory(circuit).Amplitude(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => minimum.EstimateProbabilities(circuit, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(new SimulationOptions { MemoryBudgetBytes = 63 }).EstimateProbabilities(circuit, 2));
        Assert.Equal(new ProbabilityEstimate(1, 0), Create(new SimulationOptions { MemoryBudgetBytes = 64 }).EstimateProbabilities(circuit, 2).Estimate(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create().Run(new NoisyCircuit(30), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create().EstimateProbabilities(new NoisyCircuit(30), 2));
    }

    [Fact]
    public void Invalid_input_is_rejected_without_adding_steps_or_starting_evolution()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NoisyCircuit(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NoisyCircuit(31));
        var circuit = new NoisyCircuit(1);
        Assert.Throws<ArgumentNullException>(() => circuit.Append(null!));
        Assert.Throws<ArgumentException>(() => circuit.Append(new QuantumCircuit(2)));
        Assert.Throws<ArgumentNullException>(() => circuit.ApplyNoise(null!, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => circuit.ApplyNoise(NoiseChannel.BitFlip(0.2), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => circuit.ApplyNoise(NoiseChannel.BitFlip(0.2), -1));
        Assert.Equal(0, circuit.StepCount);
        Assert.Throws<ArgumentNullException>(() => new NoisyStateVectorSimulator(null!, new CpuNoiseChannelBackend()));
        Assert.Throws<ArgumentNullException>(() => new NoisyStateVectorSimulator(new CpuStateVectorBackend(), null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(new SimulationOptions { MemoryBudgetBytes = 0 }));
        Assert.Throws<ArgumentNullException>(() => Create().SimulateTrajectory(null!));
        Assert.Throws<ArgumentNullException>(() => Create().Run(null!));
        Assert.Throws<ArgumentNullException>(() => Create().EstimateProbabilities(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create().Run(circuit, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create().EstimateProbabilities(circuit, 1));
        var state = new StateVector(1);
        Assert.Throws<ArgumentException>(() => state.EvolveTrajectory(new NoisyCircuit(2), new CpuStateVectorBackend(), new CpuNoiseChannelBackend(), new Random()));
        Assert.Throws<ArgumentNullException>(() => state.EvolveTrajectory(circuit, null!, new CpuNoiseChannelBackend(), new Random()));
        Assert.Throws<ArgumentNullException>(() => state.EvolveTrajectory(circuit, new CpuStateVectorBackend(), null!, new Random()));
        Assert.Throws<ArgumentNullException>(() => state.EvolveTrajectory(circuit, new CpuStateVectorBackend(), new CpuNoiseChannelBackend(), null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Cancellation_after_a_channel_returns_no_partial_result(int mode)
    {
        using var source = new CancellationTokenSource();
        var channel = new CancellingNoiseBackend(source);
        var simulator = new NoisyStateVectorSimulator(new CpuStateVectorBackend(), channel);
        var circuit = new NoisyCircuit(1).ApplyNoise(NoiseChannel.BitFlip(0.4), 0);
        OperationCanceledException error = Assert.Throws<OperationCanceledException>(() =>
        {
            if (mode == 0) { simulator.SimulateTrajectory(circuit, seed: 7, source.Token); }
            else if (mode == 1) { simulator.Run(circuit, 100, seed: 7, source.Token); }
            else { simulator.EstimateProbabilities(circuit, 100, seed: 7, source.Token); }
        });
        Assert.Equal(source.Token, error.CancellationToken);
        Assert.Equal(1, channel.Calls);
        Assert.Throws<OperationCanceledException>(() => Create().Run(circuit, 1, cancellationToken: source.Token));
    }

    [Fact]
    public void Independent_calls_can_share_backends_without_sharing_random_state()
    {
        NoisyStateVectorSimulator simulator = Create();
        var circuit = new NoisyCircuit(2).Append(new QuantumCircuit(2).H(0).CNot(0, 1))
            .ApplyNoise(NoiseChannel.AmplitudeDamping(0.3), 0);
        var expected = simulator.Run(circuit, 1000, seed: 71).Counts.ToArray();
        Parallel.For(0, 8, _ => Assert.Equal(expected, simulator.Run(circuit, 1000, seed: 71).Counts.ToArray()));
    }

    private sealed class CancellingNoiseBackend(CancellationTokenSource source) : INoiseChannelBackend
    {
        internal int Calls { get; private set; }
        public void Apply(NoiseChannel channel, int target, Memory<Complex> amplitudes, Random random, CancellationToken cancellationToken = default)
        {
            new CpuNoiseChannelBackend().Apply(channel, target, amplitudes, random, cancellationToken);
            Calls++;
            source.Cancel();
        }
    }
}
