using System.Numerics;

namespace QuantumMatter.Core;

/// <summary>Domain boundary for whole-circuit, in-place unitary evolution.</summary>
public interface IStateVectorBackend
{
    /// <summary>
    /// Evolves the supplied state in insertion order. Validate buffer size, honor cancellation,
    /// preserve normalization, and never retain the buffer after returning. The caller must not
    /// mutate the circuit or buffer concurrently. Backends are trusted code.
    /// </summary>
    void Execute(QuantumCircuit circuit, Memory<Complex> amplitudes, CancellationToken cancellationToken = default);
}
