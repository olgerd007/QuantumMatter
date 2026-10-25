using System.Numerics;
using QuantumMatter.Core.Noise;

namespace QuantumMatter.Core;

/// <summary>Owns a mutable double-precision pure state initialized to the all-zero basis state.</summary>
public sealed partial class StateVector
{
    /// <summary>Default host amplitude-array budget, 512 MiB.</summary>
    public const long DefaultMemoryBudgetBytes = 512L * 1024 * 1024;
    private readonly Complex[] _amplitudes;

    /// <summary>Allocates 16*2^qubits bytes after validating qubits in [1,30] and the host budget.</summary>
    public StateVector(int qubits, long memoryBudgetBytes = DefaultMemoryBudgetBytes)
    {
        long bytes = RequiredBytes(qubits);
        if (memoryBudgetBytes < bytes)
        {
            throw new ArgumentOutOfRangeException(nameof(memoryBudgetBytes), $"The state requires {bytes} bytes.");
        }

        QubitCount = qubits;
        _amplitudes = new Complex[1 << qubits];
        _amplitudes[0] = Complex.One;
    }

    /// <summary>The register size.</summary>
    public int QubitCount { get; }
    /// <summary>The number of amplitudes, 2^QubitCount.</summary>
    public int Length => _amplitudes.Length;

    /// <summary>Returns amplitude-array bytes for a register of 1 through 30 qubits without allocation.</summary>
    public static long RequiredBytes(int qubits)
    {
        if (qubits is < 1 or > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(qubits));
        }

        return 16L << qubits;
    }

    /// <summary>Returns an amplitude, including phase, at an in-range little-endian basis index.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The basis index is outside [0,Length).</exception>
    public Complex Amplitude(int basisIndex)
    {
        if ((uint)basisIndex >= (uint)Length)
        {
            throw new ArgumentOutOfRangeException(nameof(basisIndex));
        }

        return _amplitudes[basisIndex];
    }

    /// <summary>Returns the squared magnitude at an in-range basis index; no renormalization is performed.</summary>
    public double Probability(int basisIndex)
    {
        Complex value = Amplitude(basisIndex);
        return value.Real * value.Real + value.Imaginary * value.Imaginary;
    }

    /// <summary>Copies the amplitude array. The caller owns the copy and its additional memory cost.</summary>
    public Complex[] Snapshot() => (Complex[])_amplitudes.Clone();

    /// <summary>Restores |0...0>; array clearing is noncancellable after the initial token check.</summary>
    public void Reset(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Array.Clear(_amplitudes);
        _amplitudes[0] = Complex.One;
    }

    /// <summary>Produces one conditional pure trajectory, not the channel's ensemble state.</summary>
    public void EvolveTrajectory(NoisyCircuit circuit, IStateVectorBackend unitaryBackend,
        INoiseChannelBackend noiseBackend, Random random, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(unitaryBackend);
        ArgumentNullException.ThrowIfNull(noiseBackend);
        ArgumentNullException.ThrowIfNull(random);
        if (circuit.QubitCount != QubitCount)
        {
            throw new ArgumentException("Circuit and state sizes must match.", nameof(circuit));
        }

        cancellationToken.ThrowIfCancellationRequested();
        circuit.Execute(_amplitudes, unitaryBackend, noiseBackend, random, cancellationToken);
    }

    /// <summary>Evolves in place with a trusted synchronous backend. Cancellation can leave partial output.</summary>
    public void Evolve(QuantumCircuit circuit, IStateVectorBackend backend, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(backend);
        if (circuit.QubitCount != QubitCount)
        {
            throw new ArgumentException("Circuit and state sizes must match.", nameof(circuit));
        }

        cancellationToken.ThrowIfCancellationRequested();
        backend.Execute(circuit, _amplitudes, cancellationToken);
    }

    /// <summary>Samples the actual Born mass and collapses to one basis state with amplitude +1.</summary>
    /// <exception cref="InvalidOperationException">Probability mass is nonfinite or nonpositive.</exception>
    public int MeasureAll(Random random, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(random);
        double total = 0;
        int lastSupported = -1;
        for (int index = 0; index < Length; index++)
        {
            if ((index & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            double probability = Probability(index);
            total += probability;
            if (probability > 0)
            {
                lastSupported = index;
            }
        }

        if (!double.IsFinite(total) || total <= 0)
        {
            throw new InvalidOperationException("The state has no finite positive probability mass.");
        }

        double draw = random.NextDouble() * total;
        double cumulative = 0;
        int outcome = lastSupported;
        for (int index = 0; index < Length; index++)
        {
            if ((index & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            cumulative += Probability(index);
            if (draw < cumulative)
            {
                outcome = index;
                break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        Array.Clear(_amplitudes);
        _amplitudes[outcome] = Complex.One;
        return outcome;
    }
}
