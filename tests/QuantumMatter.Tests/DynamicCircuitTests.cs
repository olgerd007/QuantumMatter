using System.Numerics;
using QuantumMatter.Application;
using QuantumMatter.Application.Dynamic;
using QuantumMatter.Core;
using QuantumMatter.Core.Dynamic;
using QuantumMatter.Infrastructure;
using Xunit;

namespace QuantumMatter.Tests;

public sealed class DynamicCircuitTests
{
    private static DynamicStateVectorSimulator Create(SimulationOptions? options = null) => new(new CpuStateVectorBackend(), options);

    [Theory]
    [InlineData("scalar")]
    [InlineData("parallel")]
    [InlineData("simd")]
#if CUDA_TESTS
    [InlineData("cuda")]
#endif
    public void Teleportation_recovers_complex_amplitudes_for_every_measurement_branch(string backendName)
    {
        IStateVectorBackend backend = backendName switch
        {
            "parallel" => new ParallelCpuStateVectorBackend(new ParallelCpuOptions { MinimumParallelQubits = 1 }),
            "simd" => new ParallelCpuStateVectorBackend(new ParallelCpuOptions { MinimumParallelQubits = 1, EnableSimd = true }),
            "cuda" => new CudaStateVectorBackend(),
            _ => new CpuStateVectorBackend()
        };
        using IDisposable? ownedBackend = backend as IDisposable;
        foreach ((double theta, double phi) in new[] { (0d, 0d), (Math.PI, 0d), (Math.PI / 2, 0d), (Math.PI / 2, Math.PI / 2), (0.73, -0.61) })
        {
            Complex alpha = Math.Cos(theta / 2) * Complex.FromPolarCoordinates(1, -phi / 2);
            Complex beta = Math.Sin(theta / 2) * Complex.FromPolarCoordinates(1, phi / 2);
            DynamicCircuit circuit = Teleportation(theta, phi);
            for (int branch = 0; branch < 4; branch++)
            {
                var state = new StateVector(3);
                // Before correction, q1 XOR q2 chooses alpha versus beta. Pick a supported basis midpoint.
                double zeroMass = alpha.Magnitude * alpha.Magnitude;
                double oneMass = beta.Magnitude * beta.Magnitude;
                bool lowerIsAlpha = (branch & 2) == 0;
                int sampledBasis = branch | ((lowerIsAlpha ? zeroMass >= oneMass : oneMass >= zeroMass) ? 0 : 4);
                double draw = 0;
                for (int basis = 0; basis <= sampledBasis; basis++)
                {
                    double mass = (((basis >> 1) ^ (basis >> 2)) & 1) == 0 ? zeroMass / 4 : oneMass / 4;
                    draw += basis == sampledBasis ? mass / 2 : mass;
                }

                Assert.Equal(branch, state.EvolveDynamic(circuit, backend, new FixedRandom(draw)));
                for (int basis = 0; basis < 8; basis++)
                {
                    Complex expected = basis == branch ? alpha : basis == (branch | 4) ? beta : 0;
                    Assert.InRange(Complex.Abs(state.Amplitude(basis) - expected), 0, 3e-12);
                }
            }

            DynamicTrajectoryResult result = new DynamicStateVectorSimulator(backend).SimulateTrajectory(circuit, seed: 43);
            Assert.InRange(Complex.Abs(result.FinalState.Amplitude(result.ClassicalOutcome) - alpha), 0, 3e-12);
            Assert.InRange(Complex.Abs(result.FinalState.Amplitude(result.ClassicalOutcome | 4) - beta), 0, 3e-12);
            Assert.Equal(2, result.ClassicalBitCount);
        }
    }

    internal static DynamicCircuit Teleportation(double theta, double phi) => new DynamicCircuit(3, 2)
        .Append(new QuantumCircuit(3).Ry(0, theta).Rz(0, phi).H(1).CNot(1, 2).CNot(0, 1).H(0))
        .Measure([0, 1], [0, 1])
        .If(2, 2, new QuantumCircuit(3).X(2))
        .If(1, 1, new QuantumCircuit(3).Z(2));

