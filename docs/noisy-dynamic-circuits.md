# Noisy dynamic circuits

Version **1.4.0** combines explicit noise placements with mid-circuit
measurement, classical feedback and reset. This is a separate NoisyDynamicCircuit
and NoisyDynamicStateVectorSimulator; QuantumCircuit, NoisyCircuit and DynamicCircuit
retain their existing contracts. See the [release record](releases/1.4.0.md).

```csharp
using QuantumMatter.Application.Dynamic;
using QuantumMatter.Core;
using QuantumMatter.Core.Dynamic;
using QuantumMatter.Core.Noise;
using QuantumMatter.Infrastructure;

var program = new NoisyDynamicCircuit(qubits: 2, classicalBits: 2)
    .Append(new QuantumCircuit(2).H(0).CNot(0, 1))
    .ApplyNoise(NoiseChannel.BitFlip(0.25), target: 0)
    .Measure([0], [0])
    .If(classicalMask: 1, expectedValue: 1, new QuantumCircuit(2).X(1))
    .Measure([1], [1]);
var simulator = new NoisyDynamicStateVectorSimulator(
    new CpuStateVectorBackend(), new CpuNoiseChannelBackend());
var result = simulator.Run(program, shots: 4096, seed: 42);
// c1 records a disagreement with the original Bell correlation: probability 0.25.
```

## Instructions and ownership

Quantum and classical widths are fixed at construction, each in [1,30]. Bit zero
is least significant; printed labels put the highest classical bit first. Append
and If snapshot a size-compatible QuantumCircuit immediately. Measure copies its
ordered qubit/destination mappings and overwrites both old zeros and old ones.
Reset is the same stochastic single-qubit reset used by DynamicCircuit and writes
no classical memory. Inputs are validated before adding an instruction.

ApplyNoise(channel, target) places an existing bit-flip, phase-flip, depolarizing
or amplitude-damping channel at that exact position. It is unconditional, can
occur before/after any other instruction, and does not write or clear classical
bits. A condition after noise reads the entire preceding classical history, even
if noise has since changed the measured qubit. Consecutive placements preserve
insertion order. StepCount counts individual explicit instructions, including
zero-strength channels, rather than internal segments.

The builder composes private DynamicCircuit segments with single-placement
NoisyCircuit segments. Only the internal dynamic executor accepts a continuing
classical register; public EvolveDynamic still starts at zero. No backend port,
existing public signature, or runtime dependency changes.

## Trajectory mathematics

Each history retains its own quantum state and classical register. At a noise
placement, a Kraus branch K_j is selected with probability
`||K_j psi||^2 / ||psi||^2`; its conditional state is `K_j psi / ||K_j psi||`.
Subsequent measurements use that state, and classical results choose later unitary
segments. Reset applies the local channel with Kraus operators |0><0| and |0><1|.
These are stochastic alternatives, never a coherent sum.

The ensemble is a sum of weighted outer products grouped by classical memory.
Overwriting a classical bit can merge histories into one count label, but their
quantum amplitudes must never be added or averaged. Noise leaves stored classical
history intact. The [noise guide](noise.md) specifies every channel and parameter
convention, including depolarization as replacement strength rather than total
Pauli error probability. Background on the mathematical representation:
[IBM Quantum, channel representations](https://quantum.cloud.ibm.com/learning/en/courses/general-formulation-of-quantum-information/quantum-channels/representations-of-channels).

Run resets quantum state and classical memory and re-executes the entire program
independently for every shot. Counts are immutable final classical-memory counts
with no implicit terminal quantum readout and no ensemble FinalState.
SimulateTrajectory returns the existing DynamicTrajectoryResult containing one
owned conditional state and its classical outcome. StateVector.EvolveNoisyDynamic
instead preserves the incoming quantum state and starts new zero classical memory.

Zero-strength placements call no noise backend and consume no draw. Other noise
draws retain the existing CPU noise backend's conventions; measurement and reset
each consume one Born draw, including deterministic measurements. Skipped unitary
conditions consume none. Same-seed reproducibility is scoped to the same API,
runtime and backend; cross-version/device sequences are not promised. Counts have
sampling uncertainty, with binomial standard error sqrt(p*(1-p)/shots) for each
outcome estimate. No ensemble estimator is introduced here.

## Resources, hardware and failures

The runner preflights and reuses one 16*2^n-byte host amplitude array. There is no
terminal CDF, density matrix or history-sized amplitude buffer. Circuit snapshots,
instruction records, count dictionaries and runtime overhead are outside this
array budget. CPU operations remain synchronous; cancellation is propagated through
instructions, shots, measurements and both backend ports.

Unitary segments support scalar, bounded parallel/SIMD and CUDA execution. Noise,
measurement, reset selection and classical decisions run on the host. CUDA transfers
at each executed unitary segment or reset correction, with its existing separate
device/staging budgets. There is no GPU noise kernel or device-resident feedback.
Callers own and dispose injected CUDA backends. Mutable builders/states are not
thread-safe and must not be modified during execution.

Cancellation or backend failure can leave an existing state partially evolved;
no successful classical result or partial counts are returned. Invalid inputs are
rejected with standard argument exceptions before appending the instruction.
As with existing builders, allocation failures are not transactional guarantees.

This release has no conditional noise placements, loops, callbacks, arbitrary
classical expressions, correlated channels, readout-error model, automatic gate
noise insertion, or production density-matrix execution.

## Examples and validation

```sh
dotnet run --project src/QuantumMatter.Demo -- noisy-dynamic
dotnet run --project src/QuantumMatter.Demo -- noisy-dynamic parallel-simd
dotnet run --project src/QuantumMatter.Demo -- noisy-dynamic cuda
```

The example shows noisy Bell feedback, reset, and retention of earlier classical
records across damping. It is not a general quantum error-correcting code.
Tests force every supported channel/measurement branch on arbitrary complex
three-qubit inputs, comparing amplitudes and classical-conditioned density matrices
to independent dense Kraus/projector calculations on each enabled unitary backend.
An independent unnormalized branch oracle checks interleaved noise, measurement,
feedback, overwrite and reset statistics. Further checks cover zero-noise equivalence,
unchanged old workflows, snapshots, fresh shots, seeds, budgets and failures.

The separate samples/NoisyDynamicConsumer uses package references only and runs
through Test-Packages.ps1 on .NET 9/10. Public-feed checks opt into these APIs with
-ValidateNoisyDynamicCircuits; omit that flag for older releases. See the
[dynamic guide](dynamic-circuits.md), [compatibility policy](compatibility.md) and
[release record](releases/1.4.0.md) for execution and promotion evidence.
