namespace QuantumMatter.Core;

/// <summary>An immutable tensor product of I, X, Y and Z, with qubit zero at the right.</summary>
public sealed class PauliString
{
    /// <summary>Validates one through 30 uppercase Pauli labels; signs and coefficients are not accepted.</summary>
    public PauliString(string operators)
    {
        ArgumentNullException.ThrowIfNull(operators);
        if (operators.Length is < 1 or > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(operators), "A Pauli string requires 1 through 30 labels.");
        }

        for (int qubit = 0; qubit < operators.Length; qubit++)
        {
            char label = operators[operators.Length - 1 - qubit];
            if (label is not ('I' or 'X' or 'Y' or 'Z'))
            {
                throw new ArgumentException("Only uppercase I, X, Y and Z labels are supported.", nameof(operators));
            }

            if (label is 'X' or 'Y') { FlipMask |= 1 << qubit; }
            if (label is 'Z' or 'Y') { SignMask |= 1 << qubit; }
            if (label == 'Y') { YCount++; }
        }

        Operators = operators;
    }

    /// <summary>The validated labels, ordered from the highest qubit to qubit zero.</summary>
    public string Operators { get; }

    /// <summary>The number of qubits on which the observable acts, including identity factors.</summary>
    public int QubitCount => Operators.Length;

    internal int FlipMask { get; }
    internal int SignMask { get; }
    internal int YCount { get; }
}
