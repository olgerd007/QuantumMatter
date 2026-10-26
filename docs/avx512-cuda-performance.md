# AVX-512 and CUDA comparison — 2026-10-26

## Result

The new opt-in AVX-512 path and real CUDA execution pass the complete numerical
suites on .NET 9 and .NET 10 in an anonymized environment. Neither backend wins
every workload. Keep selection explicit. Host/device identities and physical
capacities are deliberately withheld from this public repository.

Matched CPU configurations at 20 qubits / 64 gates, eight workers, blocking on:

| Workload | Existing 256-bit path ms | AVX-512 ms | Speedup |
| --- | ---: | ---: | ---: |
| Repeated high-target H | 3.2887 | 2.0014 | 1.64x |
| Repeated high-target Rx | 8.6187 | 5.1890 | 1.66x |
| Repeated high-target Phase | 2.7488 | 2.4225 | 1.13x |
| Mixed, fixed target | 17.0782 | 16.5167 | 1.03x |
| Mixed, varying targets | 29.4716 | 28.7409 | 1.03x |

Small differences are subject to noise. AVX-512 can also regress: 20-qubit mixed
execution with four workers and blocking off takes 33.9700 ms on the existing path
versus 34.8087 ms with AVX-512. At 25 qubits / eight H gates with eight blocked
workers, the times are 24.2763 versus 20.2601 ms (1.20x). The opt-in flag remains
false by default; no global JIT, vector-width, GC or thread-pool settings change.

Selected fastest CPU configurations from the same interleaved run versus CUDA:

| Qubits | Gates | Workload | Best measured CPU ms | CUDA total ms | Separately profiled device-sequence ms |
| --- | ---: | --- | ---: | ---: | ---: |
| 12 | 64 | Mixed, fixed target | 0.0975 | 0.9010 | 0.4352 |
| 20 | 64 | Repeated high-target H | 2.0014 | 8.9646 | 3.0385 |
| 20 | 64 | Mixed, fixed target | 16.5167 | 8.9805 | 2.0963 |
| 20 | 64 | Mixed, varying targets | 28.7409 | 13.4981 | 13.8481 |
| 25 | 8 | Repeated high-target H | 20.2601 | 332.8412 | 50.2148 |
| 25 | 8 | Mixed, fixed target | 107.4285 | 396.6171 | 14.4255 |
| 25 | 8 | Mixed, varying targets | 100.3189 | 299.3089 | 28.4551 |

The best CPU above uses AVX-512 and eight workers with blocking, except the
12-qubit mixed case, which uses one worker without blocking. These are minima
across measured configuration medians, not an automatic selection policy or a
guaranteed optimum. CUDA wins the two selected 20-qubit mixed cases; optimized
CPU wins the selected small-state and 25-qubit shallow cases.

CUDA timing at 25 qubits varies substantially: mixed-high ranges from 239.8047 to
471.4492 ms across five samples. Keep the raw min/max values when interpreting
medians; the shared execution environment has uncontrolled scheduling, clock/power
policy and background activity. No universal CPU/GPU crossover is inferred.

## Implementation

`ParallelCpuOptions.EnableAvx512` is additive and false by default. It takes effect
only with EnableSimd. Infrastructure owns the fixed-width kernel, feature checks
and binary-layout probe. Core, Application and the backend port do not change.
The get/init accessors are explicitly added to PublicAPI.Unshipped; shipped
inventory, package targets and assembly binding version are preserved.

The kernel requires Avx512F.IsSupported, Vector512.IsHardwareAccelerated and the
existing verified Complex layout. It processes four complex values from each
contiguous half-block for uncontrolled H, general 2x2 matrices and X, and four
complex values per diagonal update. Real coefficients avoid imaginary products.
Complex multiplication swaps real/imaginary lanes using Permute2x64 with mask 0x55.
Loads/stores are unaligned and bounded; up to three complex tail values use scalar
arithmetic. Checks occur within 4096 work items. There is no FMA, reduced precision,
coefficient fusion, gate elimination or implicit renormalization.

