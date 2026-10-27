# Mathematical conventions

The [VQE guide](vqe.md) defines the v1.6 optimizer's gradient-stationarity test,
Armijo sufficient-decrease rule, occurrence-wise execution accounting and finite
precision limits. It preserves the state, gate and measurement conventions below.

## State and indexing

An n-qubit pure state is the column vector

```text
|psi> = sum_(j=0)^(2^n-1) alpha_j |j>
alpha_j in Complex (two IEEE 754 doubles)
sum_j |alpha_j|^2 = 1
P(j) = |alpha_j|^2 = Re(alpha_j)^2 + Im(alpha_j)^2
```

Initialization is alpha_0 = 1 and every other amplitude zero.
Qubit q is bit q of the basis index: j = sum_q b_q 2^q. Printed strings are
|b_(n-1)...b_1 b_0>. For example, X(0) on three zero qubits produces |001>,
index 1. The tensor-product order is q_(n-1) through q_0.

Operations act on the left of the column vector, in circuit insertion order:
a circuit appending U then V produces V U |psi>. Both global and relative
phase are retained in amplitudes. Probabilities alone do not determine a state.

## Single-qubit gates

For a target q, pair indices j and k = j + 2^q with bit q of j equal to zero.
For U = [[a,b],[c,d]], update using both original amplitudes:

```text
alpha'_j = a alpha_j + b alpha_k
alpha'_k = c alpha_j + d alpha_k
```

Every other qubit labels a separate pair. This applies the tensor product of U
with identity on every spectator without constructing the full matrix.

```text
H = (1/sqrt(2)) [[1,  1], [1, -1]]
X = [[0,  1], [1,  0]]
Y = [[0, -i], [i,  0]]
Z = [[1,  0], [0, -1]]
S = [[1,  0], [0,  i]]
T = [[1,  0], [0, exp(i*pi/4)]]

Rx(theta) = [[cos(theta/2),   -i*sin(theta/2)],
             [-i*sin(theta/2), cos(theta/2)]]

Ry(theta) = [[cos(theta/2), -sin(theta/2)],
             [sin(theta/2),  cos(theta/2)]]

Rz(theta) = [[exp(-i*theta/2), 0],
             [0, exp(i*theta/2)]]
```

Angles are finite radians. Rotations follow R_P(theta) = exp(-i theta P / 2)
for Pauli P. H, X, Y, Z, CNOT, and SWAP are self-inverse.
R_P(theta)^dagger = R_P(-theta). S^4 = I and T^8 = I.
A 2*pi rotation is -I, so it returns the same measurement probabilities
but not the same amplitudes.

Identity, S/T adjoints, Phase(theta) = diag(1, exp(i*theta)), controlled
single-qubit gates, Toffoli, and custom 2x2 unitaries are also supported. Custom
matrices must have finite entries and satisfy U^dagger U approximately equal to I
with a fixed absolute tolerance of 1e-12 before they can be appended. The
[gate reference](gates.md) lists every matrix, adjoint, parameter domain, and
control convention.

## Two-qubit gates

CNot(control, target) maps the target bit to target XOR control and leaves the
control unchanged. The qubits must differ; either may have the higher index.
The CPU kernel swaps each pair with control set and target clear exactly once.

Swap(a, b) exchanges bits a and b and requires distinct qubits. Swapping identical
bit values is a no-op; opposite values are exchanged once. These are permutations,
so complex phases move together with their amplitudes.

H(0) followed by CNot(0,1) produces (|00> + |11>)/sqrt(2).

For any supported single-qubit U, its controlled form applies U only on the
control-one branch and identity on the control-zero branch. Preserve all of U's
phase: a controlled 2*pi Pauli rotation is not identity. Toffoli flips its target
only when both distinct control qubits are one; all three operands must differ.

## Measurement and sampling

MeasureAll uses the Born distribution for a full-register projective measurement.
For total mass M = sum_j P(j), draw u uniformly in [0, M) and choose the first
index with cumulative mass strictly greater than u. Scaling by the computed M
accounts for small floating-point norm drift. Zero-mass plateaus are skipped;
a rounding fallback selects the last supported basis state, never an arbitrary
trailing zero-probability state. Nonfinite or nonpositive M is an error.

After measurement, the selected amplitude is set to 1 and every other amplitude
to 0. This fixes an irrelevant global phase for the collapsed basis state.
Repeated measurement is deterministic.

Run performs independent terminal shots from the uncollapsed final pure state.
It uses a cumulative array and strict upper-bound binary search. Counts sum to
the requested number of positive shots and use zero-padded n-bit strings.
Run does not insert measurement into a circuit and does not collapse FinalState.
QuantumCircuit has no mid-circuit measurement, classical registers/control or
reset instructions. StateVector.Reset restores a host state to |0...0>.
Mixed-state noise has a separate stochastic trajectory API, described below.

