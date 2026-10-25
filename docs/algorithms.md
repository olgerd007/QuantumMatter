# v0.4 quantum algorithms

QuantumMatter.Application.Algorithms provides circuit builders for Deutsch–Jozsa,
QFT/inverse QFT, and single-solution Grover search. They use the existing domain
gates and have no dependency on CPU or GPU implementations. Select the execution
backend when constructing StateVectorSimulator.

## Run the examples

```sh
dotnet run --project src/QuantumMatter.Demo -- deutsch-jozsa
dotnet run --project src/QuantumMatter.Demo -- qft
dotnet run --project src/QuantumMatter.Demo -- grover
dotnet run --project src/QuantumMatter.Demo -- all
```

No argument, or bell, runs the original Bell example. An optional second argument
selects scalar (default), parallel, or parallel-simd. See the
[CPU execution guide](cpu-performance.md) for fallback thresholds. Invalid arguments
print usage and return exit code 2. Examples use small registers and seeded terminal
sampling; they do not insert measurement into circuits.

## Fourier transform

The forward transform uses the positive exponent and includes bit reversal:

```text
F[y,x] = exp(2*pi*i*x*y/N) / sqrt(N), N = 2^n
inverse F[y,x] = exp(-2*pi*i*x*y/N) / sqrt(N)
```

