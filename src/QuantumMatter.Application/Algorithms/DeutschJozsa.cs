using System.Numerics;
using QuantumMatter.Core;

namespace QuantumMatter.Application.Algorithms;

/// <summary>Deutsch-Jozsa using a phase oracle synthesized from a promised Boolean truth table.</summary>
public static class DeutschJozsa
{
    /// <summary>Maximum logical input size accepted by truth-table synthesis.</summary>
    public const int MaximumInputQubits = BasisStatePhaseOracle.MaximumInputQubits;
    /// <summary>Maximum generated operation count, preflighted before circuit construction.</summary>
    public const int MaximumCircuitOperations = BasisStatePhaseOracle.MaximumCircuitOperations;

    /// <summary>Table index is the little-endian input value. Scratch bits are above the input register.</summary>
    public static QuantumCircuit CreateCircuit(IReadOnlyList<bool> truthTable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(truthTable);
        cancellationToken.ThrowIfCancellationRequested();
        int length = truthTable.Count;
        if (length < 2 || length > 1 << MaximumInputQubits || !BitOperations.IsPow2(length))
        {
            throw new ArgumentException("Provide a power-of-two table for 1 through 16 input qubits.", nameof(truthTable));
        }

        int inputQubits = BitOperations.Log2((uint)length);
        int totalQubits = BasisStatePhaseOracle.RequiredQubits(inputQubits);
        var values = new bool[length];
        int ones = 0;
        long oracleOperations = 0;
        for (int index = 0; index < length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            values[index] = truthTable[index];
            if (values[index])
            {
                ones++;
                oracleOperations += BasisStatePhaseOracle.OperationCount(inputQubits, index);
            }
        }

        if (ones != 0 && ones != length && ones != length / 2)
        {
            throw new ArgumentException("The truth table must be constant or balanced.", nameof(truthTable));
        }

        long operationCount = 2L * inputQubits + (ones == length ? 1 : oracleOperations);
        if (operationCount > MaximumCircuitOperations)
        {
            throw new ArgumentException("Synthesized circuit exceeds the operation limit.", nameof(truthTable));
        }

        var circuit = new QuantumCircuit(totalQubits);
        ApplyHadamards(circuit, inputQubits, cancellationToken);
        if (ones == length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BasisStatePhaseOracle.AppendGlobalNegation(circuit);
        }
        else
        {
            for (int index = 0; index < length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (values[index])
                {
                    BasisStatePhaseOracle.Append(circuit, inputQubits, index, cancellationToken);
                }
            }
        }

        ApplyHadamards(circuit, inputQubits, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return circuit;
    }

    private static void ApplyHadamards(QuantumCircuit circuit, int inputQubits, CancellationToken cancellationToken)
    {
        for (int qubit = 0; qubit < inputQubits; qubit++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            circuit.H(qubit);
        }
    }
}
