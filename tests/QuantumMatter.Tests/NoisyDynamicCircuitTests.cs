using System.Numerics;
using QuantumMatter.Application;
using QuantumMatter.Application.Dynamic;
using QuantumMatter.Application.Noise;
using QuantumMatter.Core;
using QuantumMatter.Core.Dynamic;
using QuantumMatter.Core.Noise;
using QuantumMatter.Infrastructure;
using Xunit;

namespace QuantumMatter.Tests;

public sealed class NoisyDynamicCircuitTests
{
    private static NoisyDynamicStateVectorSimulator Create(SimulationOptions? options = null) =>
        new(new CpuStateVectorBackend(), new CpuNoiseChannelBackend(), options);

    [Fact]
    public void Classical_history_survives_noise_boundaries_and_overwrites_clear_high_bits()
    {
        const int high = 1 << 29;
        var circuit = new NoisyDynamicCircuit(2, 30).Append(new QuantumCircuit(2).X(0))
            .Measure([0], [29]).ApplyNoise(NoiseChannel.BitFlip(1), 0)
            .If(high, high, new QuantumCircuit(2).X(1)).Measure([1], [0])
            .ApplyNoise(NoiseChannel.PhaseFlip(1), 1).Reset(0).Measure([0], [29])
            .If(1, 1, new QuantumCircuit(2).X(0));
        DynamicTrajectoryResult result = Create().SimulateTrajectory(circuit, 3);
        Assert.Equal(1, result.ClassicalOutcome);
        Assert.Equal(-Complex.One, result.FinalState.Amplitude(3));
        Assert.Equal(9, circuit.StepCount);
        Assert.Equal(30, result.ClassicalBitCount);
        Assert.Equal(8, Create().Run(circuit, 8, 3).Counts[new string('0', 29) + "1"]);
    }

    [Fact]
    public void Zero_noise_preserves_exact_dynamic_states_counts_and_random_draws()
    {
        var preparation = new QuantumCircuit(2).H(0).S(0).H(1);
        var correction = new QuantumCircuit(2).Ry(1, 0.7).CNot(1, 0);
        var clean = new DynamicCircuit(2, 2).Append(preparation).Measure([0], [1])
            .If(2, 2, correction).Reset(0).Measure([1], [0]);
        var noisy = new NoisyDynamicCircuit(2, 2).ApplyNoise(NoiseChannel.BitFlip(0), 0)
            .Append(preparation).Measure([0], [1]).ApplyNoise(NoiseChannel.PhaseFlip(0), 1)
            .If(2, 2, correction).ApplyNoise(NoiseChannel.Depolarizing(0), 0)
            .Reset(0).Measure([1], [0]).ApplyNoise(NoiseChannel.AmplitudeDamping(0), 1);
        var expected = new StateVector(2);
        var actual = new StateVector(2);
        var cleanRandom = new CountingRandom(129);
        var noisyRandom = new CountingRandom(129);
        int cleanOutcome = expected.EvolveDynamic(clean, new CpuStateVectorBackend(), cleanRandom);
        int noisyOutcome = actual.EvolveNoisyDynamic(noisy, new CpuStateVectorBackend(), new ThrowingNoise(), noisyRandom);
        Assert.Equal(cleanOutcome, noisyOutcome);
        Assert.Equal(expected.Snapshot(), actual.Snapshot());
        Assert.Equal(3, noisyRandom.Draws);
        Assert.Equal(cleanRandom.Draws, noisyRandom.Draws);
        var simulator = new NoisyDynamicStateVectorSimulator(new CpuStateVectorBackend(), new ThrowingNoise());
        Assert.Equal(new DynamicStateVectorSimulator(new CpuStateVectorBackend()).Run(clean, 1000, 29).Counts.ToArray(),
            simulator.Run(noisy, 1000, 29).Counts.ToArray());
    }

