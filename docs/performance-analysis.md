# CPU performance analysis — 2026-10-26

The follow-up [AVX-512/CUDA comparison](avx512-cuda-performance.md) now includes an
opt-in 512-bit path, real CUDA hardware tests on both runtimes, and optimized CPU
versus transfer-inclusive GPU measurements. The measurements below retain the
earlier CPU-only snapshot and its validation scope.

## Result and scope

Expanded the existing opt-in SIMD CPU backend while retaining .NET 9 library
assets, `System.Numerics.Complex` double precision, gate order, phases, cancellation,
and the independent scalar reference. There are no new dependencies, full-state
scratch arrays, or changes to Core/Application. A follow-up adds the opt-in
ParallelCpuOptions.EnableGateBlocking property, default false; its get/init accessors
are explicitly listed in PublicAPI.Unshipped without changing shipped inventory.

In an anonymized environment, interleaved comparisons against source revision
`96e2e2db62e6d9cc09b478fe0936d3b702280180` demonstrate improvements in the targeted
kernels. They do not establish a universal optimum across circuits or hardware.
Eight workers are frequently slower than four; small circuits can be slower with
parallel scheduling. Defaults remain four workers at most, a 16-qubit threshold,
and SIMD opt-in. A single SIMD worker can be useful below that threshold.

## Implementation review and changes

| Area | Gap found | Implemented change |
| --- | --- | --- |
| q0 Hadamard | Existing contiguous-half SIMD kernel cannot handle adjacent pairs | AVX lane permutations compute both outputs from one vector |
| Rx/Ry/Y/custom U | Four scalar complex products per amplitude pair, even with SIMD enabled | AVX complex multiplication; real coefficients omit imaginary products |
| Z/S/T/Phase/Rz | Scalar complex multiplication across the register | AVX diagonal updates; skip identity half-blocks when target stride permits |
| X | Scalar pair exchanges | AVX pair/block permutations |
| Worker dispatch | A capturing delegate allocated for every gate, including single-worker execution | One worker delegate and range holder per parallel execution; configured single-worker path creates neither |
| Performance evidence | Separate before/after runs drifted even for unchanged scalar code | Load an isolated source-baseline assembly and interleave old/new variants in one process |

`AvxStateVectorKernel` stays inside Infrastructure. It requires AVX and a verified
Complex memory layout. Loads/stores are unaligned and bounded within spans; worker
ranges never overlap. Higher target strides process two complex values from each
half-block, with scalar remainders for uneven partitions. Adjacent q0 pairs use
256-bit lane permutations. No fused multiply-add, reduced precision, fast-math
mode, or implicit renormalization is used. Cancellation checks remain within 4096
work items. Controls and SWAP retain scalar range kernels; high-stride H retains
the portable `Vector<double>` path.

The per-execution scheduler updates its gate/range fields only after `Parallel.For`
has joined all workers. Backend instances retain no execution state or host buffer,
so independent calls can still use the same backend concurrently.

## Measurements

Release, tiered compilation disabled, with the 256-bit CPU path requested. Host
hardware and installation details are deliberately withheld. Measurements used
an environment with uncontrolled
affinity, clock/power policy and background activity. Retain the raw min/max values
when interpreting differences. Earlier standalone runs drifted by 30–50% even for
unchanged scalar execution and are excluded from the comparisons below.

Every listed case has 20 qubits and 64 gates. Before/after columns use the **same
worker limit with SIMD enabled**; speedup is before divided by after. These are
selected examples, not averages across the entire suite.

| Workload | Workers | Before ms | After ms | Speedup |
| --- | ---: | ---: | ---: | ---: |
| H, q0 | 1 | 80.0879 | 33.5010 | 2.39x |
| H, q0 | 4 | 38.6765 | 19.1087 | 2.02x |
| Mixed, low target | 4 | 19.1976 | 14.1739 | 1.35x |
| Mixed, high target | 4 | 35.8420 | 23.8431 | 1.50x |
| Rx, highest target | 1 | 83.2899 | 30.9412 | 2.69x |
| Ry, highest target | 4 | 24.7835 | 9.6770 | 2.56x |
| Phase, q0 | 4 | 21.8789 | 9.0084 | 2.43x |
| Phase, highest target | 4 | 21.5168 | 5.5081 | 3.91x |

For the mixed-low case, current scalar execution takes 56.6419 ms versus 14.1739 ms
with four SIMD workers: 4.00x within that run. For high-stride H with one SIMD
worker, the existing kernel is unchanged and measures 30.3590 ms before versus
31.2659 ms after (0.97x). Eight-worker q0 H measures 26.8165 ms before versus
28.8463 ms after (0.93x). Acceleration is workload dependent.

