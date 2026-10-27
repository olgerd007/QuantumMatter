# v0.5 CPU execution and measurements

The portable CpuStateVectorBackend remains the reference. Select
ParallelCpuStateVectorBackend explicitly to use bounded data parallelism and,
optionally, the measured SIMD Hadamard kernel. Neither backend changes the domain
or Application APIs. No external numerical or quantum library is used.

The current opt-in SIMD implementation also accelerates uncontrolled matrix,
diagonal and X gates with AVX, including q0 Hadamard. The
[current performance analysis](performance-analysis.md) contains the implementation
review, interleaved before/after results, and reproduction commands. The v0.5
measurements below remain historical evidence for the original implementation.

EnableAvx512 (default false, requires EnableSimd) adds guarded 512-bit kernels for
uncontrolled targets q2 and above. Smaller target strides and unsupported hardware
fall back to the existing paths. See the [AVX-512/CUDA comparison](avx512-cuda-performance.md)
for matched-width results, observed regressions and real hardware validation.

## Select a backend

```csharp
using QuantumMatter.Application;
using QuantumMatter.Infrastructure;

var backend = new ParallelCpuStateVectorBackend(new ParallelCpuOptions
{
    MaxDegreeOfParallelism = 4,
    MinimumParallelQubits = 16,
    EnableSimd = false
});
var simulator = new StateVectorSimulator(backend);
```

Defaults are min(Environment.ProcessorCount, 4) workers, a 16-qubit parallel
threshold, and SIMD disabled. Worker limits must be 1–256, and thresholds 1–30.
The limit is an upper bound; the runtime may use fewer workers. For example,
one logical processor yields a default worker limit of one.

Without SIMD, a circuit below the threshold or with a worker limit of one uses
the reference backend. With SIMD enabled, those circuits use one worker and
portable scalar kernels wherever vectorization is inapplicable. No full state
copies, structure-of-arrays conversions, or amplitude scratch buffers are created.
The existing host-array budget still covers the state and sampling CDF; managed
scheduler objects and worker stacks are runtime overhead outside that budget.

The demo accepts an optional second argument:

```sh
dotnet run --project src/QuantumMatter.Demo -- all parallel
dotnet run --project src/QuantumMatter.Demo -- all parallel-simd
```

Scalar is the default. Small examples intentionally stay below the default parallel
threshold; tests force the threshold down to exercise partitioning on small cases.

## Partition ownership and synchronization

Each gate is prepared once into coefficients and control masks. Matrix and X
kernels enumerate target-zero amplitude pairs. Consecutive ranges partition this
pair index space, so a worker owns both amplitudes of each pair. A controlled
operation filters pairs by its control mask without changing ownership.

SWAP also enumerates target-zero pairs, selecting only those with the other bit
one. Its partner has the opposite bits and is never selected by another worker.
Diagonal gates partition amplitude indices directly. These rules give every
written amplitude exactly one writer for a gate, even with unequal range lengths.

By default Parallel.For joins all ranges before the next gate starts. Worker scheduling and
cancellation use per-execution ParallelOptions and TaskScheduler.Default. State is
not retained after Execute returns, and one backend may serve concurrent calls
on independent buffers. Limits apply per call, not as a process-wide thread quota.
Shared circuits must remain unchanged while executing.

EnableGateBlocking (default false) optionally executes up to 32 adjacent uncontrolled
same-target gates on each tile before moving to the next tile. Each tile contains at
most 512 amplitude pairs (16 KiB of state data). Gates keep their original arithmetic
order per pair; matrices are not multiplied together. Controls, SWAP, identities and
target changes end a group. A join completes all tiles before the next group or
ungrouped gate. The fixed 32-entry preparation array is backend metadata outside
the state/CDF budget. High-target tiles additionally use at most 16 KiB of stack
workspace per active worker to bulk-pack distant partners into a local two-half tile. There is
no full-state scratch array. See the current analysis
for measured 25-qubit behavior. Cancellation may leave different tiles at different
points in a group; Evolve still throws and may leave a partially evolved state.

The caller's token is passed to both the scheduler and kernels, which check at
entry and within at most 4096 work items. Cancellation raised by a worker using
that token propagates as OperationCanceledException. Other worker failures remain
observable as AggregateException. Workers finish before the call returns or
throws. An interrupted buffer can be partially evolved, as documented for Evolve.
These cancellation semantics follow the
[.NET parallel-loop contract](https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/how-to-cancel-a-parallel-for-or-foreach-loop).

## SIMD scope and portability

EnableSimd selects AVX q0 Hadamard updates and Vector<double> Hadamard updates
for uncontrolled gates with sufficient target stride.
Complex real/imaginary interleaving is verified with a
runtime layout probe before casting bounded spans. The kernel uses unaligned
vector loads, never crosses a target block or worker range, and handles tails
with scalar updates. Controlled H uses scalar range kernels. On AVX hardware,
uncontrolled matrix, diagonal and X gates use 256-bit double vectors. Adjacent q0
pairs use lane permutations; higher targets process two complex amplitudes from
each half-block. Diagonal gates avoid untouched identity halves where possible.
Uneven ranges have scalar tails. Controls and SWAP keep the scalar range path.
Hardware without AVX retains the portable Hadamard path and scalar fallback for
the other gates.

