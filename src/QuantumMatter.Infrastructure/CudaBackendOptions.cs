namespace QuantumMatter.Infrastructure;

/// <summary>Per-backend limits, separate from the simulator's host state/CDF budget.</summary>
public sealed record CudaBackendOptions
{
    /// <summary>Nonnegative ordinal in CudaStateVectorBackend.GetDevices, default zero.</summary>
    public int DeviceIndex { get; init; }
    /// <summary>Maximum device state-array bytes, at least 32; default 512 MiB.</summary>
    public long DeviceMemoryBudgetBytes { get; init; } = 512L * 1024 * 1024;
    /// <summary>Maximum host staging-array bytes, at least 32; default 512 MiB.</summary>
    public long StagingMemoryBudgetBytes { get; init; } = 512L * 1024 * 1024;
    /// <summary>Enables CUDA event timing for ExecuteProfiled; disabled by default.</summary>
    public bool EnableProfiling { get; init; }

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(DeviceIndex);
        ArgumentOutOfRangeException.ThrowIfLessThan(DeviceMemoryBudgetBytes, 32);
        ArgumentOutOfRangeException.ThrowIfLessThan(StagingMemoryBudgetBytes, 32);
    }
}

/// <summary>A discovered compatible CUDA device, without exposing driver-specific types.</summary>
/// <param name="Index">Ordinal among compatible visible devices.</param>
/// <param name="Name">Driver-provided device name.</param>
/// <param name="Architecture">Device compute architecture, for example SM_89.</param>
/// <param name="CudaDriverVersion">CUDA API version reported by the driver, not the NVIDIA display driver version.</param>
/// <param name="MemoryBytes">Total device memory; not currently free memory.</param>
public sealed record CudaDeviceInfo(int Index, string Name, string Architecture, string CudaDriverVersion, long MemoryBytes);

/// <summary>CUDA event interval around the gate sequence; includes dispatch gaps, excludes transfers.</summary>
/// <param name="DeviceEvolutionTime">Kernel-sequence event interval, not summed isolated kernel durations.</param>
/// <param name="StateBytes">Bytes in each device state and host staging array.</param>
/// <param name="KernelLaunches">Nonidentity gates dispatched.</param>
public sealed record CudaExecutionProfile(TimeSpan DeviceEvolutionTime, long StateBytes, int KernelLaunches);
