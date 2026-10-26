using QuantumMatter.Application;
using QuantumMatter.Core;
using QuantumMatter.Infrastructure;

namespace QuantumMatter.Demo;

internal sealed record DemoExecutionPreset(
    SimulationOptions Simulation,
    ParallelCpuOptions ParallelCpu,
    CudaBackendOptions Cuda)
{
    private const long GiB = 1024L * 1024 * 1024;

    internal static DemoExecutionPreset Create(string name) => name switch
    {
        "default" => new(new SimulationOptions(), new ParallelCpuOptions(), new CudaBackendOptions()),
        // Array budgets leave room for the OS, CLR, driver, counts and circuit objects on a 64/16 GiB machine.
        "workstation" => new(
            new SimulationOptions { MemoryBudgetBytes = 32 * GiB },
            new ParallelCpuOptions { MaxDegreeOfParallelism = Math.Min(12, Environment.ProcessorCount) },
            new CudaBackendOptions { DeviceMemoryBudgetBytes = 12 * GiB, StagingMemoryBudgetBytes = 12 * GiB }),
        _ => throw new ArgumentException("Unknown execution preset.", nameof(name))
    };

    internal int MaximumCpuQubits(bool sample) => MaximumQubits(sample ? 24 : 16, Simulation.MemoryBudgetBytes);

    internal int MaximumCudaQubits(bool sample) => Math.Min(MaximumCpuQubits(sample),
        MaximumQubits(16, Math.Min(Cuda.DeviceMemoryBudgetBytes, Cuda.StagingMemoryBudgetBytes)));

    internal void PrintLimits()
    {
        Console.WriteLine(FormattableString.Invariant($"Host array budget: {Simulation.MemoryBudgetBytes / (double)GiB:F2} GiB."));
        Console.WriteLine($"CPU worker cap: {ParallelCpu.MaxDegreeOfParallelism}; parallel threshold: {ParallelCpu.MinimumParallelQubits} qubits; SIMD: {ParallelCpu.EnableSimd}.");
        Console.WriteLine(FormattableString.Invariant($"CUDA budgets: device {Cuda.DeviceMemoryBudgetBytes / (double)GiB:F2} GiB; host staging {Cuda.StagingMemoryBudgetBytes / (double)GiB:F2} GiB."));
        foreach (bool sample in new[] { false, true })
        {
            int cpu = MaximumCpuQubits(sample);
            int cuda = MaximumCudaQubits(sample);
            string workflow = sample ? "Run (terminal sampling)" : "Simulate (state only)";
            double cpuHost = StateVector.RequiredBytes(cpu) * (sample ? 1.5 : 1) / GiB;
            double device = StateVector.RequiredBytes(cuda) / (double)GiB;
            // Staging and CDF lifetimes need not overlap, but collection is not forced between phases.
            double cudaHost = device * (sample ? 2.5 : 2);
            Console.WriteLine(FormattableString.Invariant($"CPU {workflow}: {cpu} qubits; host arrays {cpuHost:F2} GiB."));
            Console.WriteLine(FormattableString.Invariant($"CUDA {workflow}: {cuda} qubits; host arrays up to {cudaHost:F2} GiB; device state {device:F2} GiB."));
        }

        Console.WriteLine("Calculated allocation limits, capped by the SDK's 30-qubit contract; no large state or CUDA device was allocated.");
        Console.WriteLine("Actual free memory, driver/CLR overhead, snapshots and concurrent runs can lower these limits. No throughput guarantee.");
    }

    private static int MaximumQubits(int bytesPerAmplitude, long budget)
    {
        int maximum = 0;
        for (int qubits = 1; qubits <= 30; qubits++)
        {
            if (bytesPerAmplitude * (1L << qubits) > budget) { break; }
            maximum = qubits;
        }

        return maximum;
    }
}
