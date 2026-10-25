# NVIDIA CUDA execution (v0.6)

CudaStateVectorBackend implements IStateVectorBackend for the entire supported gate
set, including controlled gates, Toffoli, SWAP, and validated custom unitaries.
StateVectorSimulator, algorithms, measurement, and terminal sampling keep their
existing contracts. GPU execution is explicitly selected; it never silently falls
back to CPU.

## Requirements and dependency

Use .NET 9 in a 64-bit Windows or Linux process with a CUDA-capable NVIDIA GPU,
a working NVIDIA driver, and a device architecture/PTX combination recognized by
ILGPU 1.5.3. GetDevices reports the compatible devices available to this process.
Compatibility is determined by discovery and successful context/kernel creation;
we do not claim every NVIDIA model or driver version has been validated.

For v1.0, the supported CUDA platform is Windows x64. Linux CUDA execution remains
experimental until the complete hardware suite and packaged consumer pass on a
Linux NVIDIA runner. Capability discovery alone is not hardware validation.

Infrastructure references the pinned ILGPU package, a general C# GPU compiler and
runtime. Quantum kernels are implemented here from scratch. No external quantum
library, CUDA Toolkit installation, nvcc, or ILGPU.Algorithms package is needed.
ILGPU dynamically compiles C# kernels to PTX and uses the NVIDIA driver. See the
[ILGPU kernel example](https://github.com/m4rs-mt/ILGPU/blob/v1.5.3/Samples/SimpleKernel/Program.cs)
and [release notes](https://ilgpu.net/releases/).

Actual local validation: NVIDIA GeForce RTX 4070 Ti SUPER, 16,376 MiB reported VRAM,
compute capability 8.9 (SM_89), NVIDIA driver 610.88, CUDA driver API 13.3, Windows
build 26200, AMD Ryzen 9 7900X, .NET SDK 9.0.319/runtime 9.0.21. No toolkit was installed.
Linux builds are covered by the repository CI definition, but this work did not
execute remote CI or validate CUDA hardware on Linux.

## Selection and ownership

```csharp
using QuantumMatter.Application;
using QuantumMatter.Core;
using QuantumMatter.Infrastructure;

foreach (CudaDeviceInfo device in CudaStateVectorBackend.GetDevices())
{
    Console.WriteLine($"{device.Index}: {device.Name}, {device.Architecture}");
}

using var backend = new CudaStateVectorBackend(new CudaBackendOptions
{
    DeviceIndex = 0,
    DeviceMemoryBudgetBytes = 512L * 1024 * 1024,
    StagingMemoryBudgetBytes = 512L * 1024 * 1024
});
var simulator = new StateVectorSimulator(backend);
var circuit = new QuantumCircuit(2).H(0).CNot(0, 1);
var result = simulator.Run(circuit, shots: 1000, seed: 42);
```

DeviceIndex is the ordinal in GetDevices, which can change with device visibility.
Discovery returns an empty list when no compatible GPU/driver is found; technical
failures that escape driver discovery remain observable. Constructing an explicit
CUDA backend with no device throws PlatformNotSupportedException; an unavailable
device index throws ArgumentOutOfRangeException. Initialization/compiler/driver and
allocation failures propagate, with owned resources released. The demo reports
unavailable CUDA on stderr and returns 1; invalid CLI arguments return 2.

The backend owns its context and compiled kernel; the caller must dispose it.
Dispose waits for an active call, releases the accelerator/context, and is
idempotent. Later execution throws ObjectDisposedException. One instance can be
shared by independent callers, whose executions serialize through a cancellation-aware
semaphore. Separate instances own separate contexts and budgets. Do not mutate a
circuit or its input state while executing.

```sh
dotnet run --project src/QuantumMatter.Demo -- all cuda
```

## Mathematics, residency, and memory

Public state buffers use System.Numerics.Complex. Explicit conversion to an
internal struct of two doubles avoids depending on Complex's private memory layout.
Angles and matrix coefficients are computed once per gate on the host, shared with
the parallel CPU preparation path. The independent scalar reference remains separate.
ILGPU uses MathMode.Default, not fast or reduced-precision math. Complex phases and
normalization follow the existing [mathematical conventions](mathematics.md), with
no automatic renormalization. There is no float/single-precision option.

For N = 2^n, each Execute owns one 16N-byte device state and one 16N-byte managed
staging array. There are no extra amplitude scratch buffers. Each has a separate
512 MiB default budget, checked before allocation; device capacity is checked too.
This is in addition to the simulator's host state/CDF budget. For 24-qubit Run,
the host state and CDF need 384 MiB, staging needs 256 MiB, and VRAM needs 256 MiB.
Arrays remain subject to .NET length limits and actual available memory. These
budgets exclude object headers, driver/context/compiler/native transfer overhead,
stream/events, circuit, result counts, snapshots and other backend instances.
Managed staging becomes eligible for collection after the call; it is not pooled.

One initial upload and one final download surround the full circuit. Operations
launch in insertion order on a private stream; no gate transfers the full state
back to the CPU. A thread inserts a zero target bit into its pair index and owns
both amplitudes. Controlled branches filter the pair by its control mask. SWAP
selects only control-one/target-zero pairs and exchanges each opposite-bit pair
once. This gives one writer per amplitude and O(N) work per gate. Identity skips
dispatch. There is no gate fusion or multi-GPU execution.

Temporary streams and buffers are disposed on success, failure, and cancellation.
Queued work is synchronized before buffer release. Cancellation is checked during
input conversion, between gates, after device execution/download, and before host
commit. A synchronization barrier every 32 launches bounds queued work; active
kernels, allocations, transfers, and the final host commit are noncancellable.
Cancellation is cooperative and may wait for queued work. The caller's input is
unchanged if cancellation or a technical failure occurs before commit. Once the
last cancellation check passes, the commit completes without per-element checks.

## Hardware tests

```sh
dotnet restore QuantumMatter.sln
dotnet build QuantumMatter.sln --no-restore -p:EnableCudaTests=true
dotnet test QuantumMatter.sln --no-restore -p:EnableCudaTests=true
dotnet test QuantumMatter.sln --no-restore -c Release -p:EnableCudaTests=true
```

EnableCudaTests is a compile-time test flag. Allow the test project to rebuild when
changing it; do not combine a changed flag with --no-build. Enabling it requires
real CUDA hardware: context creation failures fail the suite rather than skipping
or using an emulator. A fixture shares/disposes a real backend across the inherited
66-case conformance suite. Twelve additional hardware cases cover arbitrary complex
offset buffers through 17 qubits, resource rejection/reuse, cancellation and host
failure after device work, concurrency, profiling, disposal, algorithms, and sampling.
The same dense oracle, complex matrix columns, exhaustive operand/basis checks,
global/relative phase, inverse and norm tolerances apply to CUDA.

Local v0.6 results: 582/582 pass in Debug and Release, including all 78 hardware cases.
The Release suite also passes with DOTNET_EnableHWIntrinsic=0. Default builds pass
504 cases and explicitly skip one hardware-suite marker. The normal CI matrix uses
that default mode and does not establish GPU correctness. With CUDA_VISIBLE_DEVICES=-1,
all 504 default cases pass with the hardware-suite marker skipped, and the CUDA
demo returns its expected unavailable-device error. This exercises the complete
CPU-compatible suite and no-visible-device path locally.

v0.7 adds hybrid trajectory checks for CUDA unitary segments and CPU noise channels.
The v0.7 hardware-enabled suite has 659 cases, including 79 CUDA cases. See the
[noise guide](noise.md) for separate result semantics and per-segment transfer costs.

## Measurements and reproduction

PowerShell reproduction (use an otherwise idle GPU and no concurrent tests):

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet run --project benchmarks/QuantumMatter.Benchmarks -c Release -- cuda docs/benchmarks/cuda-local.csv
```

The CUDA profile runs 24 size/depth/mix combinations: 12/18/22 qubits, 16/64 gates,
Hadamard and mixed gates at low/high target strides. Two warmups per backend and
case precede five repetitions, alternating CPU/GPU order. Initial states have
nonzero complex phases. Each result is checked against scalar execution; norms
use compensated summation. This comparison uses scalar CPU, not the fastest
parallel/SIMD configuration. It is a local baseline, not a universal speedup claim.

Total CUDA timing includes managed staging conversion/allocation, device
allocation/free, upload, dispatch/synchronization, download, host commit, and
profiling overhead. It excludes initial context/JIT, caller input reset, oracle
checks, and sampling. The device column is the CUDA event interval around the
kernel sequence: it excludes allocation and transfer but includes host dispatch
gaps and the 32-launch barriers. It is not a sum of isolated kernel execution times.
ExecuteProfiled requires EnableProfiling=true; ordinary Execute creates no events.

Selected medians from the 2026-10-25 run (64 gates, mixed-low):

| Qubits | Scalar CPU ms | CUDA total ms | CUDA device sequence ms | Total speedup |
| --- | ---: | ---: | ---: | ---: |
| 12 | 0.2072 | 1.0045 | 0.4361 | 0.21x |
| 18 | 13.6217 | 3.0074 | 0.7168 | 4.53x |
| 22 | 225.0143 | 60.3135 | 35.9552 | 3.73x |

Small registers are slower on CUDA. Large-register timings vary substantially on
this shared desktop/WDDM GPU, especially at 64 gates; the raw report retains min,
median and max. No automatic crossover threshold is inferred. Across all measured
cases, maximum absolute amplitude difference was 2.78e-17 and maximum norm error
was 8.89e-15. Each 22-qubit execution used a 64 MiB device array and 64 MiB host
staging array; reported managed allocation includes staging but excludes native
driver/context memory.

Raw evidence: [CSV](benchmarks/cuda-2026-10-25.csv) and
[hardware/method metadata](benchmarks/cuda-2026-10-25.csv.metadata.json).
Kernel event semantics follow ILGPU's
[profiling marker implementation](https://github.com/m4rs-mt/ILGPU/blob/v1.5.3/Src/ILGPU/Runtime/ProfilingMarker.cs).
