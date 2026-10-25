namespace QuantumMatter.Infrastructure;

/// <summary>Immutable CPU scheduling policy; SIMD acceleration is explicitly opt-in.</summary>
public sealed record ParallelCpuOptions
{
    /// <summary>Worker cap in [1,256]; defaults to the smaller of four and the logical processor count.</summary>
    public int MaxDegreeOfParallelism { get; init; } = Math.Min(Environment.ProcessorCount, 4);
    /// <summary>Parallelization threshold in [1,30] qubits, default 16.</summary>
    public int MinimumParallelQubits { get; init; } = 16;
    /// <summary>Enables supported SIMD Hadamard kernels with portable fallback.</summary>
    public bool EnableSimd { get; init; }
}
