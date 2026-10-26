using QuantumMatter.Application;
using QuantumMatter.Core;
using QuantumMatter.Demo;
using QuantumMatter.Infrastructure;
using Xunit;

namespace QuantumMatter.Tests;

public sealed class DemoExecutionPresetTests
{
    private const long GiB = 1024L * 1024 * 1024;

    [Fact]
    public void Default_preserves_existing_memory_and_worker_limits()
    {
        var preset = DemoExecutionPreset.Create("default");
        Assert.Equal(new SimulationOptions(), preset.Simulation);
        Assert.Equal(new ParallelCpuOptions(), preset.ParallelCpu);
        Assert.Equal(new CudaBackendOptions(), preset.Cuda);
        Assert.Equal(25, preset.MaximumCpuQubits(sample: false));
        Assert.Equal(24, preset.MaximumCpuQubits(sample: true));
        Assert.Equal(25, preset.MaximumCudaQubits(sample: false));
        Assert.Equal(24, preset.MaximumCudaQubits(sample: true));
    }

    [Fact]
    public void Workstation_limits_account_for_sampling_staging_and_sdk_cap_without_large_allocations()
    {
        var preset = DemoExecutionPreset.Create("workstation");
        Assert.Equal(32 * GiB, preset.Simulation.MemoryBudgetBytes);
        Assert.Equal(12 * GiB, preset.Cuda.DeviceMemoryBudgetBytes);
        Assert.Equal(12 * GiB, preset.Cuda.StagingMemoryBudgetBytes);
        Assert.Equal(Math.Min(12, Environment.ProcessorCount), preset.ParallelCpu.MaxDegreeOfParallelism);
        Assert.Equal(16, preset.ParallelCpu.MinimumParallelQubits);
        Assert.False(preset.ParallelCpu.EnableSimd);
        Assert.Equal(30, preset.MaximumCpuQubits(sample: false));
        Assert.Equal(30, preset.MaximumCpuQubits(sample: true));
        Assert.Equal(29, preset.MaximumCudaQubits(sample: false));
        Assert.Equal(29, preset.MaximumCudaQubits(sample: true));
        Assert.Equal(16 * GiB, StateVector.RequiredBytes(30));
        Assert.True(StateVector.RequiredBytes(30) + 8L * (1L << 30) <= preset.Simulation.MemoryBudgetBytes);
        Assert.True(StateVector.RequiredBytes(29) <= preset.Cuda.DeviceMemoryBudgetBytes);
        Assert.True(StateVector.RequiredBytes(30) > preset.Cuda.DeviceMemoryBudgetBytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => StateVector.RequiredBytes(31));
    }

    [Fact]
    public void Cuda_limit_obeys_the_smallest_budget_including_host_cdf()
    {
        var preset = DemoExecutionPreset.Create("workstation");
        var smallStaging = preset with { Cuda = preset.Cuda with { StagingMemoryBudgetBytes = 64 } };
        Assert.Equal(2, smallStaging.MaximumCudaQubits(sample: false));
        Assert.Equal(2, smallStaging.MaximumCudaQubits(sample: true));
        var smallHost = preset with { Simulation = preset.Simulation with { MemoryBudgetBytes = 64 } };
        Assert.Equal(2, smallHost.MaximumCudaQubits(sample: false));
        Assert.Equal(1, smallHost.MaximumCudaQubits(sample: true));
    }

    [Fact]
    public void Workstation_options_execute_a_small_circuit_and_terminal_sample()
    {
        var preset = DemoExecutionPreset.Create("workstation");
        var backend = new ParallelCpuStateVectorBackend(preset.ParallelCpu);
        var simulator = new StateVectorSimulator(backend, preset.Simulation);
        var circuit = new QuantumCircuit(2).H(0).CNot(0, 1);
        var result = simulator.Run(circuit, shots: 128, seed: 42);
        Assert.Equal(128, result.Counts.Values.Sum());
        Assert.All(result.Counts.Keys, bits => Assert.Contains(bits, new[] { "00", "11" }));
        Assert.InRange(Math.Abs(result.FinalState.Probability(0) - 0.5), 0, 1e-12);
        Assert.InRange(Math.Abs(result.FinalState.Probability(3) - 0.5), 0, 1e-12);
    }

#if CUDA_TESTS
    [Fact]
    public void Workstation_cuda_options_execute_on_real_hardware()
    {
        var preset = DemoExecutionPreset.Create("workstation");
        using var backend = new CudaStateVectorBackend(preset.Cuda);
        var simulator = new StateVectorSimulator(backend, preset.Simulation);
        var state = simulator.Simulate(new QuantumCircuit(2).H(0).CNot(0, 1));
        Assert.InRange(Math.Abs(state.Probability(0) - 0.5), 0, 1e-12);
        Assert.InRange(Math.Abs(state.Probability(3) - 0.5), 0, 1e-12);
    }
#endif
}
