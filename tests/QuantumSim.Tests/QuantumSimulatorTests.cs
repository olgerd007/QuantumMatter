using QuantumSim.Core;
using Xunit;

namespace QuantumSim.Tests;

public sealed class QuantumSimulatorTests
{
    private const double Epsilon = 1e-12;
    private readonly StateVectorSimulator _simulator = new();

    [Fact]
    public void Initial_state_is_zero() => Assert.Equal(1, _simulator.Simulate(new QuantumCircuit(2)).Probability(0));

    [Fact]
    public void Hadamard_creates_equal_superposition()
    {
        var state = _simulator.Simulate(new QuantumCircuit(1).H(0));
        Assert.InRange(state.Probability(0), 0.5 - Epsilon, 0.5 + Epsilon);
        Assert.InRange(state.Probability(1), 0.5 - Epsilon, 0.5 + Epsilon);
    }

    [Fact]
    public void Bell_pair_has_only_zero_zero_and_one_one()
    {
        var state = _simulator.Simulate(new QuantumCircuit(2).H(0).CNot(0, 1));
        Assert.InRange(state.Probability(0), 0.5 - Epsilon, 0.5 + Epsilon);
        Assert.InRange(state.Probability(3), 0.5 - Epsilon, 0.5 + Epsilon);
        Assert.Equal(0, state.Probability(1));
        Assert.Equal(0, state.Probability(2));
    }

    [Fact]
    public void Hadamard_is_its_own_inverse()
    {
        var state = _simulator.Simulate(new QuantumCircuit(1).H(0).H(0));
        Assert.InRange(state.Probability(0), 1 - Epsilon, 1 + Epsilon);
    }

    [Fact]
    public void X_and_swap_rearrange_bits()
    {
        var state = _simulator.Simulate(new QuantumCircuit(2).X(0).Swap(0, 1));
        Assert.Equal(1, state.Probability(2));
    }

    [Fact]
    public void Rotations_preserve_normalization()
    {
        var state = _simulator.Simulate(new QuantumCircuit(3).H(0).Ry(1, 0.7).Rx(2, -1.2).Rz(0, 0.3).CNot(1, 2));
        double total = Enumerable.Range(0, state.Length).Sum(state.Probability);
        Assert.InRange(total, 1 - Epsilon, 1 + Epsilon);
    }

    [Fact]
    public void Samples_only_observable_bell_outcomes()
    {
        var result = _simulator.Run(new QuantumCircuit(2).H(0).CNot(0, 1), 1000, 42);
        Assert.Equal(1000, result.Counts.Values.Sum());
        Assert.All(result.Counts.Keys, k => Assert.Contains(k, new[] { "00", "11" }));
    }

    [Fact]
    public void Measurement_collapses_state()
    {
        var state = _simulator.Simulate(new QuantumCircuit(1).H(0));
        var measured = state.MeasureAll(new Random(42));
        Assert.Equal(1, state.Probability(measured));
        Assert.Equal(0, state.Probability(1 - measured));
    }
}
