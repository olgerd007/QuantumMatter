using QuantumMatter.Core;

namespace QuantumMatter.Application.Algorithms;

/// <summary>Single-solution amplitude amplification using an explicit marked basis index.</summary>
public static class GroverSearch
{
    /// <summary>Maximum logical input size supported by the synthesized oracle.</summary>
    public const int MaximumInputQubits = BasisStatePhaseOracle.MaximumInputQubits;
    /// <summary>Maximum synthesized operation count, checked before circuit construction.</summary>
    public const int MaximumCircuitOperations = BasisStatePhaseOracle.MaximumCircuitOperations;

    /// <summary>Returns the nearest nonnegative single-solution iteration count to the analytic optimum.</summary>
    public static int RecommendedIterations(int inputQubits)
    {
        BasisStatePhaseOracle.RequiredQubits(inputQubits);
        double theta = Math.Asin(1 / Math.Sqrt(1 << inputQubits));
        return Math.Max(0, (int)Math.Round(Math.PI / (4 * theta) - 0.5));
    }

    /// <summary>Input bits occupy [0,inputQubits); higher bits are scratch and return to zero.</summary>
    public static QuantumCircuit CreateCircuit(int inputQubits, int markedBasisIndex, int? iterations = null,
        CancellationToken cancellationToken = default)
    {
        int totalQubits = BasisStatePhaseOracle.RequiredQubits(inputQubits);
        if ((uint)markedBasisIndex >= (uint)(1 << inputQubits))
        {
            throw new ArgumentOutOfRangeException(nameof(markedBasisIndex));
        }

        int rounds = iterations ?? RecommendedIterations(inputQubits);
        ArgumentOutOfRangeException.ThrowIfNegative(rounds, nameof(iterations));
        long perRound = BasisStatePhaseOracle.OperationCount(inputQubits, markedBasisIndex)
            + 2L * inputQubits + BasisStatePhaseOracle.OperationCount(inputQubits, 0) + 1;
        if (inputQubits + rounds * perRound > MaximumCircuitOperations)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations), "Synthesized circuit exceeds the operation limit.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var circuit = new QuantumCircuit(totalQubits);
        Hadamards(circuit, inputQubits, cancellationToken);
        for (int round = 0; round < rounds; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BasisStatePhaseOracle.Append(circuit, inputQubits, markedBasisIndex, cancellationToken);
            Hadamards(circuit, inputQubits, cancellationToken);
            BasisStatePhaseOracle.Append(circuit, inputQubits, 0, cancellationToken);
            Hadamards(circuit, inputQubits, cancellationToken);
            // The marked-zero reflection gives I - 2|s><s|; negate it for the exact diffuser.
            BasisStatePhaseOracle.AppendGlobalNegation(circuit);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return circuit;
    }

    private static void Hadamards(QuantumCircuit circuit, int inputQubits, CancellationToken cancellationToken)
    {
        for (int qubit = 0; qubit < inputQubits; qubit++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            circuit.H(qubit);
        }
    }
}
