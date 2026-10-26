# Architecture

QuantumMatter is a synchronous numerical library built from scratch for .NET 9.
Projects, assemblies, and namespaces use the QuantumMatter prefix consistently.

## Dependencies and responsibilities

```text
Demo (composition root) --> Application --> Core (domain)
Demo -------------------> Infrastructure --> Core
Tests ------------------> all library projects
```

Core owns circuit invariants, gate descriptions, state storage, Born-rule
measurement, and IStateVectorBackend, a domain service boundary for unitary evolution.
A domain port is appropriate here because it describes quantum state evolution
without mentioning a device, driver, or concrete kernel.

Application constructor-injects this port. StateVectorSimulator owns workflow and
resource policy, initializes a state, invokes evolution, and optionally samples a
terminal distribution. SimulationOptions is an immutable configuration record.
No container is required; the demo performs explicit composition.

Application/Algorithms contains pure circuit builders for Deutsch–Jozsa, QFT and
inverse QFT, and Grover search. They depend only on domain gates and never create
a backend. The internal basis-state phase oracle synthesizes reversible scratch
work from Toffoli/CZ gates; algorithm builders preflight their generated gate counts.
The [algorithm guide](algorithms.md) defines logical/physical register layout,
initialization requirements, cancellation, formulas, and simulation costs.

Infrastructure supplies CpuStateVectorBackend. It uses scalar in-place kernels:
amplitude-pair transforms for general one-qubit gates, swap-only X/CNOT/Toffoli
paths, diagonal phase multiplication, and the SWAP permutation. Angles are evaluated once
per gate, outside amplitude loops. No dense operator is allocated in production.

ParallelCpuStateVectorBackend adds bounded data parallelism behind the same port,
with configurable thresholds, reference fallback, and an opt-in portable SIMD
Hadamard kernel. Prepared gates, range ownership, and hardware handling remain in
Infrastructure. The [CPU execution guide](cpu-performance.md) records its resource
policy, race-free partitioning, benchmark method, and actual local measurements.

CudaStateVectorBackend implements the same port using ILGPU to compile our C#
kernels to NVIDIA PTX. ILGPU is a general GPU runtime, not a quantum library.
The full gate set uses double precision; contexts, streams, device buffers,
profiling, capability checks, and disposal stay in Infrastructure. CPU and CUDA
share immutable host-side coefficient preparation; the scalar reference kernel
remains independent. See the [GPU guide](gpu-execution.md).

Tests stay in one project with focused files. Hardware integration cases are
explicitly enabled with EnableCudaTests=true and require real NVIDIA execution.
Significant use cases can be tested without the demo.

The v1.1 development toolchain uses the .NET 10 SDK. Tests target net9.0 and net10.0
while all library projects retain net9.0, so both full suites exercise the same
package asset on their declared runtimes. Framework runs are sequential when
invoked together because hardware suites share a physical GPU. Package validation
compares compiled assets to published 1.0.0; a baseline-compiled consumer also runs
with candidate DLLs to exercise binary binding without recompilation. No new runtime
abstraction or domain/infrastructure dependency is introduced by these checks.

## Ownership and extension contract

StateVector owns one private Complex array. Amplitude and Probability expose values;
Snapshot copies the full array. Evolve checks register compatibility and lends its
buffer to the trusted backend for the duration of Execute. The backend must not
retain this memory. Execute applies the entire circuit, allowing hardware backends
to avoid a host round trip for every gate. Circuits expose a cached read-only view
of their operations, with validation at every builder method.

SingleQubitUnitary validates finite entries, dimensions, and column orthonormality
once at construction. Circuit operations hold immutable matrices and optional
control indices; the backend evaluates control masks outside its amplitude loops.
The [gate reference](gates.md) defines the operation encoding, matrices, validation
tolerance, and reusable backend conformance suite.

Backends accept an arbitrary valid input state and evolve it in place; they do not
reinitialize it. A backend must validate buffer dimensions, preserve phase and
normalization within declared precision, honor cancellation, and finish all work
using the host buffer before returning. Backends are trusted implementations,
not a sandbox for arbitrary plugins; the domain does not scan the norm after every
gate. Invalid probability mass is rejected when measuring or sampling.

The simulator returns a fresh state for each run. FinalState is intentionally
mutable through domain operations, so measuring it after Run changes that object.
The counts dictionary is an immutable view of the completed sample, and is not
recomputed if FinalState is later evolved or measured.

## Resource and execution policy

