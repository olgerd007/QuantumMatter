using QuantumMatter.Application;
using QuantumMatter.Application.Variational;
using QuantumMatter.Core;
using QuantumMatter.Demo;
using QuantumMatter.Infrastructure;
using Xunit;

namespace QuantumMatter.Tests;

public sealed class ChannelAssignmentExampleTests
{
    [Fact]
    public void Hamiltonian_matches_direct_interference_cost_for_every_assignment()
    {
        var backend = new CpuStateVectorBackend();
        var simulator = new StateVectorSimulator(backend);
        var hamiltonian = ChannelAssignmentExample.CreateHamiltonian();
        for (int assignment = 0; assignment < 64; assignment++)
        {
            var circuit = new QuantumCircuit(6);
            for (int qubit = 0; qubit < 6; qubit++)
            {
                if ((assignment & (1 << qubit)) != 0) { circuit.X(qubit); }
            }

            int expected = DirectScore(assignment);
            Assert.Equal(expected, ChannelAssignmentExample.Score(assignment));
            Assert.InRange(Math.Abs(simulator.Simulate(circuit).ExpectationEnergy(hamiltonian) + expected), 0, 1e-12);
            Assert.Equal(expected, ChannelAssignmentExample.Score(assignment ^ 63));
        }
    }

    [Fact]
    public void Optimized_distribution_improves_random_baseline_and_samples_an_exact_solution()
    {
        var backend = new CpuStateVectorBackend();
        var result = ChannelAssignmentExample.Optimize(new VariationalEnergyEvaluator(backend));
        var sample = new StateVectorSimulator(backend).Run(result.Circuit, shots: 2048, seed: 42);
        double expectedScore = Enumerable.Range(0, 64).Sum(value => sample.FinalState.Probability(value) * DirectScore(value));
        int optimum = Enumerable.Range(0, 64).Max(DirectScore);
        Assert.InRange(Math.Abs(result.Energy + expectedScore), 0, 1e-10);
        Assert.True(expectedScore > 23, "The optimized expectation must improve on independent uniform channel choices.");
        Assert.Equal(optimum, sample.Counts.Keys.Max(bits => DirectScore(Convert.ToInt32(bits, 2))));
    }

    [Fact]
    public void Optimization_honors_pre_cancelled_token()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var evaluator = new VariationalEnergyEvaluator(new CpuStateVectorBackend());
        Assert.Throws<OperationCanceledException>(() => ChannelAssignmentExample.Optimize(evaluator, cancellation.Token));
    }

    // Independent classical objective: each XOR contributes its edge weight when channels differ.
    private static int DirectScore(int assignment)
    {
        int[] b = Enumerable.Range(0, 6).Select(bit => (assignment >> bit) & 1).ToArray();
        return 8 * (b[0] ^ b[1]) + 6 * (b[0] ^ b[2]) + 3 * (b[1] ^ b[2])
            + 5 * (b[1] ^ b[3]) + 7 * (b[2] ^ b[4]) + 4 * (b[3] ^ b[4])
            + 6 * (b[3] ^ b[5]) + 5 * (b[4] ^ b[5]) + 2 * (b[0] ^ b[5]);
    }
}
