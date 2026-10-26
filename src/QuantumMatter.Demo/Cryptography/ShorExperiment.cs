using System.Numerics;
using QuantumMatter.Application;
using QuantumMatter.Application.Algorithms;
using QuantumMatter.Core;

namespace QuantumMatter.Demo.Cryptography;

// Exhaustive permutation synthesis is intentionally restricted to tiny moduli.
internal static class ShorExperiment
{
    internal static QuantumCircuit CreateCircuit(int modulus = 15, int basis = 2, int countingQubits = 8,
        CancellationToken cancellationToken = default)
    {
        ValidateArithmetic(modulus, basis);
        if (countingQubits is < 1 or > 8) { throw new ArgumentOutOfRangeException(nameof(countingQubits)); }
        cancellationToken.ThrowIfCancellationRequested();
        int width = WorkWidth(modulus);
        int[] work = Enumerable.Range(countingQubits, width).ToArray();
        int[] scratch = Enumerable.Range(countingQubits + width, Math.Max(0, width - 2)).ToArray();
        var circuit = new QuantumCircuit(countingQubits + width + scratch.Length).X(work[0]);
        for (int q = 0; q < countingQubits; q++) { circuit.H(q); }
        int multiplier = basis;
        for (int q = 0; q < countingQubits; q++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppendMultiplier(circuit, modulus, multiplier, q, work, scratch, cancellationToken);
            multiplier = multiplier * multiplier % modulus;
        }

        QuantumFourierTransform.Append(circuit, Enumerable.Range(0, countingQubits).ToArray(), inverse: true,
            cancellationToken: cancellationToken);
        return circuit;
    }

    internal static QuantumCircuit CreateMultiplier(int modulus, int multiplier, CancellationToken cancellationToken = default)
    {
        ValidateArithmetic(modulus, multiplier);
        cancellationToken.ThrowIfCancellationRequested();
        int width = WorkWidth(modulus);
        int[] scratch = Enumerable.Range(width + 1, Math.Max(0, width - 2)).ToArray();
        var circuit = new QuantumCircuit(width + 1 + scratch.Length);
        AppendMultiplier(circuit, modulus, multiplier, width, Enumerable.Range(0, width).ToArray(), scratch, cancellationToken);
        return circuit;
    }

    internal static ShorResult Run(StateVectorSimulator simulator, int modulus = 15, int basis = 2,
        int countingQubits = 8, int shots = 512, int seed = 42, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(simulator);
        if (shots is < 1 or > 1_000_000) { throw new ArgumentOutOfRangeException(nameof(shots)); }
        QuantumCircuit circuit = CreateCircuit(modulus, basis, countingQubits, cancellationToken);
        SimulationResult sample = simulator.Run(circuit, shots, seed, cancellationToken);
        var histogram = new SortedDictionary<int, int>();
        int mask = (1 << countingQubits) - 1;
        foreach (var (bits, count) in sample.Counts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int outcome = Convert.ToInt32(bits, 2) & mask;
            histogram[outcome] = histogram.GetValueOrDefault(outcome) + count;
        }

        (int First, int Second)? factors = null;
        foreach (int outcome in histogram.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            factors = RecoverFactors(modulus, basis, outcome, countingQubits);
            if (factors.HasValue) { break; }
        }

        return new ShorResult(circuit.QubitCount, circuit.Operations.Count,
            new System.Collections.ObjectModel.ReadOnlyDictionary<int, int>(histogram), factors);
    }