Version 1.2 adds host-side observation methods to StateVector and an immutable
PauliString in Core. Marginal queries, subset projective collapse and Pauli expectations
use constant auxiliary storage without additional backends or hardware dependencies.
They operate after any backend completes and can precede another Evolve call. Partial
measurement commits once after cancellable scans, retaining the full matching branch
and spectator coherence. See the [observation guide](observations.md) for phase,
normalization, ordering and cancellation contracts. QuantumCircuit remains unitary.

For N = 2^n, simulation owns 16N bytes of amplitude data. Run additionally owns an
8N-byte cumulative distribution. A 512 MiB default budget is checked before either
allocation; callers can configure SimulationOptions.MemoryBudgetBytes. Direct
StateVector construction uses the same default, with an explicit budget override.

| Qubits | State | State plus CDF |
| --- | ---: | ---: |
| 20 | 16 MiB | 24 MiB |
| 24 | 256 MiB | 384 MiB |
| 25 | 512 MiB | 768 MiB |
| 30 | 16 GiB | 24 GiB |

The logical range is 1–30 qubits, not a promise of available RAM. Budgets bound these
host arrays only: runtime/object overhead, the circuit, counts dictionaries, caller
snapshots, concurrent runs, and backend staging/device buffers are separate.
Allocation may still fail due to actual resource availability. A 30-qubit run
requires an explicit large budget; rejection tests never allocate it.

A gate costs O(N) work, with O(1) scalar auxiliary space or O(W) parallel scheduling
state for W workers and no additional amplitude arrays. Sampling builds a CDF in O(N),
then performs a strict upper-bound lookup in O(log N) per shot. It aggregates
integer outcomes before formatting strings, avoiding one string allocation per
shot. Counts require O(min(shots, N)) entries.

CPU computation stays synchronous. Cancellation is checked before allocation,
between operations, within long kernel/CDF/measurement loops (at most 4096 iterations
apart), and during shot sampling. Allocation and Array.Clear cannot be interrupted.
Cancellation during Evolve may leave an existing state partially evolved. Simulate
and Run throw and never return that partial state as success.

The CPU backends are reusable concurrently on independent buffers. StateVector and the fluent
circuit builder are mutable and are not thread-safe. Each concurrent simulation
must own its state, and circuit mutation must finish before execution starts.
Seeded runs use a local Random; unseeded runs use Random.Shared. Repeatability is
intended within the same .NET runtime/backend; cross-runtime random sequence
compatibility is not guaranteed.

## Parallel CPU and SIMD

v0.5 supplies a configurable parallel implementation and a measured Vector<double>
Hadamard path. Gate barriers preserve insertion order; disjoint pair/index ranges
give each amplitude one writer. The SIMD path checks hardware and Complex layout,
handles unaligned blocks and tails, and falls back to scalar kernels. SIMD and
hardware details stay out of Core and Application. No structure-of-arrays scratch
or full-register copying is required. See the CPU execution guide for defaults,
constraints, and reproducible raw results.

Run the same gate-column, permutation, dense-oracle, phase, inverse, norm, sampling,
and cancellation tests for every backend. Add reproducible benchmarks by register
size, target stride, gate mix, and circuit depth, recording CPU/ISA, SDK, elapsed
time, throughput, allocations, and peak working memory. Parallelize only above
measured thresholds and ensure each amplitude pair has one writer.

## NVIDIA GPU execution

The whole-circuit Execute boundary performs one upload, multiple ordered device
gates, and one download. One thread owns a disjoint amplitude pair, including
controlled and SWAP cases. The device buffer stays resident across the operation
loop. There is no gate fusion, implicit CPU fallback, reduced precision, or pooling.

CudaStateVectorBackend owns a context and compiled kernel and implements IDisposable.
Each execution owns a stream, a 16N-byte device buffer and a 16N-byte host staging
array. No additional amplitude scratch is allocated. Separate device/staging
budgets default to 512 MiB each, in addition to Application's host state/CDF policy.
Runtime, driver, context, stream and profiling metadata are outside these array
budgets. Actual allocation can still fail under memory pressure.

Calls on one backend serialize through a cancellation-aware semaphore, bounding
its simultaneous state allocations. Different backend instances have independent
budgets. Dispose waits for active work and is idempotent. Always synchronize the
stream before releasing a device buffer, including failure and cancellation paths.

Cancellation is checked during staging conversion and between dispatches. At most
32 kernels are queued before a barrier; an active kernel or transfer cannot be
interrupted. The final host commit is noncancellable after its last check. Device
work and staging complete before committing the result, so cancellation or technical
failure before commit leaves the input unchanged. No caller memory is retained.