Targets q0/q1, controls, SWAP, unsupported hardware and disabled runtime features
retain existing paths. Blocking continues to use its bounded 16 KiB stack tile;
local rebased targets use AVX-512 when their stride permits. The new kernels add
no amplitude arrays and do not change cancellation or backend ownership.

```csharp
var backend = new ParallelCpuStateVectorBackend(new ParallelCpuOptions
{
    MaxDegreeOfParallelism = 8,
    MinimumParallelQubits = 16,
    EnableSimd = true,
    EnableAvx512 = true,
    EnableGateBlocking = true
});
```

The CUDA runtime/kernel implementation is unchanged. It still keeps the state
resident for an entire circuit, uses double precision, and returns ownership of
the host buffer only after synchronization and commit. Contexts are caller-owned
IDisposable resources; there is no implicit CPU fallback.

## Evidence and method

- Host hardware and installation fingerprints are omitted. Performance measurements
  use the .NET 9 target; .NET 10 is validated for correctness and package compatibility,
  not timed. Requested kernel and worker settings remain in the raw data.
- Release with tiering disabled; two warmups per variant/case, five samples.
  All CPU/CUDA variants rotate and reverse execution order between repetitions.
- Twelve CPU configurations: one/four/eight workers, 256/512 requests, blocking
  off/on. The parallel threshold is forced to one to expose scheduling overhead.
  CUDA is the thirteenth variant. There are 143 result rows across eleven cases.
- Sizes are 12/20/25 qubits. Depth is 64 gates at 12/20 qubits and eight at 25.
  Same-target H and mixed circuits demonstrate blocking; varying-high changes
  targets each gate and includes controlled rotations, Toffoli and SWAP. The
  20-qubit Rx/Phase cases isolate arithmetic/diagonal work. Mixed fixed-target U
  uses a dense complex unitary. Every variant receives identical phased input.
- The independent scalar backend supplies expected amplitudes. Warmups, every timed
  output and every profiled output are checked. Maximum amplitude difference is
  2.72e-17; maximum compensated norm error is 2.89e-15 across the recorded cases.
- Local JIT inspection verified actual zmm arithmetic, including vmulpd,
  vaddpd and vpermilpd. This confirms execution of AVX-512 rather than merely
  detecting CPU support. Wider register capacity is four Complex values, not
  four complex operations per CPU cycle.

Ordinary synchronous Execute is timed for every variant. CUDA total includes
staging allocation/conversion, device allocation/free, upload, dispatch/barriers,
download and host commit. Context/kernel compilation, input reset, setup and
validation are excluded. No profiling events are created inside timed Execute;
the context is profiling-capable for the separate measurements below.

After the timed rounds, five separate ExecuteProfiled calls provide the device
sequence median. This interval excludes transfers/allocation but includes dispatch
gaps and barriers every 32 launches. It is not a sum of isolated kernels. Because
these are separate executions under variable scheduling, the device median can
exceed ordinary total (as in 20-qubit varying-high). Do not subtract the medians
to estimate transfer time or a transfer percentage. The large host-inclusive gap
at 25 qubits motivates further staging/allocation/transfer profiling; it does not
measure those components individually.

At 25 qubits, one state is 512 MiB. The harness owns three host arrays (input,
oracle, working state), and CUDA adds 512 MiB staging plus 512 MiB VRAM. Its
configurable 2 GiB host-array preflight accounts for all four simultaneously owned
host arrays. CPU blocking additionally uses fixed 32-gate metadata and up to
16 KiB explicit stack workspace per active worker. Native driver/context objects,
runtime overhead and GC-retained dead arrays are outside this array budget.

The cumulative process peak reached 9,309,081,600 bytes in this run. This includes
benchmark arrays, retained garbage and native/compiler/driver overhead; it is not
an isolated backend memory peak or a claim that the process stays below 2 GiB.
Managed allocation measurements include worker allocations and CUDA staging,
but exclude explicit stack workspace and native/device memory.