This matches the positive-exponent definition in the
[IBM Quantum QFT reference](https://quantum.cloud.ibm.com/learning/en/modules/computer-science/qft).
The gate decomposition uses H, controlled phase rotations, and final SWAPs.
The inverse reverses the complete decomposition and negates the rotation angles.
Fourier-basis probabilities alone do not expose the input phase; the example prints
complex amplitudes and then restores the initial state using inverse QFT.

```csharp
using QuantumMatter.Application.Algorithms;
using QuantumMatter.Core;

var circuit = new QuantumCircuit(3).X(0).X(2); // |101>, integer 5
QuantumFourierTransform.Append(circuit);
QuantumFourierTransform.Append(circuit, inverse: true);

// Local bit 0 is physical qubit 3, local bit 1 is physical qubit 0.
QuantumFourierTransform.Append(circuit: new QuantumCircuit(4), qubits: new[] { 3, 0 });
```

CreateCircuit(qubits, inverse = false) creates a standalone transform for 1–30
qubits. Append(circuit, inverse = false) transforms the whole register. The subset
overload takes distinct physical qubits ordered from local least to most significant
bit and leaves spectators unchanged, including entangled spectators. Invalid
subsets are rejected before any gates are appended. Cancellation during a valid
append can leave the existing circuit with an appended prefix.

## Deutsch–Jozsa

CreateCircuit(truthTable) accepts 2^n Boolean values indexed by the integer input x.
The table must be constant or contain exactly half true entries. It synthesizes
the phase oracle O_f|x> = (-1)^f(x)|x>, surrounded by Hadamards on the input register.
The resulting amplitude at input y is

```text
alpha_y = (1/N) sum_x (-1)^(f(x) + parity(x AND y))
```

The zero input outcome has probability one for constant functions and zero for
balanced functions, following the
[Deutsch–Jozsa classification rule](https://quantum.cloud.ibm.com/learning/en/modules/computer-science/deutsch-jozsa).
Any nonzero measured input indicates balanced. This implementation directly uses
the phase-oracle form, so there is no separate output qubit prepared in |->.

```csharp
bool[] truthTable = { false, true, true, false }; // x0 XOR x1
var circuit = DeutschJozsa.CreateCircuit(truthTable);
```

The demo uses three input bits: constant f(x)=1 produces input 000; balanced
f(x)=x0 XOR x2 produces input 101. Constant-one retains its global minus sign in
amplitudes. Table data is copied during construction and is not retained. Do not
mutate the supplied collection concurrently.

An explicit truth table is an educational oracle description: constructing and
checking it already reads the classical function values. This API does not give
a classical simulation the query advantage of an unknown quantum oracle.

## Grover search

CreateCircuit(inputQubits, markedBasisIndex, iterations = null) marks one explicit
integer input w. It prepares the uniform superposition |s>, then repeats a marked
phase flip and the exact diffuser D = 2|s><s| - I. With theta = asin(1/sqrt(N)):

```text
marked amplitude after k rounds = sin((2*k + 1)*theta)
each unmarked amplitude = cos((2*k + 1)*theta) / sqrt(N - 1)
success probability = sin((2*k + 1)*theta)^2
```

These are the amplitude-amplification relations described in the
[IBM Quantum Grover lesson](https://quantum.cloud.ibm.com/learning/en/modules/computer-science/grovers).
RecommendedIterations selects the nearest integer to pi/(4*theta) - 1/2 for the
first peak; the optional explicit count supports zero rounds and overshoot studies.
The diffuser includes the global minus sign required by its stated matrix.

```csharp
var circuit = GroverSearch.CreateCircuit(inputQubits: 3, markedBasisIndex: 5);
```

For this example the default is two rounds and success probability 121/128 for
input 101. Four candidates with one marked input reach probability one after
one round. With two candidates, the standard iteration does not improve on 1/2.
The marked index is supplied to construct the demonstration oracle; this API is
not an unknown-target classical database search. Multiple marked inputs and
user-supplied Grover oracles are outside this milestone.

## Input and scratch register layout

Deutsch–Jozsa and Grover assume an initially zero register, as provided by Simulate
and Run. Input bits occupy [0,n); max(0,n-2) scratch bits follow them. The circuit
therefore uses m = n + max(0,n-2) physical qubits. The builders support n=1–16,
subject to the generated-operation limit; n=16 uses the domain limit of 30 qubits.

To flip one basis state's phase, the synthesizer maps its zero bits to one using
X gates, computes a conjunction through a Toffoli ladder, applies CZ, and reverses
the ladder and X gates. For one or two inputs it uses Z or CZ directly. Scratch
qubits return to zero after each oracle and diffusion reflection, permitting reuse
in later rounds. They must not be treated as additional search inputs.

Measurement still covers the whole physical register. For n=3 the circuit has
four qubits, and count key 0101 means scratch 0 and input 101. Read the final n
characters for the input value; high scratch bits should all be zero. FinalState
remains uncollapsed after Run. A logical input w with zero scratch has physical
basis index w, so FinalState.Probability(w) is its probability.

## Resource limits and validation

Circuit generation is synchronous and cancellable. Each phase-oracle builder
limits output to 1,000,000 operations, checked before generating gates. Excessive
iterations or large synthesized truth-table oracles are rejected. The limit is
exposed as MaximumCircuitOperations, and MaximumInputQubits is 16. A constant-one
table is synthesized as a single global negation between Hadamard layers.

QFT uses O(n^2) gates and no scratch. Deutsch–Jozsa truth-table synthesis uses up
to O(n*2^n) gates. Grover uses O(n) gates per round. A scalar simulation of G gates
on m physical qubits costs O(G*2^m) work and 16*2^m bytes for amplitudes; terminal
sampling adds an 8*2^m-byte CDF. These are classical simulation costs.

The existing host-array budget applies to the physical register, independently of
the gate-count limit. With the default 512 MiB budget, phase-oracle algorithms
fit up to 13 input qubits for either simulation or terminal sampling, subject to
available memory and the oracle operation limit. Larger circuits can be built but
require an explicit larger simulation budget. Circuit objects and counts require
additional memory. No large state allocation is needed to reject invalid inputs.

## Independent verification

- QFT and inverse QFT match every analytic matrix column through five qubits;
  complex states match direct DFT summation, and inverse composition restores them.
- Ordered noncontiguous QFT subsets are checked against the analytic local matrix
  on an entangled four-qubit state.
- Every promised truth table through three input qubits matches a direct Walsh
  sum. Larger balanced tables test deeper scratch ladders and zero scratch output.
- Grover amplitudes, including signs after overshoot, match the closed-form
  expression for every marked input through four qubits and selected larger cases.
- Tests cover argument validation, promise rejection, cancellation, operation
  limits, terminal sampling, normalization, and scratch cleanup.

Absolute amplitude tolerance is 3e-12 for QFT and 1e-11 for the synthesized phase
algorithms. Full matrices and analytic sums are confined to tests; execution uses
only the simulator's own gates.