Device discovery lists only compatible CUDA devices. Explicit CUDA construction
throws when none are available. CUDA event profiling is opt-in; the kernel-sequence
interval excludes transfers but includes dispatch gaps. Real hardware tests and
transfer-inclusive measurements are recorded in the GPU guide.

A future asynchronous execution port may be justified for device queue orchestration;
do not add one merely by wrapping the scalar CPU backend in Task.Run.

## Dynamic circuits

Version 1.3 adds Core/Dynamic.DynamicCircuit with private validated instructions
and snapshots of appended unitary segments. StateVector.EvolveDynamic executes one
conditional trajectory using existing evolution/partial-measurement methods, returning
a packed classical register. Reset uses measurement and conditional X. No existing
backend or circuit contract changes.

Application/Dynamic owns allocation policy, independent shots and result lifetimes.
Run reuses one amplitude array but resets quantum/classical state and executes every
instruction per shot. It returns final classical counts without implicit quantum
readout or an ensemble ket; SimulateTrajectory returns one conditional state and
classical outcome. Classical decisions run on the host and CUDA transfers at each
executed unitary-segment boundary. See the [dynamic guide](dynamic-circuits.md).

## Noise trajectories

Core/Noise adds immutable NoiseChannel descriptions, an explicit NoisyCircuit builder
and INoiseChannelBackend for stochastic nonunitary updates. NoisyCircuit snapshots
appended QuantumCircuit segments and preserves their order relative to channels.
StateVector.EvolveTrajectory lends its private buffer to each selected backend.
The original unitary backend/circuit/result contracts remain intact.

Application/Noise orchestrates independent trajectories. Run executes and measures
a fresh trajectory per shot, returning counts without an ensemble FinalState ket.
SimulateTrajectory returns one conditional pure state. Probability estimation
averages Born probabilities with Welford variance/standard errors. It never averages
amplitudes or stores all trajectories. A reusable state costs 16N bytes; estimates
add two 8N-byte arrays, preflighted against SimulationOptions.MemoryBudgetBytes.
Backend device/staging costs remain separate.

Infrastructure supplies stateless CPU kernels for bit flip, phase flip,
depolarization and amplitude damping. Any existing unitary backend can execute the
segments, including CUDA with a host round trip per segment. Noise kernels are CPU
only. See the [noise guide](noise.md) for contracts and estimator uncertainty.

## Noisy dynamic composition

Version 1.4 adds Core/Dynamic.NoisyDynamicCircuit, composing private
DynamicCircuit segments and explicit single-placement NoisyCircuit segments.
An internal continuation value carries the classical register across noise
boundaries; public EvolveDynamic still initializes it to zero. Both instruction
models keep their existing validation, snapshots, ordering and backend ownership.
NoisyDynamicCircuit exposes no mutable internal segment.

StateVector.EvolveNoisyDynamic validates both injected ports and register sizes
before executing one conditional history. Application's separate
NoisyDynamicStateVectorSimulator reuses the dynamic result models, performs the
same amplitude-only budget check, and re-executes independent histories per shot.
No backend interface or runtime dependency is added. Noise, measurements and
classical decisions stay on the host; CUDA transfers at executed unitary segment
boundaries. See the [noisy dynamic guide](noisy-dynamic-circuits.md).

## Variational building blocks

Version 1.5 adds immutable Core/Observables Pauli Hamiltonians and a
Core/Variational parameterized template. Constant operations are snapshotted;
binding creates validated QuantumCircuit operations for existing backends. Public
ExpectationValue remains unchanged; ExpectationEnergy adds weighted Hamiltonians
without ambiguous overloads. No full operator matrix is allocated in production.

Application/Variational injects the existing unitary backend and options, reuses
one state for base/shifted executions, and returns immutable energy/gradient results.
Shared parameters are differentiated occurrence by occurrence. Termwise differences
preserve small gradients under large identity offsets; composed same-axis shifts
avoid lost angle increments. The host budget includes parameter/gradient/term arrays;
backend device/staging budgets remain separate. No new infrastructure dependency
or backend port is introduced. See the [variational guide](variational.md).

Version 1.6 composes that evaluator in Application/Variational.VqeSolver.
One deterministic gradient-descent/backtracking policy keeps optimization out of
Core and Infrastructure; no generic optimizer port is needed for one policy.
Options/results are immutable. Execution counts include every shifted preparation,
and the solver reserves overlapping parameter arrays before nested evaluation.
Backends remain caller-owned. Stationarity, limits and stalled searches have
distinct results; cancellation and technical failures throw without partial results.
See the [VQE guide](vqe.md).
