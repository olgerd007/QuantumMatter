namespace QuantumMatter.Infrastructure;

/// <summary>Immutable CPU scheduling policy; SIMD acceleration is explicitly opt-in.</summary>
public sealed record ParallelCpuOptions
{
    /// <summary>Worker cap in [1,256]; defaults to the smaller of four and the logical processor count.</summary>
    public int MaxDegreeOfParallelism { get; init; } = Math.Min(Environment.ProcessorCount, 4);
    /// <summary>Parallelization threshold in [1,30] qubits, default 16.</summary>
    public int MinimumParallelQubits { get; init; } = 16;
    /// <summary>Enables supported SIMD gate kernels with portable scalar and Hadamard fallbacks.</summary>
    public bool EnableSimd { get; init; }
    /// <summary>Requests supported 512-bit SIMD kernels when EnableSimd is true; disabled by default, with portable fallback.</summary>
    public bool EnableAvx512 { get; init; }
    /// <summary>Reuses tiles across up to 32 adjacent uncontrolled same-target gates; disabled by default.</summary>
    /// <remarks>High-target tiles use at most 16 KiB of stack workspace per active worker, outside the state-array budget.</remarks>
    public bool EnableGateBlocking { get; init; }
}
