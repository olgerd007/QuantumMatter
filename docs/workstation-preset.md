# Example workstation allocation preset

The Demo's opt-in `workstation` preset configures the existing SDK options without
changing package defaults, precision, mathematical behavior or backend selection.
Budgets assume a 64-bit process and one active large simulation. These values are
example allocation policies, not a description of any developer's hardware,
reservations, or checks of free memory. Adjust them to the execution environment.

```powershell
# Calculate limits without allocating a state or initializing CUDA.
dotnet run --project src/QuantumMatter.Demo -- limits parallel workstation

# Use up to 12 CPU workers; parallel execution still starts at 16 qubits.
dotnet run --project src/QuantumMatter.Demo -- channel-assignment parallel workstation

# Explicitly opt into portable SIMD, or select an NVIDIA CUDA backend.
dotnet run --project src/QuantumMatter.Demo -- all parallel-simd workstation
dotnet run --project src/QuantumMatter.Demo -- all cuda workstation
```

The third positional argument is `default` or `workstation`. Omitting it preserves
the SDK's existing 512 MiB budgets and default worker policy. A preset does not
increase the qubit count of an example. `limits` reports both CPU and CUDA policy
ceilings without discovering hardware; choosing `cuda` for an actual simulation
requires a compatible NVIDIA GPU and driver. There is no automatic CPU fallback.

## Settings

| Setting | Workstation value |
| --- | ---: |
| Application host-array budget | 32 GiB |
| CPU worker cap | min(12, Environment.ProcessorCount) |
| CPU parallel threshold | 16 qubits |
| SIMD | Off unless `parallel-simd` is selected |
| CUDA device state-array budget | 12 GiB |
| CUDA host staging-array budget | 12 GiB |
| CUDA device index | 0 among compatible discovered devices |

The CPU worker cap is a scheduling choice, not a measured optimum. Twelve workers
may not outperform fewer workers for a memory-bound circuit. SIMD remains opt-in.
The application and staging budgets are separate allowances, not physical memory
capacities. Leave additional memory for the runtime, driver, display and other
applications; this preset makes no claim about available RAM or VRAM.

## Qubit limits

Double-precision Complex amplitudes cost `16 * 2^n` bytes. Terminal `Run` adds a
CDF of `8 * 2^n` bytes. CUDA additionally uses a host staging array and device
state, each `16 * 2^n` bytes. Staging and CDF need not be live simultaneously, but
staging is not pooled and collection is not forced before CDF allocation; the
host figures below conservatively allow both.

| Qubits | CPU state | CPU state + CDF | CUDA host state + staging + CDF | CUDA device state |
| --- | ---: | ---: | ---: | ---: |
| 26 | 1 GiB | 1.5 GiB | 2.5 GiB | 1 GiB |
| 27 | 2 GiB | 3 GiB | 5 GiB | 2 GiB |
| 28 | 4 GiB | 6 GiB | 10 GiB | 4 GiB |
| 29 | 8 GiB | 12 GiB | 20 GiB | 8 GiB |
| 30 | 16 GiB | 24 GiB | 40 GiB | 16 GiB |

**The configured ceiling is 30 physical qubits on CPU and 29 on CUDA.** Both
state-only simulation and terminal sampling fit those array policies. A 30-qubit
GPU state requires 16 GiB before driver/display overhead and exceeds the preset's
12 GiB budget. Extra host RAM cannot extend device capacity;
the current backend does not spill states to host memory or combine multiple GPUs.

The SDK itself caps registers at 30 qubits. For comparison, 31 qubits would need
32 GiB for a state and 48 GiB with a CDF, but this implementation rejects 31 before
allocation. Raising the cap would require a separate indexing/runtime/backend
review and validation; this preset does not change it.

These are physical state-register counts. Algorithm scratch qubits count against
them. For example, the current Grover and Deutsch-Jozsa builders use
`m = n + max(0,n-2)` physical qubits: the memory/register ceilings permit up to
16 logical inputs on CPU or 15 on CUDA, still subject to circuit-generation limits
and runtime cost. The six-transmitter application continues to use six qubits.

Noise probability estimation adds two double arrays, totaling `32 * 2^n` host
bytes; at 30 qubits these alone fill the 32 GiB application allowance. Dynamic and
noisy workflows re-execute trajectories per shot. Variational gradients run many
state evolutions and include parameter/term buffers. A register that fits memory
may be too slow for a useful workload, especially with many terms, gates or shots.
No large-register throughput or allocation-success claim is made by this preset.

Snapshots add another full state. Counts dictionaries, circuit objects, CLR/GC,
worker stacks, CUDA context/compiler and native driver overhead are outside the
array budgets. Limits apply per execution/backend, not to total process memory;
several concurrent large runs can exceed the machine's capacity. RAM and VRAM
figures use GiB (`1024^3` bytes), and actual available capacity may be lower.

## Equivalent SDK composition

For a separate application, use the same existing options at its composition root:

```csharp
const long GiB = 1024L * 1024 * 1024;
var options = new SimulationOptions { MemoryBudgetBytes = 32 * GiB };
var cpu = new ParallelCpuStateVectorBackend(new ParallelCpuOptions
{
    MaxDegreeOfParallelism = Math.Min(12, Environment.ProcessorCount),
    MinimumParallelQubits = 16,
    EnableSimd = false
});
var cpuSimulator = new StateVectorSimulator(cpu, options);

using var cuda = new CudaStateVectorBackend(new CudaBackendOptions
{
    DeviceMemoryBudgetBytes = 12 * GiB,
    StagingMemoryBudgetBytes = 12 * GiB
});
var gpuSimulator = new StateVectorSimulator(cuda, options);
```

The caller chooses and owns the backend. All Demo workflows pass the selected
application budget through their existing constructors, including noise, dynamic,
noisy dynamic and variational evaluation.