    internal static (int First, int Second)? RecoverFactors(int modulus, int basis, int outcome, int countingQubits)
    {
        ValidateArithmetic(modulus, basis);
        if (countingQubits is < 1 or > 8) { throw new ArgumentOutOfRangeException(nameof(countingQubits)); }
        int dimension = 1 << countingQubits;
        if ((uint)outcome >= (uint)dimension) { throw new ArgumentOutOfRangeException(nameof(outcome)); }
        if (outcome == 0) { return null; }

        int numerator = outcome, denominator = dimension;
        int previousDenominator = 0, currentDenominator = 1;
        while (denominator != 0)
        {
            int quotient = numerator / denominator;
            int next = quotient * previousDenominator + currentDenominator;
            currentDenominator = previousDenominator;
            previousDenominator = next;
            (numerator, denominator) = (denominator, numerator % denominator);
            if (next <= 1 || next >= modulus) { continue; }

            // Reduced fractions can reveal a divisor of the order. This bounded
            // multiple check is teaching postprocessing, not scalable factoring.
            for (int order = next; order < modulus; order += next)
            {
                if (order % 2 != 0 || BigInteger.ModPow(basis, order, modulus) != 1) { continue; }
                int halfPower = (int)BigInteger.ModPow(basis, order / 2, modulus);
                int factor = Gcd(halfPower - 1, modulus);
                if (factor is > 1 && factor < modulus)
                {
                    return (Math.Min(factor, modulus / factor), Math.Max(factor, modulus / factor));
                }

                factor = Gcd(halfPower + 1, modulus);
                if (factor is > 1 && factor < modulus)
                {
                    return (Math.Min(factor, modulus / factor), Math.Max(factor, modulus / factor));
                }
            }
        }

        return null;
    }

    private static void AppendMultiplier(QuantumCircuit circuit, int modulus, int multiplier, int control,
        int[] work, int[] scratch, CancellationToken cancellationToken)
    {
        // The finite bounds imply fewer than 40,000 gates even for eight
        // multipliers; no matrix or exponentially unbounded circuit is built.
        var visited = new bool[1 << work.Length];
        for (int start = 0; start < modulus; start++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (visited[start]) { continue; }
            visited[start] = true;
            int next = start * multiplier % modulus;
            while (next != start)
            {
                visited[next] = true;
                AppendTransposition(circuit, start, next, control, work, scratch);
                next = next * multiplier % modulus;
            }
        }
    }

    private static void AppendTransposition(QuantumCircuit circuit, int first, int second, int control, int[] work, int[] scratch)
    {
        // Adjacent swaps along a Gray path, then all except the final edge in
        // reverse, exchange just the endpoints and restore intermediate vertices.
        var path = new List<(int Vertex, int Bit)>();
        int vertex = first;
        for (int bit = 0; bit < work.Length; bit++)
        {
            if (((first ^ second) & (1 << bit)) == 0) { continue; }
            path.Add((vertex, bit));
            vertex ^= 1 << bit;
        }

        foreach (var edge in path) { AdjacentSwap(edge.Vertex, edge.Bit); }
        for (int index = path.Count - 2; index >= 0; index--) { AdjacentSwap(path[index].Vertex, path[index].Bit); }

        void AdjacentSwap(int pattern, int targetBit)
        {
            int[] controls = new int[work.Length];
            controls[0] = control;
            int count = 1;
            for (int bit = 0; bit < work.Length; bit++)
            {
                if (bit == targetBit) { continue; }
                controls[count++] = work[bit];
                if ((pattern & (1 << bit)) == 0) { circuit.X(work[bit]); }
            }

            ReversibleGates.MultiControlledX(circuit, controls, work[targetBit], scratch);
            for (int bit = 0; bit < work.Length; bit++)
            {
                if (bit != targetBit && (pattern & (1 << bit)) == 0) { circuit.X(work[bit]); }
            }
        }
    }

    private static int WorkWidth(int modulus) => BitOperations.Log2((uint)(modulus - 1)) + 1;

    private static void ValidateArithmetic(int modulus, int multiplier)
    {
        if (modulus is < 3 or > 15 || modulus % 2 == 0) { throw new ArgumentOutOfRangeException(nameof(modulus)); }
        if (multiplier < 1 || multiplier >= modulus || Gcd(multiplier, modulus) != 1)
        {
            throw new ArgumentOutOfRangeException(nameof(multiplier), "Multiplier must be a coprime residue in [1,modulus).");
        }
    }

    private static int Gcd(int first, int second)
    {
        while (second != 0) { (first, second) = (second, first % second); }
        return Math.Abs(first);
    }
}

internal sealed record ShorResult(int PhysicalQubits, int Gates, IReadOnlyDictionary<int, int> CountingOutcomes,
    (int First, int Second)? Factors);