    [Fact]
    public void Explicit_full_readout_matches_existing_noisy_workflow_without_changing_its_contract()
    {
        var preparation = new QuantumCircuit(2).H(0).CNot(0, 1).S(1);
        var final = new QuantumCircuit(2).H(0).Ry(1, 0.6);
        var old = new NoisyCircuit(2).Append(preparation).ApplyNoise(NoiseChannel.AmplitudeDamping(0.4), 1)
            .ApplyNoise(NoiseChannel.Depolarizing(0.3), 0).Append(final);
        var combined = new NoisyDynamicCircuit(2, 2).Append(preparation).ApplyNoise(NoiseChannel.AmplitudeDamping(0.4), 1)
            .ApplyNoise(NoiseChannel.Depolarizing(0.3), 0).Append(final).Measure([0, 1], [0, 1]);
        var expected = new NoisyStateVectorSimulator(new CpuStateVectorBackend(), new CpuNoiseChannelBackend()).Run(old, 2000, 921);
        Assert.Equal(expected.Counts.ToArray(), Create().Run(combined, 2000, 921).Counts.ToArray());
    }

    [Fact]
    public void Independent_shots_start_with_fresh_quantum_and_classical_state_and_have_immutable_counts()
    {
        var circuit = new NoisyDynamicCircuit(1, 1).ApplyNoise(NoiseChannel.BitFlip(1), 0).Measure([0], [0]);
        var counts = Create().Run(circuit, 20, 71);
        Assert.Equal(20, counts.Shots);
        Assert.Equal(1, counts.ClassicalBitCount);
        Assert.Equal(20, counts.Counts["1"]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, int>)counts.Counts).Add("0", 1));
        var state = new StateVector(1);
        var conditional = new NoisyDynamicCircuit(1, 1).If(1, 0, new QuantumCircuit(1).X(0))
            .ApplyNoise(NoiseChannel.BitFlip(0), 0).Measure([0], [0]);
        Assert.Equal(1, state.EvolveNoisyDynamic(conditional, new CpuStateVectorBackend(), new ThrowingNoise(), new Random(1)));
        Assert.Equal(0, state.EvolveNoisyDynamic(conditional, new CpuStateVectorBackend(), new ThrowingNoise(), new Random(1)));
    }

    [Fact]
    public void Empty_and_noise_only_programs_have_no_implicit_measurement_and_trajectories_own_their_state()
    {
        var empty = new NoisyDynamicCircuit(1, 3);
        var state = new StateVector(1);
        Assert.Equal(0, state.EvolveNoisyDynamic(empty, new ThrowingUnitary(), new ThrowingNoise(), new ThrowingRandom()));
        Assert.Equal(5, Create().Run(empty, 5).Counts["000"]);
        var circuit = new NoisyDynamicCircuit(1, 3).Append(new QuantumCircuit(1).H(0)).ApplyNoise(NoiseChannel.PhaseFlip(1), 0);
        DynamicTrajectoryResult first = Create().SimulateTrajectory(circuit, 1);
        DynamicTrajectoryResult second = Create().SimulateTrajectory(circuit, 1);
        Assert.Equal(0, first.ClassicalOutcome);
        Assert.InRange(Complex.Abs(first.FinalState.Amplitude(1) + Math.Sqrt(0.5)), 0, 1e-12);
        first.FinalState.Reset();
        Assert.InRange(Math.Abs(second.FinalState.Probability(1) - 0.5), 0, 1e-12);
        Assert.Equal(9, Create().Run(circuit, 9, 1).Counts["000"]);
        var sampled = new NoisyDynamicCircuit(1, 1).ApplyNoise(NoiseChannel.BitFlip(0.5), 0).Measure([0], [0]);
        var one = Create().Run(sampled, 4000, 928);
        Assert.Equal(one.Counts.ToArray(), Create().Run(sampled, 4000, 928).Counts.ToArray());
        Assert.All(one.Counts.Values, count => Assert.InRange(count, 1750, 2250));
        Assert.Equal(4000, one.Counts.Values.Sum());
    }

    [Theory]
    [InlineData("scalar")]
    [InlineData("parallel")]
    [InlineData("simd")]
#if CUDA_TESTS
    [InlineData("cuda")]
