using System.Numerics;

namespace QuantumMatter.Core.Noise;

/// <summary>Separate domain boundary for stochastic, nonunitary pure-state trajectories.</summary>
public interface INoiseChannelBackend
{
    /// <summary>
    /// Samples one channel branch on a valid normalized pure state and writes its normalized
    /// conditional state. Validate inputs before mutation, honor cancellation, and never retain
    /// the state or random source. Cancellation can leave partial output. No concurrent mutation.
    /// </summary>
    void Apply(NoiseChannel channel, int target, Memory<Complex> amplitudes, Random random,
        CancellationToken cancellationToken = default);
}
