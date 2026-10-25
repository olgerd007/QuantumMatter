namespace QuantumMatter.Core.Observables;

/// <summary>An immutable ordered real sum of register-sized Pauli terms.</summary>
public sealed class PauliHamiltonian
{
    /// <summary>Snapshots matching terms for 1–30 qubits; an empty list represents the zero operator.</summary>
    /// <remarks>Duplicate and zero terms are retained in input order, without coefficient aggregation.</remarks>
    public PauliHamiltonian(int qubits, IReadOnlyList<PauliTerm> terms)
    {
        StateVector.RequiredBytes(qubits);
        ArgumentNullException.ThrowIfNull(terms);
        var snapshot = new PauliTerm[terms.Count];
        for (int index = 0; index < snapshot.Length; index++)
        {
            PauliTerm term = terms[index];
            if (term is null || term.Observable.QubitCount != qubits)
            {
                throw new ArgumentException("Every term must be nonnull and match the register size.", nameof(terms));
            }

            snapshot[index] = term;
        }

        QubitCount = qubits;
        Terms = Array.AsReadOnly(snapshot);
    }

    /// <summary>The fixed register width, including identity factors.</summary>
    public int QubitCount { get; }
    /// <summary>The immutable snapshot of terms in supplied order.</summary>
    public IReadOnlyList<PauliTerm> Terms { get; }
}
