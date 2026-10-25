using System.Numerics;
using QuantumMatter.Application;
using QuantumMatter.Application.Algorithms;
using QuantumMatter.Core;
using QuantumMatter.Infrastructure;
using Xunit;

namespace QuantumMatter.Tests;

public sealed class GroverSearchTests
{
    private readonly StateVectorSimulator _simulator = new(new CpuStateVectorBackend());

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void All_marked_states_match_exact_amplification_including_overshoot(int inputQubits)
    {
        for (int marked = 0; marked < 1 << inputQubits; marked++)
        {
            for (int iterations = 0; iterations <= 5; iterations++)
            {
                Verify(inputQubits, marked, iterations);
            }
        }
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void Deeper_scratch_ladders_match_exact_amplitudes_and_return_to_zero(int inputQubits)
    {
        foreach (int marked in new[] { 0, 1, (1 << inputQubits) / 2, (1 << inputQubits) - 1 })
        {
            Verify(inputQubits, marked, GroverSearch.RecommendedIterations(inputQubits));
        }
    }

    [Fact]
    public void Four_states_have_exact_success_and_eight_states_have_expected_probability()
    {
        for (int marked = 0; marked < 4; marked++)
        {
            Assert.Equal(1, GroverSearch.RecommendedIterations(2));
            StateVector state = _simulator.Simulate(GroverSearch.CreateCircuit(2, marked));
            Assert.InRange(state.Probability(marked), 1 - 1e-12, 1 + 1e-12);
        }

        Assert.Equal(2, GroverSearch.RecommendedIterations(3));
        SimulationResult result = _simulator.Run(GroverSearch.CreateCircuit(3, 5), 2000, 42);
        Assert.InRange(result.FinalState.Probability(5), 121.0 / 128 - 1e-12, 121.0 / 128 + 1e-12);
        Assert.Equal(2000, result.Counts.Values.Sum());
        Assert.All(result.Counts.Keys, bits => Assert.StartsWith("0", bits));
        Assert.InRange(result.Counts["0101"], 1800, 1970);
    }

    [Fact]
    public void Recommended_iteration_count_maximizes_the_first_peak_locally()
    {
        for (int inputQubits = 1; inputQubits <= GroverSearch.MaximumInputQubits; inputQubits++)
        {
            int iterations = GroverSearch.RecommendedIterations(inputQubits);
            double theta = Math.Asin(1 / Math.Sqrt(1 << inputQubits));
            double success = Math.Pow(Math.Sin((2 * iterations + 1) * theta), 2);
            foreach (int neighbor in new[] { iterations - 1, iterations + 1 }.Where(x => x >= 0))
            {
                double alternative = Math.Pow(Math.Sin((2 * neighbor + 1) * theta), 2);
                Assert.True(success + 1e-12 >= alternative);
            }
        }
    }

    [Fact]
    public void Invalid_inputs_and_excessive_circuit_size_are_rejected_without_state_allocation()
    {
        foreach (int invalid in new[] { -1, 0, 17 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => GroverSearch.CreateCircuit(invalid, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => GroverSearch.RecommendedIterations(invalid));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => GroverSearch.CreateCircuit(3, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => GroverSearch.CreateCircuit(3, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => GroverSearch.CreateCircuit(3, 1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => GroverSearch.CreateCircuit(16, 1, int.MaxValue));
        QuantumCircuit largest = GroverSearch.CreateCircuit(16, 1);
        Assert.Equal(30, largest.QubitCount);
        Assert.True(largest.Operations.Count <= GroverSearch.MaximumCircuitOperations);
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => GroverSearch.CreateCircuit(3, 1, cancellationToken: source.Token));
    }

    private void Verify(int inputQubits, int marked, int iterations)
    {
        int size = 1 << inputQubits;
        double theta = Math.Asin(1 / Math.Sqrt(size));
        double markedAmplitude = Math.Sin((2 * iterations + 1) * theta);
        double otherAmplitude = Math.Cos((2 * iterations + 1) * theta) / Math.Sqrt(size - 1);
        StateVector state = _simulator.Simulate(GroverSearch.CreateCircuit(inputQubits, marked, iterations));
        for (int index = 0; index < state.Length; index++)
        {
            double expected = index >= size ? 0 : index == marked ? markedAmplitude : otherAmplitude;
            Assert.True(Complex.Abs(state.Amplitude(index) - expected) < 1e-11,
                $"n={inputQubits}, marked={marked}, k={iterations}, index={index}: expected {expected}, actual {state.Amplitude(index)}");
        }

        Assert.InRange(Enumerable.Range(0, state.Length).Sum(state.Probability), 1 - 1e-11, 1 + 1e-11);
    }
}
