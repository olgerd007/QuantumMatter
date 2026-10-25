# v0.3 gate reference

All matrices act on column vectors. Angles are finite radians, qubit q is bit q,
and gates execute in insertion order. See [mathematical conventions](mathematics.md)
for indexing and measurement. This reference defines the v0.3 release gate set.

## Single-qubit gates

In the table, c = cos(theta/2), s = sin(theta/2), and dagger means conjugate transpose.
Every target must be in [0, QubitCount). Fixed gates have no angle parameter.

| Circuit method | Matrix (rows separated by semicolons) | Adjoint |
| --- | --- | --- |
| I(q) | [[1, 0]; [0, 1]] | I(q) |
| H(q) | [[1, 1]; [1, -1]] / sqrt(2) | H(q) |
| X(q) | [[0, 1]; [1, 0]] | X(q) |
| Y(q) | [[0, -i]; [i, 0]] | Y(q) |
| Z(q) | [[1, 0]; [0, -1]] | Z(q) |
| S(q) | [[1, 0]; [0, i]] | Sdg(q) |
| Sdg(q) | [[1, 0]; [0, -i]] | S(q) |
| T(q) | [[1, 0]; [0, exp(i*pi/4)]] | Tdg(q) |
| Tdg(q) | [[1, 0]; [0, exp(-i*pi/4)]] | T(q) |
| Phase(q, theta) | [[1, 0]; [0, exp(i*theta)]] | Phase(q, -theta) |
| Rx(q, theta) | [[c, -i*s]; [-i*s, c]] | Rx(q, -theta) |
| Ry(q, theta) | [[c, -s]; [s, c]] | Ry(q, -theta) |
| Rz(q, theta) | [[exp(-i*theta/2), 0]; [0, exp(i*theta/2)]] | Rz(q, -theta) |
| U(q, unitary) | Validated [[M00, M01]; [M10, M11]] | U(q, unitary.Adjoint()) |

Phase(theta) and Rz(theta) differ by exp(i*theta/2) on the whole local matrix.
That phase becomes relative between branches when either operation is controlled.
In particular, controlled Rz(2*pi) is not identity, while controlled Phase(2*pi) is.

## Controlled gates

Controlled(kind, control, target, angle = 0) applies a built-in single-qubit gate
only when the control bit is one. The control and target must be distinct and valid.
Either may be the higher-index qubit. The angle is used by Phase, Rx, Ry, and Rz;
it must be finite even when supplied for a fixed gate.

Allowed kinds are I, H, X, Y, Z, S, Sdg, T, Tdg, Phase, Rx, Ry, and Rz.
The convenience methods are CH, CY, CZ, CS, CSdg, CT, CTdg, CPhase, CRx, CRy,
and CRz. Each takes (control, target), with the angle as the last argument when
needed. CNot(control, target) is the existing controlled-X method; generic
Controlled(GateKind.X, control, target) has the same action.

ControlledU(control, target, unitary) applies a validated custom unitary under
the same condition. All controlled gates implement

```text
C(U) = |0><0|_control tensor I_target + |1><1|_control tensor U_target
```

with identity on spectator qubits and the repository's qubit ordering. The adjoint
is C(U-dagger) with the same operands. Thus CH, CY, CZ, and CNOT are self-inverse;
CS/CSdg and CT/CTdg are adjoint pairs; negate the angle for controlled rotations
and phase gates. ControlledU uses unitary.Adjoint().

## Permutation gates

| Circuit method | Basis-state action | Adjoint |
| --- | --- | --- |
| CNot(control, target) | target := target XOR control | Same gate |
| Swap(a, b) | Exchange bits a and b | Same gate |
| Toffoli(firstControl, secondControl, target) | target := target XOR (firstControl AND secondControl) | Same gate |

All operands must be distinct and in range. Toffoli controls are symmetric and
activate on one. Spectators and complex amplitudes retain their phases.

## Custom unitary validation

SingleQubitUnitary is an immutable value object. Create(Complex[,]) requires
exactly two rows and two columns and copies the four entries. The four-coefficient
overload uses row-major order: Create(m00, m01, m10, m11). Both reject nonfinite
real or imaginary components.

Construction requires orthonormal columns, equivalent to U-dagger U = I:

```text
abs(|m00|^2 + |m10|^2 - 1) <= 1e-12
abs(|m01|^2 + |m11|^2 - 1) <= 1e-12
abs(conjugate(m00)*m01 + conjugate(m10)*m11) <= 1e-12
```

The fixed absolute tolerance is SingleQubitUnitary.ValidationTolerance. Matrices
are never silently normalized, and callers cannot weaken validation. Passing the
check permits small floating-point errors; repeated application can accumulate
drift. Adjoint returns the conjugate transpose. U and ControlledU reject null
matrices and invalid operands before appending an operation.

```csharp
using System.Numerics;
using QuantumMatter.Core;

var unitary = SingleQubitUnitary.Create(0, Complex.ImaginaryOne, 1, 0);
var circuit = new QuantumCircuit(3)
    .H(0)
    .ControlledU(0, 1, unitary)
    .Toffoli(0, 1, 2)
    .CPhase(1, 2, Math.PI / 3)
    .U(2, unitary.Adjoint());
```

General multi-target custom matrices, control-on-zero, and more than two controls
are outside this release gate set.

## Backend operation encoding and conformance

For a single-qubit GateOperation, Kind identifies its local matrix; Control = -1
means unconditional execution and Control >= 0 enables it on one. Unitary stores
the custom matrix for Kind = Unitary. CNot uses Control and Target; Swap stores
its first operand in Control and its second in Target. Toffoli also uses
SecondControl. Other operations leave SecondControl = -1. QuantumCircuit is the
validated construction boundary; raw GateOperation values cannot be appended.

The scalar backend forms a control mask once per operation and updates only
matching amplitude pairs or diagonal entries. It constructs no dense operator and
does not discard global phase. Existing GateKind numeric values are retained;
new kinds are appended.

Existing four-argument GateOperation construction and four-value deconstruction
remain supported. Backend authors must now honor Control on every single-qubit
kind and implement the new gate kinds; ignoring Control would execute an
unconditional gate and violate the contract.

StateVectorBackendConformanceTests is an abstract xUnit suite. A backend fixture
implements CreateBackend() and inherits known matrix-column tests, all basis and
operand-order checks, dense-oracle comparisons, inverse/norm checks, and
cancellation checks. CpuBackendConformanceTests registers the scalar backend;
v0.5 fixtures also register one/two/three-worker and scalar/parallel SIMD modes.
The dense oracle uses separate row/column multiplication and independent gate
definitions. Mixed circuits include every gate kind and generated complex unitaries.

Known-column/basis tolerance is 1e-12; mixed extended-oracle tolerance is 3e-12;
long inverse and norm tolerance is 1e-11. These limits apply to the documented
test cases, not arbitrary circuit depth.
