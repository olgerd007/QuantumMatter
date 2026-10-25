using QuantumMatter.Application.Noise;
using QuantumMatter.Core;
using QuantumMatter.Core.Noise;

namespace QuantumMatter.Demo;

internal static class NoiseExamples
{
    internal static void Run(NoisyStateVectorSimulator simulator)
    {
        var circuit = new NoisyCircuit(1)
            .Append(new QuantumCircuit(1).X(0))
            .ApplyNoise(NoiseChannel.AmplitudeDamping(0.3), target: 0);
        NoisyProbabilityResult estimate = simulator.EstimateProbabilities(circuit, trajectories: 10000, seed: 42);
        ProbabilityEstimate excited = estimate.Estimate(1);
        Console.WriteLine("Amplitude damping on |1>, gamma=0.3: analytic P(1)=0.7.");
        Console.WriteLine($"Trajectory estimate: {excited.Probability:F6}; estimated standard error: {excited.StandardError:F6}");
        NoisySimulationResult samples = simulator.Run(circuit, shots: 10000, seed: 42);
        Console.WriteLine("One terminal measurement per fresh trajectory:");
        foreach ((string bits, int count) in samples.Counts)
        {
            Console.WriteLine($"|{bits}> = {count}");
        }
    }
}
