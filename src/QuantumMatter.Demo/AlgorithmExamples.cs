using System.Numerics;
using QuantumMatter.Application;
using QuantumMatter.Application.Algorithms;
using QuantumMatter.Core;

namespace QuantumMatter.Demo;

internal static class AlgorithmExamples
{
    internal static void RunDeutschJozsa(StateVectorSimulator simulator)
    {
        const int inputQubits = 3;
        bool[] constant = Enumerable.Repeat(true, 1 << inputQubits).ToArray();
        bool[] balanced = Enumerable.Range(0, 1 << inputQubits)
            .Select(x => (BitOperations.PopCount((uint)(x & 5)) & 1) != 0).ToArray();

        Console.WriteLine("Deutsch-Jozsa: low three bits are input; the high scratch bit returns to zero.");
        foreach (var (name, table) in new[] { ("f(x) = 1", constant), ("f(x) = x0 XOR x2", balanced) })
        {
            SimulationResult result = simulator.Run(DeutschJozsa.CreateCircuit(table), shots: 128, seed: 42);
            string input = result.Counts.Keys.First()[^inputQubits..];
            string classification = input.All(bit => bit == '0') ? "constant" : "balanced";
            Console.WriteLine($"{name}: measured input {input} -> {classification}");
        }
    }

    internal static void RunQft(StateVectorSimulator simulator)
    {
        const int basis = 5;
        var circuit = new QuantumCircuit(3).X(0).X(2);
        QuantumFourierTransform.Append(circuit);
        StateVector transformed = simulator.Simulate(circuit);
        Console.WriteLine("QFT of |101>: positive exponent, with final bit reversal included.");
        for (int index = 0; index < transformed.Length; index++)
        {
            Complex amplitude = transformed.Amplitude(index);
            Console.WriteLine(FormattableString.Invariant(
                $"|{Convert.ToString(index, 2).PadLeft(3, '0')}> real={amplitude.Real:F6}, imag={amplitude.Imaginary:F6}"));
        }

        QuantumFourierTransform.Append(circuit, inverse: true);
        Console.WriteLine(FormattableString.Invariant(
            $"Inverse QFT restores |101>: probability = {simulator.Simulate(circuit).Probability(basis):F6}"));
    }

    internal static void RunGrover(StateVectorSimulator simulator)
    {
        const int inputQubits = 3;
        const int marked = 5;
        int iterations = GroverSearch.RecommendedIterations(inputQubits);
        SimulationResult result = simulator.Run(GroverSearch.CreateCircuit(inputQubits, marked), shots: 1000, seed: 42);
        Console.WriteLine($"Grover: marked input |101>, {iterations} iterations, one high scratch bit.");
        Console.WriteLine(FormattableString.Invariant($"Success probability = {result.FinalState.Probability(marked):F6}"));
        foreach (var (bits, count) in result.Counts)
        {
            Console.WriteLine($"work |{bits[..^inputQubits]}>, input |{bits[^inputQubits..]}> = {count}");
        }
    }
}
