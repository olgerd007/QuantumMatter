using QuantumMatter.Core.Dynamic;

namespace QuantumMatter.Core;

public sealed partial class StateVector
{
    /// <summary>Evolves one dynamic trajectory in place and returns its final packed classical register.</summary>
    /// <remarks>
    /// Each call starts classical bits at zero and preserves the incoming quantum state.
    /// Cancellation or backend failure can leave a partially evolved state; no classical result is returned on failure.
    /// </remarks>
    public int EvolveDynamic(DynamicCircuit circuit, IStateVectorBackend backend, Random random,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(random);
        if (circuit.QubitCount != QubitCount)
        {
            throw new ArgumentException("Circuit and state sizes must match.", nameof(circuit));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return circuit.Execute(this, backend, random, cancellationToken);
    }
}
