using System.Numerics;
using QuantumMatter.Core.Noise;

namespace QuantumMatter.Infrastructure;

/// <summary>Double-precision reference kernels for single-qubit stochastic noise trajectories.</summary>
public sealed class CpuNoiseChannelBackend : INoiseChannelBackend
{
    /// <inheritdoc />
    public void Apply(NoiseChannel channel, int target, Memory<Complex> amplitudes, Random random,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(random);
        if (amplitudes.Length < 2 || !BitOperations.IsPow2(amplitudes.Length))
        {
            throw new ArgumentException("State length must be a power of two, at least two.", nameof(amplitudes));
        }

        if ((uint)target >= (uint)BitOperations.Log2((uint)amplitudes.Length))
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (channel.Probability == 0)
        {
            return;
        }

        Span<Complex> state = amplitudes.Span;
        int bit = 1 << target;
        if (channel.Kind == NoiseKind.AmplitudeDamping)
        {
            ApplyDamping(state, bit, channel.Probability, random, cancellationToken);
            return;
        }

        // 0 = identity, 1 = X, 2 = Y, 3 = Z. Pauli mixtures need no renormalization.
        int pauli;
        if (channel.Kind == NoiseKind.Depolarizing)
        {
            double draw = random.NextDouble();
            double quarter = channel.Probability / 4;
            pauli = draw < quarter ? 1 : draw < 2 * quarter ? 2 : draw < 3 * quarter ? 3 : 0;
        }
        else
        {
            bool apply = channel.Probability == 1 || random.NextDouble() < channel.Probability;
            pauli = !apply ? 0 : channel.Kind == NoiseKind.BitFlip ? 1 : 3;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (pauli == 0)
        {
            return;
        }

        for (int pair = 0; pair < state.Length / 2; pair++)
        {
            if ((pair & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            int low = pair & (bit - 1);
            int first = ((pair - low) << 1) | low;
            int second = first | bit;
            Complex zero = state[first];
            Complex one = state[second];
            if (pauli == 3)
            {
                state[second] = -one;
            }
            else
            {
                state[first] = pauli == 1 ? one : new Complex(one.Imaginary, -one.Real);
                state[second] = pauli == 1 ? zero : new Complex(-zero.Imaginary, zero.Real);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void ApplyDamping(Span<Complex> state, int bit, double gamma, Random random,
        CancellationToken cancellationToken)
    {
        double zeroMass = 0, oneMass = 0, zeroCompensation = 0, oneCompensation = 0;
        for (int index = 0; index < state.Length; index++)
        {
            if ((index & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            Complex value = state[index];
            double mass = value.Real * value.Real + value.Imaginary * value.Imaginary;
            if ((index & bit) == 0)
            {
                AddMass(mass, ref zeroMass, ref zeroCompensation);
            }
            else
            {
                AddMass(mass, ref oneMass, ref oneCompensation);
            }
        }

        double jumpMass = gamma * oneMass;
        // Avoid cancellation in total - jumpMass when the no-jump branch is very small.
        double stayMass = zeroMass + (1 - gamma) * oneMass;
        double total = jumpMass + stayMass;
        if (!double.IsFinite(total) || total <= 0)
        {
            throw new InvalidOperationException("The state has no finite positive probability mass.");
        }

        bool jump = jumpMass > 0 && (stayMass == 0 || random.NextDouble() < jumpMass / total);
        double branchMass = jump ? jumpMass : stayMass;
        double scale = 1 / Math.Sqrt(branchMass);
        double jumpScale = Math.Sqrt(gamma) * scale;
        double stayScale = Math.Sqrt(1 - gamma) * scale;
        for (int pair = 0; pair < state.Length / 2; pair++)
        {
            if ((pair & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            int low = pair & (bit - 1);
            int first = ((pair - low) << 1) | low;
            int second = first | bit;
            if (jump)
            {
                state[first] = jumpScale * state[second];
                state[second] = Complex.Zero;
            }
            else
            {
                state[first] *= scale;
                state[second] *= stayScale;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void AddMass(double value, ref double sum, ref double compensation)
    {
        double corrected = value - compensation;
        double next = sum + corrected;
        compensation = (next - sum) - corrected;
        sum = next;
    }
}