#endif
    public void Noise_feedback_and_reset_preserve_complex_spectators_on_each_backend(string name)
    {
        IStateVectorBackend backend = Backend(name);
        using IDisposable? owned = backend as IDisposable;
        var program = new NoisyDynamicCircuit(2, 2).Append(new QuantumCircuit(2).X(0).H(1).S(1))
            .ApplyNoise(NoiseChannel.AmplitudeDamping(1), 0).Measure([0], [1])
            .If(2, 0, new QuantumCircuit(2).X(0)).ApplyNoise(NoiseChannel.PhaseFlip(1), 1)
            .Reset(0).Measure([0], [0]);
        var simulator = new NoisyDynamicStateVectorSimulator(backend, new CpuNoiseChannelBackend());
        DynamicTrajectoryResult result = simulator.SimulateTrajectory(program, 92);
        Assert.Equal(0, result.ClassicalOutcome);
        Complex[] expected = [Math.Sqrt(0.5), 0, -Complex.ImaginaryOne * Math.Sqrt(0.5), 0];
        AssertAmplitudes(expected, result.FinalState.Snapshot());
        Assert.Equal(8, simulator.Run(program, 8, 92).Counts["00"]);
    }

    [Fact]
    public void Builders_snapshot_sources_and_validate_before_mutating_across_noise_boundaries()
    {
        foreach (int invalid in new[] { -1, 0, 31, int.MaxValue })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new NoisyDynamicCircuit(invalid, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NoisyDynamicCircuit(1, invalid));
        }

        var prep = new QuantumCircuit(2).X(0);
        var correction = new QuantumCircuit(2).X(1);
        int[] qubits = [0];
        int[] bits = [1];
        var program = new NoisyDynamicCircuit(2, 2).Append(prep).Measure(qubits, bits)
            .ApplyNoise(NoiseChannel.BitFlip(1), 0).If(2, 2, correction);
        prep.X(0);
        correction.X(1);
        qubits[0] = 1;
        bits[0] = 0;
        Assert.Throws<ArgumentNullException>(() => program.ApplyNoise(null!, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => program.ApplyNoise(NoiseChannel.BitFlip(0.1), -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => program.ApplyNoise(NoiseChannel.BitFlip(0.1), 2));
        Assert.Throws<ArgumentNullException>(() => program.Append(null!));
        Assert.Throws<ArgumentException>(() => program.Append(new QuantumCircuit(1)));
        Assert.Throws<ArgumentNullException>(() => program.If(1, 1, null!));
        Assert.Throws<ArgumentException>(() => program.If(1, 1, new QuantumCircuit(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => program.If(0, 0, new QuantumCircuit(2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => program.If(4, 0, new QuantumCircuit(2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => program.If(1, 2, new QuantumCircuit(2)));
        Assert.Throws<ArgumentException>(() => program.Measure([], []));
        Assert.Throws<ArgumentException>(() => program.Measure([0], [0, 1]));
        Assert.Throws<ArgumentException>(() => program.Measure([0, 0], [0, 1]));
        Assert.Throws<ArgumentException>(() => program.Measure([0, 1], [0, 0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => program.Measure([2], [0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => program.Measure([0], [2]));
        Assert.Throws<ArgumentOutOfRangeException>(() => program.Reset(2));
        Assert.Equal(4, program.StepCount);
        var result = Create().SimulateTrajectory(program, 9);
        Assert.Equal(2, result.ClassicalOutcome);
        Assert.Equal(1, result.FinalState.Probability(2));
        // Appending after an earlier execution must extend the same private pending segment.
        program.ApplyNoise(NoiseChannel.BitFlip(1), 1).Measure([1], [1]);
        Assert.Equal(0, Create().SimulateTrajectory(program, 9).ClassicalOutcome);
    }

    [Fact]
    public void Inputs_budgets_and_pre_cancellation_are_checked_before_state_mutation_or_backend_calls()
    {
        var state = new StateVector(1);
        var circuit = new NoisyDynamicCircuit(1, 1);
        var unitary = new ThrowingUnitary();
        var noise = new ThrowingNoise();
        Assert.Throws<ArgumentNullException>(() => state.EvolveNoisyDynamic(null!, unitary, noise, new Random()));
        Assert.Throws<ArgumentNullException>(() => state.EvolveNoisyDynamic(circuit, null!, noise, new Random()));
        Assert.Throws<ArgumentNullException>(() => state.EvolveNoisyDynamic(circuit, unitary, null!, new Random()));
        Assert.Throws<ArgumentNullException>(() => state.EvolveNoisyDynamic(circuit, unitary, noise, null!));
        Assert.Throws<ArgumentException>(() => state.EvolveNoisyDynamic(new NoisyDynamicCircuit(2, 1), unitary, noise, new Random()));
        Assert.Throws<ArgumentNullException>(() => new NoisyDynamicStateVectorSimulator(null!, noise));
        Assert.Throws<ArgumentNullException>(() => new NoisyDynamicStateVectorSimulator(unitary, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(new SimulationOptions { MemoryBudgetBytes = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(new SimulationOptions { MemoryBudgetBytes = 31 }).Run(circuit, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create().SimulateTrajectory(new NoisyDynamicCircuit(30, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create().Run(circuit, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create().Run(circuit, -1));
        Assert.Throws<ArgumentNullException>(() => Create().Run(null!));
        Assert.Throws<ArgumentNullException>(() => Create().SimulateTrajectory(null!));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => state.EvolveNoisyDynamic(circuit, unitary, noise, new Random(), canceled.Token));
        Assert.Throws<OperationCanceledException>(() => Create().Run(circuit, cancellationToken: canceled.Token));
        Assert.Throws<OperationCanceledException>(() => Create().SimulateTrajectory(circuit, cancellationToken: canceled.Token));
        Assert.Equal(1, state.Probability(0));
        Assert.Equal(2, Create(new SimulationOptions { MemoryBudgetBytes = 32 }).Run(circuit, 2).Counts["0"]);
    }

    [Fact]
    public void Backend_failures_and_cancellation_never_return_partial_success()
    {
        var noisy = new NoisyDynamicCircuit(1, 1).ApplyNoise(NoiseChannel.BitFlip(0.5), 0);
        var unitary = new NoisyDynamicCircuit(1, 1).Append(new QuantumCircuit(1).X(0));
        Assert.Throws<InvalidOperationException>(() => new NoisyDynamicStateVectorSimulator(new CpuStateVectorBackend(), new ThrowingNoise()).Run(noisy, 1));
        Assert.Throws<InvalidOperationException>(() => new NoisyDynamicStateVectorSimulator(new ThrowingUnitary(), new CpuNoiseChannelBackend()).SimulateTrajectory(unitary));
        using var canceled = new CancellationTokenSource();
        var cancelNoise = new CancelNoise(canceled);
        var simulator = new NoisyDynamicStateVectorSimulator(new CpuStateVectorBackend(), cancelNoise);
        Assert.Throws<OperationCanceledException>(() => simulator.Run(noisy, 2, cancellationToken: canceled.Token));
        using var duringDraw = new CancellationTokenSource();
        var state = new StateVector(1);
        Assert.Throws<OperationCanceledException>(() => state.EvolveNoisyDynamic(noisy, new CpuStateVectorBackend(),
            new CpuNoiseChannelBackend(), new CancelRandom(duringDraw), duringDraw.Token));
        Assert.Equal(1, state.Probability(0));
    }

    internal static IStateVectorBackend Backend(string name) => name switch
    {
        "parallel" => new ParallelCpuStateVectorBackend(new ParallelCpuOptions { MinimumParallelQubits = 1 }),
        "simd" => new ParallelCpuStateVectorBackend(new ParallelCpuOptions { MinimumParallelQubits = 1, EnableSimd = true }),
        "cuda" => new CudaStateVectorBackend(),
        _ => new CpuStateVectorBackend()
    };

    internal static void AssertAmplitudes(Complex[] expected, Complex[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int index = 0; index < expected.Length; index++) { Assert.InRange(Complex.Abs(expected[index] - actual[index]), 0, 4e-12); }
    }

    private sealed class CountingRandom(int seed) : Random(seed)
    {
        public int Draws { get; private set; }
        public override double NextDouble() { Draws++; return base.NextDouble(); }
    }

    private sealed class ThrowingRandom : Random { public override double NextDouble() => throw new InvalidOperationException("Unexpected draw"); }
    private sealed class CancelRandom(CancellationTokenSource source) : Random { public override double NextDouble() { source.Cancel(); return 0; } }
    private sealed class ThrowingUnitary : IStateVectorBackend
    {
        public void Execute(QuantumCircuit circuit, Memory<Complex> amplitudes, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unitary backend failed");
    }

    private sealed class ThrowingNoise : INoiseChannelBackend
    {
        public void Apply(NoiseChannel channel, int target, Memory<Complex> amplitudes, Random random, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Noise backend failed");
    }

    private sealed class CancelNoise(CancellationTokenSource source) : INoiseChannelBackend
    {
        public void Apply(NoiseChannel channel, int target, Memory<Complex> amplitudes, Random random, CancellationToken cancellationToken = default) => source.Cancel();
    }
}
