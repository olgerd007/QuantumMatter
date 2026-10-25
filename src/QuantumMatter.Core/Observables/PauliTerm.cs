namespace QuantumMatter.Core.Observables;

/// <summary>An immutable real coefficient multiplying one Hermitian Pauli string.</summary>
public sealed class PauliTerm
{
    /// <summary>Creates a term with a finite coefficient and a nonnull observable.</summary>
    public PauliTerm(double coefficient, PauliString observable)
    {
        ArgumentNullException.ThrowIfNull(observable);
        if (!double.IsFinite(coefficient)) { throw new ArgumentOutOfRangeException(nameof(coefficient)); }
        Coefficient = coefficient;
        Observable = observable;
    }

    /// <summary>The finite real coefficient; zero and negative coefficients are allowed.</summary>
    public double Coefficient { get; }
    /// <summary>The immutable Pauli tensor product, with qubit zero at the right.</summary>
    public PauliString Observable { get; }
}