The portable H kernel checks Vector.IsHardwareAccelerated and obtains its width
from Vector<double>.Count. The new fixed-width kernels additionally check
Avx.IsSupported. Unsupported hardware or an incompatible layout uses the applicable
portable/scalar fallback. See the
[.NET SIMD guidance](https://learn.microsoft.com/dotnet/standard/simd).
Double precision and the existing conformance tolerances are preserved. Passing
tests on this host does not promise bit-identical results on all hardware.

SIMD stays opt-in because its benefit depends on gate mix and target stride.
The reference backend remains independent of the prepared/parallel kernels.

## Reproduce the benchmarks

The dedicated console project uses only framework APIs; it is separate because
benchmark tooling is not a runtime library dependency. Run from the repository
root with the selected .NET 9 SDK and Release configuration. For the recorded
Windows runs:

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet run --project benchmarks/QuantumMatter.Benchmarks --configuration Release -- quick docs/benchmarks/cpu-quick.csv
dotnet run --project benchmarks/QuantumMatter.Benchmarks --configuration Release -- full docs/benchmarks/cpu-full.csv
```

On a POSIX shell, prefix each command with DOTNET_TieredCompilation=0 instead.
Tiering is disabled to keep the measured methods optimized consistently after
warmup. Removing the setting is a separate experiment; metadata records it.

Quick uses 12/16/18 qubits, 32 gates, and three timed samples per configuration.
Full uses 12/16/18/20 qubits, 16/64 gates, and five samples. Both compare scalar
with worker limits 1/2/4/8 and SIMD on/off, at low and high target strides. The
benchmark forces the parallel threshold to one to expose crossover behavior.

There are two untimed warmups per backend/case. All variants start from the same
normalized complex state, reset before each Execute outside its timed region.
Warm outputs are checked against the scalar reference before timing. Backend order
rotates between samples. Setup, reset, validation, and collection are excluded from
the timed interval. The measured execution includes gate preparation, dispatch,
synchronization, and cancellation checks; it excludes state allocation and shots.

CSV reports median/min/max time, gates/second, process-wide managed allocation
deltas including workers, state bytes, cumulative process peak working set, and
warmup conformance errors. The peak is not an isolated per-backend memory peak.
Scheduling produces allocations proportional to gates/workers; the implementation
does not claim allocation-free execution. Metadata records precision, requested
backend settings and benchmark parameters. Host identity and installation details
are deliberately omitted from public reports.

## Recorded local results

Recorded 2026-10-25 in an anonymized environment; timestamps are UTC. This
implementation uses the portable Vector API. Sources were the local feature-branch
working tree. Host models, capacities and installation details are withheld;
results support comparisons within this run, not machine-specific recommendations.

- [Quick CSV](benchmarks/cpu-quick-2026-10-25.csv) and [metadata](benchmarks/cpu-quick-2026-10-25.csv.metadata.json).
- [Full CSV](benchmarks/cpu-full-2026-10-25.csv) and [metadata](benchmarks/cpu-full-2026-10-25.csv.metadata.json).

Selected full-profile medians below; speedup is scalar time divided by candidate
time. The raw files include all 288 full-profile configurations, not just gains.

| Qubits | Gates | Mix | Candidate | Scalar ms | Candidate ms | Speedup |
| ---: | ---: | --- | --- | ---: | ---: | ---: |
| 12 | 16 | H, target 0 | 4 workers, SIMD off | 0.0600 | 0.0621 | 0.97x |
| 16 | 64 | Mixed, low target | 4 workers, SIMD off | 4.0063 | 2.0628 | 1.94x |
| 18 | 64 | Mixed, low target | 4 workers, SIMD off | 15.4647 | 10.7328 | 1.44x |
| 20 | 64 | Mixed, low target | 4 workers, SIMD off | 58.9086 | 41.3411 | 1.42x |
| 20 | 64 | Mixed, low target | 8 workers, SIMD off | 58.9086 | 26.4621 | 2.23x |
| 16 | 64 | H, highest target | 1 worker, SIMD on | 4.0360 | 1.2585 | 3.21x |
| 16 | 64 | H, highest target | 4 workers, SIMD on | 4.0360 | 0.4797 | 8.41x |
| 20 | 64 | H, highest target | 1 worker, SIMD on | 66.2779 | 21.0283 | 3.15x |
| 20 | 64 | H, highest target | 4 workers, SIMD on | 66.2779 | 34.6367 | 1.91x |

These measurements support a conservative 16-qubit threshold and four-worker
default. They also show that eight workers can help large states, and that single
worker SIMD can outperform four-worker SIMD. The threshold is configurable;
13–15-qubit crossover points were not measured. SIMD is retained for its clear
high-stride H benefit, with no universal speedup claim for other workloads.

For the 16-qubit, 64-gate mixed-low case, median managed allocation was 64 bytes
for scalar and 141,208 bytes for four workers. No amplitude buffer was added.
Across the full warmup validations, maximum amplitude difference was zero on this
host, and maximum norm error was approximately 3.27e-14. This is evidence for the
measured cases, not an unrestricted error guarantee.

The quick and full runs show timing variability and occasional spikes. CPU
affinity, background activity, and power policy were not controlled; medians from
different profiles should not be treated as identical conditions. Use raw min/max
columns and rerun on target hardware before choosing a workload-specific policy.

## Validation coverage

The shared numerical suite runs for scalar, one/two/three-worker configurations,
single-worker SIMD, and three-worker SIMD. Tests force small-register partitioning
to compare against independent dense operators. Additional cases cover larger
states, low/high strides, seven workers, nonzero buffer offsets, vector tails,
concurrent backend reuse, worker exceptions, and cancellation inside a worker.
The v0.4 algorithms and terminal sampling also run through forced parallel paths.

Release tests are additionally run with DOTNET_EnableHWIntrinsic=0 to exercise
portable fallbacks; CI repeats this configuration. Normal conformance checks do
not assert elapsed times or exact thread counts. No benchmark threshold is used
as a correctness assertion.