    [Fact]
    public void Ordered_destinations_and_overwriting_zero_preserve_unrelated_classical_bits()
    {
        var circuit = new DynamicCircuit(3, 3).Append(new QuantumCircuit(3).X(0).X(1))
            .Measure([0, 1], [0, 2]).Append(new QuantumCircuit(3).X(0))
            .Measure([0], [0]).If(4, 4, new QuantumCircuit(3).X(2));
        DynamicTrajectoryResult result = Create().SimulateTrajectory(circuit, 19);
        Assert.Equal(4, result.ClassicalOutcome);
        Assert.Equal(1, result.FinalState.Probability(6));
        DynamicSimulationResult counts = Create().Run(circuit, 23, 19);
        Assert.Equal(23, counts.Counts["100"]);
        Assert.Equal(23, counts.Shots);
        Assert.Equal(3, counts.ClassicalBitCount);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, int>)counts.Counts).Clear());
    }

    [Fact]
    public void Every_basis_maps_reordered_measurements_into_requested_classical_positions()
    {
        for (int basis = 0; basis < 16; basis++)
        {
            var preparation = new QuantumCircuit(4);
            for (int q = 0; q < 4; q++) { if ((basis & (1 << q)) != 0) { preparation.X(q); } }
            var circuit = new DynamicCircuit(4, 5).Append(preparation).Measure([3, 0, 2], [0, 4, 2]);
            int expected = ((basis >> 3) & 1) | ((basis & 1) << 4) | (basis & 4);
            Assert.Equal(expected, Create().SimulateTrajectory(circuit, 27).ClassicalOutcome);
        }
    }

    [Fact]
    public void Conditions_support_zero_values_and_multibit_masks_without_changing_classical_memory()
    {
        for (int register = 0; register < 4; register++)
        {
            var preparation = new QuantumCircuit(3);
            if ((register & 1) != 0) { preparation.X(0); }
            if ((register & 2) != 0) { preparation.X(1); }
            var circuit = new DynamicCircuit(3, 2).Append(preparation).Measure([0, 1], [0, 1])
                .If(3, 1, new QuantumCircuit(3).X(2)).If(3, 0, new QuantumCircuit(3).Z(2));
            DynamicTrajectoryResult result = Create().SimulateTrajectory(circuit, 81);
            Assert.Equal(register, result.ClassicalOutcome);
            Assert.Equal(1, result.FinalState.Probability(register == 1 ? register | 4 : register));
        }
    }

    [Fact]
    public void Highest_classical_bit_works_without_allocating_a_large_quantum_state()
    {
        var circuit = new DynamicCircuit(1, 30).Append(new QuantumCircuit(1).X(0)).Measure([0], [29])
            .If(1 << 29, 1 << 29, new QuantumCircuit(1).X(0));
        var result = Create().SimulateTrajectory(circuit, 12);
        Assert.Equal(1 << 29, result.ClassicalOutcome);
        Assert.Equal(1, result.FinalState.Probability(0));
        Assert.Equal(2, Create().Run(circuit, 2, 12).Counts["1" + new string('0', 29)]);
    }

    [Fact]
    public void Builder_snapshots_unitaries_conditions_and_measurement_mappings()
    {
        var preparation = new QuantumCircuit(2).X(0);
        var correction = new QuantumCircuit(2).X(1);
        int[] qubits = [0];
        int[] classical = [0];
        var circuit = new DynamicCircuit(2, 2).Append(preparation).Measure(qubits, classical).If(1, 1, correction);
        preparation.X(0);
        correction.X(1);
        qubits[0] = 1;
        classical[0] = 1;
        var result = Create().SimulateTrajectory(circuit, 32);
        Assert.Equal(1, result.ClassicalOutcome);
        Assert.Equal(1, result.FinalState.Probability(3));
        Assert.Equal(3, circuit.StepCount);
    }

    [Fact]
    public void No_implicit_terminal_measurement_and_every_shot_reexecutes_the_program()
    {
        var backend = new CountingBackend();
        var simulator = new DynamicStateVectorSimulator(backend);
        var circuit = new DynamicCircuit(1, 3).Append(new QuantumCircuit(1).H(0));
        var trajectory = simulator.SimulateTrajectory(circuit, 42);
        Assert.Equal(0, trajectory.ClassicalOutcome);
        Assert.InRange(Math.Abs(trajectory.FinalState.Probability(1) - 0.5), 0, 1e-12);
        Assert.Equal(20, simulator.Run(circuit, 20, 42).Counts["000"]);
        Assert.Equal(21, backend.Calls);
        // Starting every shot at |0> is essential: repeated X on one continuing state would alternate.
        var flip = new DynamicCircuit(1, 1).Append(new QuantumCircuit(1).X(0)).Measure([0], [0]);
        Assert.Equal(20, simulator.Run(flip, 20, 42).Counts["1"]);
    }

    [Fact]
    public void Classical_memory_starts_at_zero_for_each_evolution_of_an_existing_state()
    {
        var circuit = new DynamicCircuit(1, 1).If(1, 0, new QuantumCircuit(1).X(0)).Measure([0], [0]);
        var state = new StateVector(1);
        Assert.Equal(1, state.EvolveDynamic(circuit, new CpuStateVectorBackend(), new Random(9)));
        Assert.Equal(0, state.EvolveDynamic(circuit, new CpuStateVectorBackend(), new Random(9)));
    }

    [Fact]
    public void Independent_mid_circuit_draws_are_seeded_and_not_one_shared_branch_per_run()
    {
        var circuit = new DynamicCircuit(1, 2).Append(new QuantumCircuit(1).H(0)).Measure([0], [0])
            .If(1, 1, new QuantumCircuit(1).X(0)).Append(new QuantumCircuit(1).H(0)).Measure([0], [1]);
        var first = Create().Run(circuit, 4000, 528);
        var second = Create().Run(circuit, 4000, 528);
        Assert.Equal(first.Counts.ToArray(), second.Counts.ToArray());
        Assert.Equal(4000, first.Counts.Values.Sum());
        Assert.Equal(4, first.Counts.Count);
        Assert.All(first.Counts.Values, count => Assert.InRange(count, 800, 1200));
    }

    [Fact]
    public void Skipped_conditions_never_invoke_a_backend_and_empty_circuits_are_valid()
    {
        var state = new StateVector(1);
        var backend = new ThrowingBackend();
        Assert.Equal(0, state.EvolveDynamic(new DynamicCircuit(1, 1), backend, new ThrowingRandom()));
        var circuit = new DynamicCircuit(1, 1).If(1, 1, new QuantumCircuit(1).X(0));
        Assert.Equal(0, state.EvolveDynamic(circuit, backend, new ThrowingRandom()));
        Assert.Equal(1, state.Probability(0));
    }

    [Fact]
    public void Reset_preserves_spectator_phase_and_does_not_write_classical_bits()
    {
        var circuit = new DynamicCircuit(2, 1).Append(new QuantumCircuit(2).X(0).S(0).H(1).S(1))
            .Measure([0], [0]).Reset(0);
        var result = Create().SimulateTrajectory(circuit, 14);
        Assert.Equal(1, result.ClassicalOutcome);
        Assert.InRange(Complex.Abs(result.FinalState.Amplitude(0) - Complex.ImaginaryOne / Math.Sqrt(2)), 0, 1e-12);
        Assert.InRange(Complex.Abs(result.FinalState.Amplitude(2) + 1 / Math.Sqrt(2)), 0, 1e-12);
    }

    [Fact]
    public void Reset_and_feedback_execute_on_every_enabled_backend()
    {
        // The full gate/backend oracle remains separate; this check covers reset instruction placement.
        var circuit = new DynamicCircuit(2, 2).Append(new QuantumCircuit(2).H(0).CNot(0, 1))
            .Measure([1], [1]).Reset(0).If(2, 2, new QuantumCircuit(2).X(1)).Measure([0, 1], [0, 1]);
        foreach (IStateVectorBackend backend in Backends())
        {
            using IDisposable? owned = backend as IDisposable;
            Assert.Equal(16, new DynamicStateVectorSimulator(backend).Run(circuit, 16, 39).Counts["00"]);
        }
    }

    private static IEnumerable<IStateVectorBackend> Backends()
    {
        yield return new CpuStateVectorBackend();
        yield return new ParallelCpuStateVectorBackend(new ParallelCpuOptions { MinimumParallelQubits = 1, EnableSimd = true });
#if CUDA_TESTS
        yield return new CudaStateVectorBackend();
#endif
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(31, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 31)]
    [InlineData(1, -1)]
    public void Invalid_register_sizes_are_rejected(int qubits, int bits) => Assert.Throws<ArgumentOutOfRangeException>(() => new DynamicCircuit(qubits, bits));

    [Fact]
    public void Invalid_instructions_leave_the_builder_unchanged()
    {
        var circuit = new DynamicCircuit(2, 2);
        Assert.Throws<ArgumentNullException>(() => circuit.Append(null!));
        Assert.Throws<ArgumentException>(() => circuit.Append(new QuantumCircuit(1)));
        Assert.Throws<ArgumentNullException>(() => circuit.If(1, 1, null!));
        foreach (int mask in new[] { 0, -1, 4, int.MaxValue, int.MinValue })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => circuit.If(mask, 0, new QuantumCircuit(2)));
        }

        foreach (int value in new[] { -1, 2, int.MaxValue, int.MinValue })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => circuit.If(1, value, new QuantumCircuit(2)));
        }

        Assert.Throws<ArgumentException>(() => circuit.Measure([], []));
        Assert.Throws<ArgumentException>(() => circuit.Measure([0], [0, 1]));
        Assert.Throws<ArgumentException>(() => circuit.Measure([0, 0], [0, 1]));
        Assert.Throws<ArgumentException>(() => circuit.Measure([0, 1], [0, 0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => circuit.Measure([-1], [0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => circuit.Measure([2], [0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => circuit.Measure([0], [2]));
        Assert.Throws<ArgumentOutOfRangeException>(() => circuit.Reset(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => circuit.Reset(2));
        Assert.Equal(0, circuit.StepCount);
    }

    [Fact]
    public void Execution_validates_null_sizes_shots_budgets_and_cancellation_before_allocating_or_calling_backends()
    {
        var state = new StateVector(1);
        var circuit = new DynamicCircuit(1, 1);
        var backend = new ThrowingBackend();
        Assert.Throws<ArgumentNullException>(() => state.EvolveDynamic(null!, backend, new Random()));
        Assert.Throws<ArgumentNullException>(() => state.EvolveDynamic(circuit, null!, new Random()));
        Assert.Throws<ArgumentNullException>(() => state.EvolveDynamic(circuit, backend, null!));
        Assert.Throws<ArgumentException>(() => state.EvolveDynamic(new DynamicCircuit(2, 1), backend, new Random()));
        Assert.Throws<ArgumentNullException>(() => new DynamicStateVectorSimulator(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(new SimulationOptions { MemoryBudgetBytes = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(new SimulationOptions { MemoryBudgetBytes = 31 }).Run(circuit, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create().SimulateTrajectory(new DynamicCircuit(30, 1)));
        Assert.Throws<ArgumentNullException>(() => Create().Run(null!));
        Assert.Throws<ArgumentNullException>(() => Create().SimulateTrajectory(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create().Run(circuit, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create().Run(circuit, -1));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var simulator = new DynamicStateVectorSimulator(backend);
        Assert.Throws<OperationCanceledException>(() => simulator.Run(circuit, 1, cancellationToken: canceled.Token));
        Assert.Throws<OperationCanceledException>(() => simulator.SimulateTrajectory(circuit, cancellationToken: canceled.Token));
        Assert.Throws<OperationCanceledException>(() => state.EvolveDynamic(circuit, backend, new Random(), canceled.Token));
        Assert.Equal(1, state.Probability(0));
        // Only the amplitude array is required; dynamic runs do not allocate a terminal CDF.
        Assert.Equal(2, Create(new SimulationOptions { MemoryBudgetBytes = 32 }).Run(circuit, 2).Counts["0"]);
    }

    [Fact]
    public void Cancellation_and_backend_failures_never_return_successful_partial_results()
    {
        using var canceled = new CancellationTokenSource();
        var backend = new CancelingBackend(canceled);
        var circuit = new DynamicCircuit(1, 1).Append(new QuantumCircuit(1).X(0));
        Assert.Throws<OperationCanceledException>(() => new DynamicStateVectorSimulator(backend).Run(circuit, 2, cancellationToken: canceled.Token));
        Assert.Throws<InvalidOperationException>(() => new DynamicStateVectorSimulator(new ThrowingBackend()).Run(circuit, 1));
        using var duringMeasurement = new CancellationTokenSource();
        var state = new StateVector(1);
        Assert.Throws<OperationCanceledException>(() => state.EvolveDynamic(new DynamicCircuit(1, 1).Reset(0),
            new CpuStateVectorBackend(), new CancelingRandom(duringMeasurement), duringMeasurement.Token));
        Assert.Equal(1, state.Probability(0));
    }

    private sealed class FixedRandom(double value) : Random { public override double NextDouble() => value; }
    private sealed class ThrowingRandom : Random { public override double NextDouble() => throw new InvalidOperationException("Unexpected random draw"); }
    private sealed class CancelingRandom(CancellationTokenSource source) : Random { public override double NextDouble() { source.Cancel(); return 0; } }
    private sealed class ThrowingBackend : IStateVectorBackend
    {
        public void Execute(QuantumCircuit circuit, Memory<Complex> amplitudes, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Backend failure");
    }

    private sealed class CancelingBackend(CancellationTokenSource source) : IStateVectorBackend
    {
        public void Execute(QuantumCircuit circuit, Memory<Complex> amplitudes, CancellationToken cancellationToken = default) => source.Cancel();
    }

    private sealed class CountingBackend : IStateVectorBackend
    {
        public int Calls { get; private set; }
        public void Execute(QuantumCircuit circuit, Memory<Complex> amplitudes, CancellationToken cancellationToken = default)
        {
            Calls++;
            new CpuStateVectorBackend().Execute(circuit, amplitudes, cancellationToken);
        }
    }
}
