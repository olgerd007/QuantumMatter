namespace QuantumMatter.Application.Variational;

/// <summary>An immutable exact-state energy and parameter gradient, subject to floating-point precision.</summary>
public sealed class VariationalEnergyResult
{
    internal VariationalEnergyResult(double energy, double[] gradient)
    {
        Energy = energy;
        Gradient = Array.AsReadOnly(gradient);
    }

    /// <summary>The normalized Hamiltonian expectation at the supplied parameters.</summary>
    public double Energy { get; }
    /// <summary>The immutable gradient in parameter-index order; unused parameters have zero derivative.</summary>
    public IReadOnlyList<double> Gradient { get; }
}
