# Dynamic circuits and classical control

These APIs are included in **1.3.0**; the [release record](releases/1.3.0.md)
tracks publication. The existing QuantumCircuit is still unitary;
DynamicCircuit is a separate finite program with explicit measurement and feedback.

```csharp
using QuantumMatter.Application.Dynamic;
using QuantumMatter.Core;
using QuantumMatter.Core.Dynamic;
using QuantumMatter.Infrastructure;

var circuit = new DynamicCircuit(qubits: 2, classicalBits: 2)
    .Append(new QuantumCircuit(2).H(0).CNot(0, 1))
    .Measure([0], [0])
    .If(classicalMask: 1, expectedValue: 1, new QuantumCircuit(2).X(1))
    .Measure([1], [1]);
var simulator = new DynamicStateVectorSimulator(new CpuStateVectorBackend());
var result = simulator.Run(circuit, shots: 1024, seed: 42);
// Classical c1 is always zero after correction; labels are "00" and "01".
```

## Program and classical memory

Both register widths are fixed at construction: 1–30 qubits and 1–30 classical bits.
The classical register is a packed nonnegative int with bit zero least significant.
Every execution starts its classical register at zero. Append and If copy their
unitary segment immediately; later source-circuit changes do not affect the program.
Measure copies its mappings. Instructions run in insertion order.

Measure(qubits, classicalBits) requires equally sized, nonempty selections with
distinct in-range entries within each selection. It projectively measures the
qubit subset using v1.2 semantics and writes outcome bit k to classicalBits[k].
Writes overwrite old zeros **and** ones, preserving unrelated classical bits.
For example Measure([2,0], [1,3]) writes q2 to c1 and q0 to c3. Earlier classical
records are retained until explicitly overwritten, even if their qubits later change.

If(mask, value, segment) executes the copied unitary segment exactly when
(classicalRegister & mask) == value. Mask is nonzero, fits the register, and value
sets only bits present in mask. Value zero is allowed. A skipped condition makes
no backend call and consumes no random draw. Conditions can read unwritten bits,
whose value is zero. This increment has no conditional measurements/resets,
if/else blocks, loops, arithmetic, external callbacks or general expression language.

No existing unitary backend interface changes. Scalar, parallel/SIMD and CUDA
execute the selected unitary segments. Measurement, classical decisions and reset
selection run on the host; CUDA performs its existing upload/download for each
executed segment, including a reset correction. No device-resident adaptive
execution or speedup is claimed.

## Trajectories, shots and results

SimulateTrajectory returns one owned conditional FinalState and its packed
ClassicalOutcome. It adds no implicit terminal measurement. FinalState remains
mutable and describes only the sampled history, not the ensemble over histories.
StateVector.EvolveDynamic instead evolves an existing quantum state, creates a
fresh zero classical register for that call, and returns the final packed bits.

Run re-executes the **whole program** for every shot, starting from |0...0> and
zero classical memory. It counts final classical registers as zero-padded labels
of ClassicalBitCount bits, highest bit first. It does not sample a CDF from one
selected branch. Unwritten bits remain zero, and an empty program produces all-zero
classical counts. There is no ensemble FinalState and no automatic quantum readout;
append explicit Measure instructions for any final qubits you want counted.

Measurements consume one full-basis Born draw each, including deterministic
measurements. Reset also consumes one draw; unitary and conditional instructions
consume none. The same seed reproduces a run within the same runtime/backend/API.
Different instruction sequences or SimulateTrajectory versus Run need not consume
the same random stream. Cross-runtime/device sequences are not guaranteed.

## Reset mathematics

Reset(q) measures q and applies X if the result is one. It does not write any
classical bit and does not overwrite a portion of the amplitude array. In density
matrix form its local Kraus operators are

```text
R0 = |0><0| = [[1,0],[0,0]]
R1 = |0><1| = [[0,1],[0,0]]
rho' = R0 rho R0^dagger + R1 rho R1^dagger
```

Each trajectory samples branch j with probability ||Rj psi||^2 / ||psi||^2 and
returns Rj psi / ||Rj psi||. This sets q to zero while leaving an entangled
spectator ensemble mixed. For a Bell pair, resetting q0 produces equal-weight
|00> and |10> trajectories, not a coherent superposition of them. Existing
surviving phases and independent spectator coherence are preserved.

## Cancellation, budgets and ownership

The runner preflights 16*2^n host amplitude bytes before allocation. It reuses one
state across shots; there is no terminal CDF or history-sized state array.
Each executed gate/measurement costs O(2^n) work, and classical operations cost
O(selected bits). Circuit snapshots/mappings, count dictionaries and runtime/object
overhead are outside the amplitude-array budget. Device/staging budgets are separate.

Cancellation is checked before allocation, between instructions/shots, and inside
existing kernels/measurement scans. The partial-measurement projection commit is
noncancellable after its final check. Existing-state EvolveDynamic can leave partial
evolution on cancellation or backend failure; it does not return a classical result
in that case. The runner never returns successful partial counts or trajectories.
Mutable builders and states are not thread-safe; finish construction before execution.
Callers retain ownership of injected backends and must dispose CUDA backends.

The separate NoisyCircuit/trajectory API remains unchanged. Dynamic instructions
and noise channels are not combined in this increment. A later combined model
must preserve trajectory sampling, phase and memory contracts explicitly.

The [v1.4 candidate](noisy-dynamic-circuits.md) introduces that composition through
a separate NoisyDynamicCircuit and runner. DynamicCircuit itself remains unchanged.

## Validation and examples

Tests force all four teleportation outcomes for basis states, real/complex
superpositions and an arbitrary phase. They compare the receiver's complex
amplitudes on every enabled backend. Independent dense Kraus matrices verify
reset density matrices; exact unnormalized branch enumeration verifies interleaved
measurement, conditions and reset without production kernels in the oracle.
Other checks cover overwrite/ordering, snapshots, unused/high classical bits,
independent seeded shots, cancellation, budgets and failed backends.

```sh
dotnet run --project src/QuantumMatter.Demo -- dynamic
dotnet run --project src/QuantumMatter.Demo -- dynamic parallel-simd
dotnet run --project src/QuantumMatter.Demo -- dynamic cuda
```

The demo teleports a complex state, inverts the receiver preparation to verify it
on every shot, and shows that reset preserves the earlier classical record.
See [measurement conventions](observations.md) and the [release record](releases/1.3.0.md).
Background: [dynamic circuits](https://quantum.cloud.ibm.com/docs/en/guides/execute-dynamic-circuits)
and [teleportation](https://quantum.cloud.ibm.com/learning/en/modules/computer-science/quantum-teleportation).