Raw evidence:

- [CSV](benchmarks/accelerators-2026-10-26.csv) and [metadata](benchmarks/accelerators-2026-10-26.csv.metadata.json).
- Host-derived diagnostic captures are kept private outside versioned documentation.
- [Source SHA-256 inventory](benchmarks/accelerators-2026-10-26.source-sha256.txt).

Public metadata has been anonymized after measurement. The source inventory
identifies the measurement snapshot before reporting-only privacy changes;
timings and numerical outcomes are preserved. Host diagnostics are not published.

## Reproduction and validation

```powershell
dotnet restore QuantumMatter.sln
dotnet build QuantumMatter.sln -c Release --no-restore
dotnet test QuantumMatter.sln -c Release --no-restore -p:EnableCudaTests=true
$env:DOTNET_TieredCompilation = '0'
dotnet benchmarks/QuantumMatter.Benchmarks/bin/Release/net9.0/QuantumMatter.Benchmarks.dll accelerators artifacts/accelerators.csv
Remove-Item Env:DOTNET_TieredCompilation
```

Run without simultaneous tests/benchmarks. Override
QUANTUMMATTER_BENCHMARK_MEMORY_BUDGET_BYTES to constrain the host arrays. Existing
quick/full/kernels/memory and legacy CUDA profiles remain available. The legacy
CUDA profile compares scalar CPU; use accelerators for this optimized CPU comparison.

Complete hardware-enabled suites pass 1,030 tests on .NET 9 and 1,032 on .NET 10,
with zero skips. This includes independent dense conformance, complex columns,
controls, phases, inverse/norm checks, uneven partitions, offset buffers, vector
tails, packed tiles, cancellation and concurrent reuse. Fresh package consumers
also execute real CUDA on both runtimes, including additive API consumers and
the 1.0.0-compiled binary compatibility consumer with candidate DLLs.

Fallback checks use **both** switches below, verified by the local capability
probe. Switch naming differs between runtime generations. Setting both was
verified to disable AVX-512 while preserving AVX on each tested target. Host-derived
probe output is kept private.

```powershell
$env:DOTNET_EnableAVX512 = '0'
$env:DOTNET_EnableAVX512F = '0'
dotnet test QuantumMatter.sln -c Release --no-build --no-restore -p:EnableCudaTests=true
Remove-Item Env:DOTNET_EnableAVX512
Remove-Item Env:DOTNET_EnableAVX512F
$env:DOTNET_EnableHWIntrinsic = '0'
dotnet test QuantumMatter.sln -c Release --no-build --no-restore -p:EnableCudaTests=true
Remove-Item Env:DOTNET_EnableHWIntrinsic
dotnet format QuantumMatter.sln --no-restore --verify-no-changes
./scripts/Test-Packages.ps1 -SkipBuild -ConsumerFramework net9.0 -EnableCuda
./scripts/Test-Packages.ps1 -SkipBuild -ConsumerFramework net10.0 -EnableCuda
```

Both fallback configurations pass the same complete hardware-enabled suites.
Default CPU-only suites also pass 939 tests on .NET 9 and 941 on .NET 10, each
with the explicit CUDA hardware marker skipped. The Release demo smoke test passes.
The no-build commands require the preceding CUDA-enabled test build. Restore/build,
format verification and both fresh-package runs passed. Commands may use
-p:MSBuildEnableWorkloadResolver=false where workload discovery requires it.
The accelerator host-budget guard also rejects a one-byte budget before creating
the CUDA context, benchmark output or state arrays.

Feature guards and fixed-width handling follow [Microsoft's SIMD guidance](https://learn.microsoft.com/dotnet/standard/simd)
and [Permute2x64 documentation](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.x86.avx512f.permute2x64?view=net-9.0).
CUDA remains validated on an anonymized Windows x64 environment; these results
do not establish Linux support or performance on other CPU/GPU models.
