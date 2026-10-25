using QuantumMatter.Core.Dynamic;
using QuantumMatter.Core.Noise;

namespace QuantumMatter.Core;

public sealed partial class StateVector
{
    /// <summary>Evolves one noisy dynamic trajectory in place and returns its final packed classical register.</summary>
    /// <remarks>
    /// Classical bits start at zero while the incoming quantum state is preserved.
    /// Backend failure or cancellation can leave partial state changes; no classical result is returned on failure.
    /// </remarks>
    public int EvolveNoisyDynamic(NoisyDynamicCircuit circuit, IStateVectorBackend unitaryBackend,
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
        return circuit.Execute(this, unitaryBackend, noiseBackend, random, cancellationToken);
    }
}
