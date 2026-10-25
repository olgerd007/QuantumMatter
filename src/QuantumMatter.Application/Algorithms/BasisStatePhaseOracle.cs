using System.Numerics;
using QuantumMatter.Core;

namespace QuantumMatter.Application.Algorithms;

/// <summary>Gate synthesis on low input bits and initially-zero scratch bits above them.</summary>
internal static class BasisStatePhaseOracle
{
    internal const int MaximumInputQubits = 16;
    internal const int MaximumCircuitOperations = 1_000_000;
    private static readonly SingleQubitUnitary NegativeIdentity = SingleQubitUnitary.Create(-1, 0, 0, -1);

    internal static int RequiredQubits(int inputQubits)
    {
        if (inputQubits is < 1 or > MaximumInputQubits)
        {
            throw new ArgumentOutOfRangeException(nameof(inputQubits), "Phase-oracle algorithms support 1 through 16 input qubits.");
        }

        return inputQubits + Math.Max(0, inputQubits - 2);
    }

    internal static int OperationCount(int inputQubits, int basisIndex) =>
        2 * (inputQubits - BitOperations.PopCount((uint)basisIndex)) + (inputQubits <= 2 ? 1 : 2 * (inputQubits - 2) + 1);

    internal static void AppendGlobalNegation(QuantumCircuit circuit) => circuit.U(0, NegativeIdentity);

    internal static void Append(QuantumCircuit circuit, int inputQubits, int basisIndex, CancellationToken cancellationToken)
    {
        FlipZeroBits(circuit, inputQubits, basisIndex, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (inputQubits == 1)
        {
            circuit.Z(0);
        }
        else if (inputQubits == 2)
        {
            circuit.CZ(0, 1);
        }
        else
        {
            // Compute the conjunction of all input bits except the final target.
            circuit.Toffoli(0, 1, inputQubits);
            for (int input = 2; input < inputQubits - 1; input++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                circuit.Toffoli(inputQubits + input - 2, input, inputQubits + input - 1);
            }

            circuit.CZ(2 * inputQubits - 3, inputQubits - 1);
            for (int input = inputQubits - 2; input >= 2; input--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                circuit.Toffoli(inputQubits + input - 2, input, inputQubits + input - 1);
            }

            circuit.Toffoli(0, 1, inputQubits);
        }

        FlipZeroBits(circuit, inputQubits, basisIndex, cancellationToken);
    }

    private static void FlipZeroBits(QuantumCircuit circuit, int inputQubits, int basisIndex, CancellationToken cancellationToken)
    {
        for (int qubit = 0; qubit < inputQubits; qubit++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((basisIndex & (1 << qubit)) == 0)
            {
                circuit.X(qubit);
            }
        }
    }
}
