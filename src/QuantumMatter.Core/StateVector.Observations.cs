using System.Numerics;

namespace QuantumMatter.Core;

public sealed partial class StateVector
{
    /// <summary>Returns a normalized marginal Born probability without modifying the state.</summary>
    /// <remarks>Bit k of outcome refers to qubits[k]. Select at least one distinct, in-range qubit.</remarks>
    /// <exception cref="InvalidOperationException">The total Born mass is nonfinite or nonpositive.</exception>
    public double MarginalProbability(ReadOnlySpan<int> qubits, int outcome, CancellationToken cancellationToken = default)
    {
        int mask = ValidateSelection(qubits);
        if ((uint)outcome >= (uint)(1 << qubits.Length))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        int pattern = 0;
        for (int bit = 0; bit < qubits.Length; bit++)
        {
            pattern |= ((outcome >> bit) & 1) << qubits[bit];
        }

        double total = 0;
        double selected = 0;
        for (int index = 0; index < Length; index++)
        {
            if ((index & 4095) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
            double probability = Probability(index);
            total += probability;
            if ((index & mask) == pattern) { selected += probability; }
        }

        ValidateBornMass(total);
        return selected / total;
    }

    /// <summary>Measures an ordered subset and projects the state, preserving surviving relative and global phase.</summary>
    /// <remarks>
    /// Bit k of the returned outcome refers to qubits[k]. Samples one full-basis Born draw and retains
    /// the entire matching subset branch, including spectator coherence. Uses constant auxiliary space.
    /// Cancellation before the final projection leaves the state unchanged; the O(2^n) commit is noncancellable.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The total Born mass is nonfinite or nonpositive.</exception>
    public int Measure(ReadOnlySpan<int> qubits, Random random, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(random);
        int mask = ValidateSelection(qubits);
        double total = 0;
        int lastSupported = -1;
        for (int index = 0; index < Length; index++)
        {
            if ((index & 4095) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
            double probability = Probability(index);
            total += probability;
            if (probability > 0) { lastSupported = index; }
        }

        ValidateBornMass(total);
        cancellationToken.ThrowIfCancellationRequested();
        double draw = random.NextDouble() * total;
        double cumulative = 0;
        int basis = lastSupported;
        for (int index = 0; index < Length; index++)
        {
            if ((index & 4095) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
            cumulative += Probability(index);
            if (draw < cumulative)
            {
                basis = index;
                break;
            }
        }

        int pattern = basis & mask;
        double branchMass = 0;
        for (int index = 0; index < Length; index++)
        {
            if ((index & 4095) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
            if ((index & mask) == pattern) { branchMass += Probability(index); }
        }

        ValidateBornMass(branchMass);
        double norm = Math.Sqrt(branchMass);
        cancellationToken.ThrowIfCancellationRequested();
        // Commit only after all validation and cancellable scans. No scratch state is required.
        for (int index = 0; index < Length; index++)
        {
            Complex value = _amplitudes[index];
            _amplitudes[index] = (index & mask) == pattern
                ? new Complex(value.Real / norm, value.Imaginary / norm)
                : Complex.Zero;
        }

        int outcome = 0;
        for (int bit = 0; bit < qubits.Length; bit++)
        {
            outcome |= ((basis >> qubits[bit]) & 1) << bit;
        }

        return outcome;
    }

    /// <summary>Returns real &lt;psi|P|psi&gt;/&lt;psi|psi&gt; without sampling or changing the state.</summary>
    /// <remarks>The Pauli string must match the register size. Runs in O(2^n) time and constant auxiliary space.</remarks>
    /// <exception cref="InvalidOperationException">The total Born mass or observable accumulation is invalid.</exception>
    public double ExpectationValue(PauliString observable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observable);
        if (observable.QubitCount != QubitCount)
        {
            throw new ArgumentException("Observable and state sizes must match.", nameof(observable));
        }

        double total = 0;
        double expectation = 0;
        for (int index = 0; index < Length; index++)
        {
            if ((index & 4095) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
            total += Probability(index);
            // P|j> = i^(number of Y factors) (-1)^popcount(j & signMask) |j XOR flipMask>.
            Complex overlap = Complex.Conjugate(_amplitudes[index ^ observable.FlipMask]) * _amplitudes[index];
            double contribution = (observable.YCount & 3) switch
            {
                0 => overlap.Real,
                1 => -overlap.Imaginary,
                2 => -overlap.Real,
                _ => overlap.Imaginary
            };
            expectation += (BitOperations.PopCount((uint)(index & observable.SignMask)) & 1) == 0
                ? contribution : -contribution;
        }

        ValidateBornMass(total);
        if (!double.IsFinite(expectation))
        {
            throw new InvalidOperationException("The observable accumulation is nonfinite.");
        }

        return expectation / total;
    }

    private int ValidateSelection(ReadOnlySpan<int> qubits)
    {
        if (qubits.Length < 1 || qubits.Length > QubitCount)
        {
            throw new ArgumentException("Select between one and QubitCount distinct qubits.", nameof(qubits));
        }

        int mask = 0;
        foreach (int qubit in qubits)
        {
            if ((uint)qubit >= (uint)QubitCount)
            {
                throw new ArgumentOutOfRangeException(nameof(qubits), "Selected qubits must be in range.");
            }

            int bit = 1 << qubit;
            if ((mask & bit) != 0)
            {
                throw new ArgumentException("Selected qubits must be distinct.", nameof(qubits));
            }

            mask |= bit;
        }

        return mask;
    }

    private static void ValidateBornMass(double mass)
    {
        if (!double.IsFinite(mass) || mass <= 0)
        {
            throw new InvalidOperationException("The state has no finite positive probability mass.");
        }
    }
}
