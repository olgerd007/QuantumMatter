using QuantumMatter.Core.Observables;

namespace QuantumMatter.Core;

public sealed partial class StateVector
{
    /// <summary>Returns the real normalized expectation of a Pauli Hamiltonian without modifying amplitudes.</summary>
    /// <remarks>Uses O((terms+1)*2^n) work and constant auxiliary storage; an empty Hamiltonian still validates Born mass.</remarks>
    /// <exception cref="InvalidOperationException">Born mass or intermediate energy arithmetic is nonfinite or invalid.</exception>
    public double ExpectationEnergy(PauliHamiltonian hamiltonian, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hamiltonian);
        if (hamiltonian.QubitCount != QubitCount)
        {
            throw new ArgumentException("Hamiltonian and state sizes must match.", nameof(hamiltonian));
        }

        double mass = 0;
        for (int index = 0; index < Length; index++)
        {
            if ((index & 4095) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
            mass += Probability(index);
        }

        ValidateBornMass(mass);
        double sum = 0, correction = 0;
        foreach (PauliTerm term in hamiltonian.Terms)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (term.Coefficient == 0) { continue; }
            double value = term.Coefficient * ExpectationValue(term.Observable, cancellationToken);
            double next = sum + value;
            // Neumaier summation retains small terms between large, canceling contributions.
            correction += Math.Abs(sum) >= Math.Abs(value) ? (sum - next) + value : (value - next) + sum;
            sum = next;
            if (!double.IsFinite(sum) || !double.IsFinite(correction))
            {
                throw new InvalidOperationException("The energy accumulation is nonfinite.");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        double energy = sum + correction;
        if (!double.IsFinite(energy)) { throw new InvalidOperationException("The energy is nonfinite."); }
        return energy;
    }
}
