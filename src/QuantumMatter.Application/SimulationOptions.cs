using QuantumMatter.Core;

namespace QuantumMatter.Application;

/// <summary>Immutable host-array allocation policy shared by unitary and trajectory workflows.</summary>
public sealed record SimulationOptions
{
    /// <summary>Bounds state, sampling, trajectory-estimation and variational parameter/gradient arrays; excludes backend staging, runtime, circuit, counts and caller snapshots.</summary>
    public long MemoryBudgetBytes { get; init; } = StateVector.DefaultMemoryBudgetBytes;
}
