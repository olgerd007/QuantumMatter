using System.Diagnostics.CodeAnalysis;
using QuantumMatter.Core;

namespace QuantumMatter.Application.Algorithms;

/// <summary>Gate decomposition of F[y,x] = exp(2*pi*i*x*y/2^n) / sqrt(2^n).</summary>
public static class QuantumFourierTransform
{
    /// <summary>Builds a complete QFT or inverse for 1 through 30 qubits, including bit reversal.</summary>
    public static QuantumCircuit CreateCircuit(int qubits, bool inverse = false, CancellationToken cancellationToken = default)
    {
        var circuit = new QuantumCircuit(qubits);
        return Append(circuit, inverse, cancellationToken);
    }

    /// <summary>Appends QFT or inverse over the entire register; cancellation may leave partial appended gates.</summary>
    [SuppressMessage("ApiDesign", "RS0026:Do not add multiple public overloads with optional parameters",
        Justification = "Existing whole-register and required-subset overloads are unambiguous; preserve their signatures and defaults in 1.x.")]
    public static QuantumCircuit Append(QuantumCircuit circuit, bool inverse = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        return Append(circuit, Enumerable.Range(0, circuit.QubitCount).ToArray(), inverse, cancellationToken);
    }

    /// <summary>Qubits are ordered from the least to the most significant local bit.</summary>
    [SuppressMessage("ApiDesign", "RS0026:Do not add multiple public overloads with optional parameters",
        Justification = "The required qubits parameter distinguishes this existing overload; retain source compatibility in 1.x.")]
    public static QuantumCircuit Append(QuantumCircuit circuit, IReadOnlyList<int> qubits,
        bool inverse = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(qubits);
        cancellationToken.ThrowIfCancellationRequested();
        if (qubits.Count < 1 || qubits.Count > circuit.QubitCount)
        {
            throw new ArgumentException("Select a nonempty subset of the circuit's qubits.", nameof(qubits));
        }

        int[] register = qubits.ToArray();
        var distinct = new HashSet<int>();
        foreach (int qubit in register)
        {
            if ((uint)qubit >= (uint)circuit.QubitCount || !distinct.Add(qubit))
            {
                throw new ArgumentException("Selected qubits must be distinct and in range.", nameof(qubits));
            }
        }

        if (inverse)
        {
            ReverseBits(circuit, register, cancellationToken);
            for (int target = 0; target < register.Length; target++)
            {
                for (int control = 0; control < target; control++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    circuit.CPhase(register[control], register[target], -Math.ScaleB(Math.PI, control - target));
                }

                cancellationToken.ThrowIfCancellationRequested();
                circuit.H(register[target]);
            }
        }
        else
        {
            for (int target = register.Length - 1; target >= 0; target--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                circuit.H(register[target]);
                for (int control = target - 1; control >= 0; control--)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    circuit.CPhase(register[control], register[target], Math.ScaleB(Math.PI, control - target));
                }
            }

            ReverseBits(circuit, register, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return circuit;
    }

    private static void ReverseBits(QuantumCircuit circuit, int[] register, CancellationToken cancellationToken)
    {
        for (int first = 0; first < register.Length / 2; first++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            circuit.Swap(register[first], register[register.Length - first - 1]);
        }
    }
}