Single-worker SIMD allocations fall from 7,832 to 104 bytes per 64-gate Execute
(98.7% reduction). Mixed-low four-worker allocations fall from 141,208 to 129,920
bytes; runtime parallel scheduling still allocates. A 20-qubit state is 16 MiB;
these changes add no amplitude scratch or copies. Application sampling separately
requires an 8 MiB CDF for that state.

The full comparison contains 544 rows; the kernel profile contains 1,088 rows.
These first-pass measurements precede the optional multi-gate blocking change
described below. The memory profile records that later implementation, including
blocked and unblocked variants in the same process.
Both use 12/16/18/20 qubits, 16/64 gates, five timed repetitions, two warmups per
variant/case, worker limits 1/2/4/8 and SIMD on/off. The parallel threshold is forced
to one to reveal overhead. Backend order rotates and reverses on odd repetitions;
each baseline sits beside its candidate. Reset, input allocation, warmup and
conformance scans are outside timing. Execute includes preparation, scheduling,
kernel work and joins. This does not measure sampling, dynamic/noise trajectories,
variational evaluation or cold startup. The unitary microbenchmark uses the same
fixed phase/permutation matrix as the mixed profile; dense complex matrices are
additionally covered by numerical tests.

Maximum warmup amplitude difference against scalar is zero on this host across
both profiles. Maximum norm error is 3.27e-14 for full and 9.77e-14 for kernels.
Those results do not imply bitwise identity on other runtimes/hardware.

Raw evidence: [full CSV](benchmarks/cpu-comparison-2026-10-26.csv),
[full metadata](benchmarks/cpu-comparison-2026-10-26.csv.metadata.json),
[kernel CSV](benchmarks/cpu-kernels-comparison-2026-10-26.csv),
[kernel metadata](benchmarks/cpu-kernels-comparison-2026-10-26.csv.metadata.json).
Each CSV has a `.baseline.txt` sidecar identifying the source baseline.

## Reproduction and use

```powershell
dotnet restore QuantumMatter.sln
./scripts/Compare-CpuKernels.ps1 -BaselineRef 96e2e2db62e6d9cc09b478fe0936d3b702280180 -Profile full -Output artifacts/cpu-comparison.csv
./scripts/Compare-CpuKernels.ps1 -BaselineRef 96e2e2db62e6d9cc09b478fe0936d3b702280180 -Profile kernels -Output artifacts/cpu-kernels-comparison.csv
```

The script builds CPU sources from the selected Git revision into an isolated,
nonshipping tooling assembly under `artifacts/`, sharing the existing Core backend
port with the candidate. It records that revision, sets tiering to zero for the
comparison, and restores the previous tiering environment setting afterward.
Run without simultaneous benchmarks/tests. The original quick/full/CUDA commands
remain usable; `kernels` and an optional baseline DLL are additive CLI modes.

```csharp
var backend = new ParallelCpuStateVectorBackend(new ParallelCpuOptions
{
    MaxDegreeOfParallelism = 4,
    MinimumParallelQubits = 16,
    EnableSimd = true
});
var simulator = new StateVectorSimulator(backend);
```

Tune workers and threshold using the actual circuit mix. SIMD runs with one worker
below the parallel threshold. Running several simulations concurrently multiplies
the per-execution worker limit and state memory; avoid nested oversubscription.

## SIMD width and large target strides

A Complex amplitude contains two 64-bit components. Therefore one 256-bit register
holds four doubles / two complex values; a 512-bit register holds eight doubles /
four complex values. These are register capacities, not complex operations per
cycle. A complex multiply also needs component permutations, multiplies and adds.
Actual throughput depends on the instruction sequence, processor and memory access.
The count semantics are documented by [Vector256.Count](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.vector256-1.count)
and [Vector512.Count](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.vector512-1.count).

At 25 qubits the state is 512 MiB. For target q24, partner indices differ by
2^24 elements / 256 MiB, but successive pairs still access **two contiguous
streams**, one in each half of the array. Each worker owns disjoint ranges in both
streams. The distance between partners is not the distance between successive
loads within each stream, and does not alone demonstrate inefficient cache-line
use or a Parallel.For defect.