Version 1.2 also supports explicit partial measurement between evolution
calls. For subset projector Pi_b, its outcome has probability ||Pi_b psi||^2/M
and leaves state Pi_b psi/||Pi_b psi||. Unmeasured coherences and surviving phases
are preserved. Pauli expectations use Re(psi^dagger P psi)/M, with rightmost
Pauli label on q0. These new normalized queries do not modify stored amplitudes;
the existing Probability method still returns raw squared magnitude. See the
[observation guide](observations.md) for selection ordering and implementation costs.

Version 1.3 introduces a separate DynamicCircuit with explicit mid-circuit
measurement and classical feedback. Each history follows the Born rule and executes
only unitary segments matching its classical register. Reset of qubit q samples
R0=|0><0| or R1=|0><1| locally, yielding Rj psi/||Rj psi|| with Born weight; the
ensemble channel is sum_j Rj rho Rj^dagger. It is not a coherent sum of branches.
Run repeats the full history independently for each shot and counts only explicitly
written classical bits. See the [dynamic guide](dynamic-circuits.md).

Version 1.4 composes those dynamic histories with explicit Kraus channels.
Noise acts on the current conditional state and leaves stored classical bits
unchanged; later measurements/control use that history. Each final classical label
groups a sum of weighted outer products, even when overwrites merge earlier records.
Never coherently combine those histories or sample all shots from one noise branch.
See the [noisy dynamic guide](noisy-dynamic-circuits.md).

The v1.5 candidate defines H=sum_k c_k P_k for finite real coefficients and
matching Pauli strings. Energy queries return sum_k c_k*Re(psi^dagger P_k psi)/M
without modifying state. For each uncontrolled Pauli rotation occurrence,
the parameter derivative uses half the +pi/2 versus -pi/2 expectation difference.
Shared parameters sum all occurrence contributions by the product rule. Shifts
are composed as same-axis rotations, and term expectations are differenced before
weighting, preserving phase and avoiding loss from large constant offsets.
See the [variational guide](variational.md) for numerical limits and reference equations.

## Numerical validation

The reference backend uses double-precision Complex arithmetic and does not
renormalize after each gate. Norm drift accumulates with gate depth; sampling
rescales the draw but does not modify amplitudes or conceal kernel defects.
Finite rotation angles are accepted over the double range; extreme angles inherit
the platform math library's argument-reduction precision limitations.

Tests compare known complex matrix columns, interference, Bell/GHZ amplitudes,
all four-qubit basis permutations in both operand orders, and inverse circuits.
A separate dense operator/tensor-product oracle checks seeded circuits on
one through five qubits. Full matrices exist only in tests.

Typical absolute amplitude tolerance is 1e-12, 2e-12 for randomized oracle
comparisons, and 1e-11 for long-circuit norm/inverse checks. These tolerances
describe the tested cases, not a universal error guarantee at arbitrary depth.
Future backends must declare their precision and pass equivalent conformance
tests before performance comparisons.

The extended gate conformance suite additionally checks generated complex unitaries,
controlled forms, and mixed circuits containing every gate kind against the dense
oracle with a 3e-12 amplitude tolerance. See the gate reference for the reusable
backend test fixture and custom matrix validation contract.

The CUDA backend preserves these double-precision contracts using an explicit
device struct containing real and imaginary doubles. Host coefficients are prepared
with System.Numerics.Complex; kernels apply pair transforms and permutations with
no renormalization or fast/reduced-precision math. CPU and device instruction
rounding can differ, so amplitudes are compared with the existing absolute
tolerances rather than requiring bitwise identity. The [GPU guide](gpu-execution.md)
records hardware conformance and measured error.

## Algorithm conventions

The QFT uses F[y,x] = exp(2*pi*i*x*y/2^n)/sqrt(2^n), including final bit reversal;
its inverse conjugates the exponential. Selected qubits are ordered from local
least to most significant bit. Deutsch–Jozsa uses phase-oracle synthesis and
Grover uses D = 2|s><s| - I with its full global phase. Both place input bits below
initially-zero scratch bits, which are uncomputed before measurement. See the
[algorithm guide](algorithms.md) for definitions, references, and analytic tests.

## Quantum noise

Noise channels use a separate trajectory port; unitary evolution remains unchanged.
For Kraus operators K_j with sum K_j^dagger K_j = I, the ensemble map is
rho' = sum K_j rho K_j^dagger. A trajectory samples j with probability
||K_j psi||^2 and returns K_j psi / ||K_j psi||. Ensemble quantities average
outer products or probabilities, never amplitudes. Bit/phase flip, depolarizing
and damping definitions, parameter conventions and conditional normalization are
specified in the [noise guide](noise.md), together with statistical errors and
independent density-matrix validation.
