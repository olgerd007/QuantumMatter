using QuantumMatter.Application.Dynamic;
using QuantumMatter.Core;
using QuantumMatter.Core.Dynamic;
using QuantumMatter.Core.Noise;

namespace QuantumMatter.Demo;

internal static class NoisyDynamicExamples
{
    public static void Run(NoisyDynamicStateVectorSimulator simulator)
    {
        var program = new NoisyDynamicCircuit(2, 3).Append(new QuantumCircuit(2).H(0).CNot(0, 1))
            .ApplyNoise(NoiseChannel.BitFlip(0.25), 0).Measure([0], [0])
            .If(1, 1, new QuantumCircuit(2).X(1)).Measure([1], [1])
            .Reset(1).ApplyNoise(NoiseChannel.AmplitudeDamping(0.3), 0).Measure([1], [2]);
        DynamicSimulationResult result = simulator.Run(program, shots: 1024, seed: 42);
        Console.WriteLine("Noisy Bell feedback: c1 records a disagreement (expected probability 25%).");
        Console.WriteLine("c2 reads the reset partner and must be zero; earlier classical records survive damping.");
        foreach ((string bits, int count) in result.Counts) { Console.WriteLine($"{bits}: {count}"); }
        int disagreements = result.Counts.Where(entry => entry.Key[1] == '1').Sum(entry => entry.Value);
        Console.WriteLine($"Observed disagreement fraction: {(double)disagreements / result.Shots:P2}.");
        if (result.Counts.Keys.Any(bits => bits[0] != '0')) { throw new InvalidOperationException("Reset readout must be zero."); }
    }
}