The current AVX path already traverses bounded runs of at most 4096 amplitude
pairs (128 KiB of paired state data), with no copy. This bounds cancellation and
span lengths; it is not temporal cache blocking across multiple gates. Splitting
one streaming pass into smaller loops does not reduce its compulsory data traffic.
An uncontrolled full-state gate logically reads and writes 32N bytes, or 1 GiB at
25 qubits. Cache blocking is most useful when several compatible gates reuse a
tile before eviction. This reuse requirement follows
[Intel's loop-blocking analysis](https://www.intel.com/content/www/us/en/developer/articles/technical/loop-optimizations-where-blocks-are-required.html).
Transposing/permuting the complete state adds another full-state traversal and
requires preserving the logical qubit mapping through later gates/observations;
it should be justified by enough subsequent reuse to recover that cost.

Toffoli already uses an exchange kernel selected by both control bits. CNOT/X,
SWAP and diagonal gates also have specialized paths; no production 8x8 or 2^n x
2^n gate matrix is created. The scalar reference remains independent of prepared
gate kernels.

The additional `memory` benchmark profile compares 20 and 25 qubits, eight gates,
low/high targets and one/four/eight SIMD workers, with five timed repetitions.
With a source baseline and blocked variants it has 80 rows. Three full harness arrays at 25 qubits
cost 1.5 GiB; the harness preflights them against a 2 GiB default array budget.
Override `QUANTUMMATTER_BENCHMARK_MEMORY_BUDGET_BYTES` to constrain benchmark arrays;
this is separate from the simulator's existing host budget. Scheduler, process and
circuit overhead are outside this array budget. Peak working set is cumulative.

```powershell
./scripts/Compare-CpuKernels.ps1 -BaselineRef 96e2e2db62e6d9cc09b478fe0936d3b702280180 -Profile memory -Output artifacts/cpu-memory-comparison.csv
```

### Optional multi-gate cache blocking

EnableGateBlocking reuses a tile of at most 512 amplitude pairs / 16 KiB across
up to 32 adjacent uncontrolled gates with the same target. It does not compose
gate matrices, eliminate inverse gates, change coefficient preparation, or reorder
the arithmetic for any pair. Pair tiles are independent for every gate in the
group. Complete low-target blocks use one contiguous amplitude interval for
diagonal updates. Targets with a stride greater than 512 amplitudes pack both
owned strips into a two-half tile in a 16 KiB stack workspace per active worker.
Power-of-two strip lengths retain the target-bit representation locally; bulk span
copies gather/scatter each strip, and the same kernels execute with the local target.
Uneven worker starts and ends use partial tiles. Controls, SWAP, identity and
target changes end groups.

Preparation requires only one fixed 32-entry metadata array per enabled execution.
This and the bounded per-worker stack workspace are outside the existing state/CDF
array budget, like scheduling/worker-stack overhead. No full-state array or full-state
transposition is introduced. A group joins before any following operation.
During cancellation different tiles can have advanced through different gates;
the existing partially-evolved-state contract still applies, and no simulation
result is returned as success.

```csharp
var backend = new ParallelCpuStateVectorBackend(new ParallelCpuOptions
{
    MaxDegreeOfParallelism = 4,
    MinimumParallelQubits = 16,
    EnableSimd = true,
    EnableGateBlocking = true
});
```

### 25-qubit results

The final memory profile compares the **current unblocked backend** with the same
worker limit plus EnableGateBlocking. All cases below have eight gates and SIMD
enabled. Low target means q0; high target means q24. This tests a 512 MiB state,
including two streams 256 MiB apart for high-target pairs.

| Workload | Workers | Unblocked ms | Blocked ms | Speedup |
| --- | ---: | ---: | ---: | ---: |
| H, q24 | 1 | 1136.8411 | 113.6881 | 10.00x |
| H, q24 | 4 | 294.0434 | 33.7663 | 8.71x |
| H, q24 | 8 | 164.5831 | 24.4189 | 6.74x |
| H, q0 | 8 | 136.3773 | 31.5475 | 4.32x |
| Mixed, high target | 8 | 172.8780 | 105.7951 | 1.63x |
| Mixed, low target | 8 | 121.3653 | 71.5688 | 1.70x |

The mixed case includes controlled gates and SWAP that cannot join these groups;
its speedup is consequently smaller than repeated same-target H. H operations
are still evaluated individually, including repeated inverses; no gates are removed.
These are selected synthetic workloads, not a measured speedup for every algorithm.

Blocking is optional because it can regress smaller states and single-worker runs.
At 20 qubits, high-target H with one worker measures 2.7372 ms unblocked versus
3.2189 ms blocked. Mixed-low with one worker measures 5.0941 versus 6.2480 ms.
Use the actual workload to select worker count and whether to enable blocking.

For eight-worker 25-qubit H, managed allocation falls from 24,072 bytes unblocked
to 6,024 bytes blocked; stack workspace adds up to 128 KiB across eight active
workers and is not included in managed allocation. The single-worker blocked
execution allocates 2,880 managed bytes, versus 40 unblocked, because of the bounded
32-entry metadata array. Neither mode allocates a full-state scratch array.

Across all 80 configurations maximum amplitude difference against the independent
scalar backend is zero on this host; maximum norm error is 4.26e-13. No hardware
cache-miss or memory-controller counters were collected, so these timings do not
establish a precise L3/DRAM cause for the high-target slowdown. They do demonstrate
that bounded tile reuse with bulk strip packing addresses the observed case.

Raw evidence: [memory CSV](benchmarks/cpu-memory-comparison-2026-10-26.csv),
[metadata](benchmarks/cpu-memory-comparison-2026-10-26.csv.metadata.json),
[source baseline](benchmarks/cpu-memory-comparison-2026-10-26.csv.baseline.txt).

## Remaining performance opportunities

The following are structural findings from code inspection, not measured speedup
claims or completed optimizations:

1. **General gate fusion / cache blocking.** The opt-in implementation covers only
   uncontrolled same-target groups. Mixed-target fusion could further reduce memory
   traffic and barriers, but needs independent phase/depth/cancellation tests and
   representative algorithm benchmarks; repeated-H benchmarks alone would overstate
   its benefit.
2. **Controlled gates and SWAP.** They still filter scalar pair ranges. Compact
   control-bit enumeration and masked SIMD should be measured in circuits with
   realistic control density and both operand orderings.
3. **Observables and variational gradients.** Host expectation queries remain serial.
   `ExpectationEnergy` validates Born mass and then each `ExpectationValue` scans it
   again; shifted evaluations repeat term scans. Reusing a validated mass within one
   immutable observation could remove work. Parallel reductions must preserve
   cancellation, summation accuracy, large-offset gradients and resource budgets.
4. **Shot-level parallelism.** Independent dynamic/noisy histories can be distributed,
   but require separate state/RNG ownership, a total memory budget and a reproducible
   seed policy. It can oversubscribe internally parallel kernels and multiply 16N-byte
   states, so it is not enabled indiscriminately.
5. **Runtime/JIT tuning.** Tiered PGO, .NET 10 hosting, AVX-512 and native AOT need
   separate steady-state, startup and code-generation experiments. These measurements
   disable tiering and use the .NET 9 runtime; passing .NET 10 tests is not a .NET 10
   performance claim. No global GC/thread-pool/JIT switches are changed.
6. **CUDA.** Existing unitary execution keeps state resident for a whole circuit;
   dynamic/noisy segments transfer at each boundary. Device/staging allocation reuse
   and segment fusion deserve independent hardware measurements. Historical GPU
   results compare against scalar CPU, so they do not establish a gap against the
   improved SIMD backend. No CUDA performance or hardware correctness is claimed
   from the CPU validation here.

The decision to measure worker counts and keep bounded parallelism follows
[Microsoft's parallelism guidance](https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/potential-pitfalls-in-data-and-task-parallelism).
Hardware checks, bounded remainders and scalar fallback follow
[Microsoft's SIMD guidance](https://learn.microsoft.com/dotnet/standard/simd).

## Validation performed

- Solution restore and Release build passed, with zero build warnings/errors.
- `dotnet test QuantumMatter.sln -c Release --no-restore --verbosity minimal`
  passed 703 tests on each of .NET 9 and .NET 10. The CUDA hardware marker
  was explicitly skipped on each runtime.
- Both complete suites also passed with `DOTNET_EnableHWIntrinsic=0`, and separately
  with `DOTNET_EnableAVX=0`, using `--no-build --no-restore`.
- `dotnet format QuantumMatter.sln --no-restore --verify-no-changes` passed.
- `scripts/Test-Packages.ps1 -SkipBuild -ConsumerFramework net9.0` and the same command
  with `net10.0` passed. Both used fresh candidate packages, all additive consumers,
  and the 1.0.0-compiled consumer with candidate DLLs without rebuilding that consumer.
- `dotnet run --project src/QuantumMatter.Demo -c Release --no-build` passed.
- The full and kernel interleaved benchmarks passed their warmup conformance checks.

The build/test invocations used `-p:MSBuildEnableWorkloadResolver=false` where needed
for workload-discovery permissions. Numerical coverage includes the
shared independent dense oracle, complex columns/phases, inverse/norm checks,
concurrent backend reuse and cancellation, plus per-gate checks at every target
stride on offset buffers with one/three/seven workers and uneven scalar tails.
The follow-up cache-blocking implementation passes 798 tests on .NET 9 and 800 on
.NET 10, with one explicit CUDA skip per runtime. Its coverage additionally includes
independent analytic tensor columns for packed high targets, 73-gate sequences
crossing the 32-gate batch boundary at every target stride, scalar/SIMD and uneven
one/three/seven-worker paths, offset sentinels, blocked cancellation and concurrent
backend reuse. The memory profile's insufficient-budget check was exercised with a
one-byte budget: it rejects the first case before output/state-array creation.

Shipped PublicAPI inventory, package targets and dependencies are unchanged;
the two accessors of the new opt-in property are explicitly added to Unshipped.
CUDA hardware
execution was not validated in this CPU optimization task.
