using System.Numerics;
using QuantumMatter.Application;
using QuantumMatter.Core;
using QuantumMatter.Infrastructure;
using Xunit;

namespace QuantumMatter.Tests;

public sealed class ValidationAndExecutionTests
{
    private readonly StateVectorSimulator _simulator = new(new CpuStateVectorBackend());

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(31)]
    public void Invalid_register_sizes_are_rejected(int qubits)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new QuantumCircuit(qubits));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StateVector(qubits));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void All_gate_builders_reject_invalid_qubits_without_modifying_circuit(int qubit)
    {
        var circuit = new QuantumCircuit(3);
        foreach (GateKind kind in Enum.GetValues<GateKind>().Where(k => k < GateKind.CNot))
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => StateVectorBackendConformanceTests.Append(circuit, kind, qubit, 0));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => circuit.CNot(qubit, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => circuit.CNot(0, qubit));
        Assert.Throws<ArgumentOutOfRangeException>(() => circuit.Swap(qubit, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => circuit.Swap(0, qubit));
        Assert.Empty(circuit.Operations);
    }

    [Fact]
    public void Two_qubit_gates_require_distinct_qubits()
    {
        var circuit = new QuantumCircuit(2);
        Assert.Throws<ArgumentException>(() => circuit.CNot(0, 0));
        Assert.Throws<ArgumentException>(() => circuit.Swap(1, 1));
        Assert.Empty(circuit.Operations);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Rotations_require_finite_angles(double angle)
    {
        var circuit = new QuantumCircuit(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => circuit.Rx(0, angle));
        Assert.Throws<ArgumentOutOfRangeException>(() => circuit.Ry(0, angle));
        Assert.Throws<ArgumentOutOfRangeException>(() => circuit.Rz(0, angle));
        Assert.Empty(circuit.Operations);
    }

    [Fact]
    public void Null_dependencies_and_invalid_shots_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new StateVectorSimulator(null!));
        Assert.Throws<ArgumentNullException>(() => _simulator.Simulate(null!));
        Assert.Throws<ArgumentNullException>(() => _simulator.Run(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => _simulator.Run(new QuantumCircuit(1), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _simulator.Run(new QuantumCircuit(1), -1));
        Assert.Throws<ArgumentNullException>(() => new StateVector(1).MeasureAll(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new StateVectorSimulator(new CpuStateVectorBackend(), new SimulationOptions { MemoryBudgetBytes = 0 }));
    }

    [Fact]
    public void Allocation_budget_includes_sampling_and_is_checked_before_backend_execution()
    {
        var backend = new RecordingBackend();
        var simulator = new StateVectorSimulator(backend, new SimulationOptions { MemoryBudgetBytes = 64 });
        Assert.Equal(64, StateVector.RequiredBytes(2));
        Assert.Equal(16L * (1L << 30), StateVector.RequiredBytes(30));
        Assert.Throws<ArgumentOutOfRangeException>(() => simulator.Simulate(new QuantumCircuit(3)));
        Assert.Throws<ArgumentOutOfRangeException>(() => simulator.Run(new QuantumCircuit(2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StateVector(30));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StateVector(2, 63));
        Assert.Equal(0, backend.Calls);
        simulator.Simulate(new QuantumCircuit(2));
        Assert.Equal(1, backend.Calls);

        var samplingSimulator = new StateVectorSimulator(backend, new SimulationOptions { MemoryBudgetBytes = 96 });
        Assert.Equal(1024, samplingSimulator.Run(new QuantumCircuit(2)).Counts["00"]);
    }

    [Fact]
    public void Cancellation_is_propagated_and_checked_before_work()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var circuit = new QuantumCircuit(2).H(0);
        Assert.Throws<OperationCanceledException>(() => _simulator.Simulate(circuit, source.Token));
        Assert.Throws<OperationCanceledException>(() => _simulator.Run(circuit, cancellationToken: source.Token));
        var state = new StateVector(2);
        Assert.Throws<OperationCanceledException>(() => state.MeasureAll(new Random(), source.Token));
        Assert.Equal(1, state.Probability(0));
        Assert.Throws<OperationCanceledException>(() =>
            new CpuStateVectorBackend().Execute(circuit, new Complex[4], source.Token));

        using var forwarded = new CancellationTokenSource();
        var backend = new RecordingBackend
        {
            OnExecute = token =>
        {
            Assert.Equal(forwarded.Token, token);
            forwarded.Cancel();
            token.ThrowIfCancellationRequested();
        }
        };
        var simulator = new StateVectorSimulator(backend);
        Assert.Throws<OperationCanceledException>(() => simulator.Simulate(circuit, forwarded.Token));
        Assert.Equal(1, backend.Calls);
    }

    [Fact]
    public void State_and_backend_reject_incompatible_sizes()
    {
        var state = new StateVector(2);
        var backend = new CpuStateVectorBackend();
        Assert.Throws<ArgumentNullException>(() => state.Evolve(null!, backend));
        Assert.Throws<ArgumentNullException>(() => state.Evolve(new QuantumCircuit(2), null!));
        Assert.Throws<ArgumentException>(() => state.Evolve(new QuantumCircuit(1), backend));
        Assert.Throws<ArgumentNullException>(() => backend.Execute(null!, new Complex[2]));
        Assert.Throws<ArgumentException>(() => backend.Execute(new QuantumCircuit(2), new Complex[2]));
        Assert.Throws<ArgumentException>(() => backend.Execute(new QuantumCircuit(2), new Complex[8]));
    }

    [Fact]
    public void Snapshots_and_operation_views_do_not_allow_mutating_domain_state()
    {
        var state = new StateVector(2);
        Complex[] snapshot = state.Snapshot();
        snapshot[0] = 0;
        Assert.Equal(Complex.One, state.Amplitude(0));
        var circuit = new QuantumCircuit(2).H(0);
        Assert.Throws<NotSupportedException>(() => ((IList<GateOperation>)circuit.Operations).Clear());
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Amplitude(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Probability(4));
    }

    private sealed class RecordingBackend : IStateVectorBackend
    {
        public int Calls { get; private set; }
        public Action<CancellationToken>? OnExecute { get; init; }

        public void Execute(QuantumCircuit circuit, Memory<Complex> amplitudes, CancellationToken cancellationToken = default)
        {
            Calls++;
            OnExecute?.Invoke(cancellationToken);
        }
    }
}
