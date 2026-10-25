# Pauli Hamiltonians and variational gradients

The **1.5.0-rc.1 candidate** adds real Pauli Hamiltonians, parameterized rotation
templates, and pure-state energy/gradient evaluation. It preserves existing circuit,
measurement, noise, dynamic and backend contracts. See the [release record](releases/1.5.0.md).

```csharp
using QuantumMatter.Application.Variational;
using QuantumMatter.Core;
using QuantumMatter.Core.Observables;
using QuantumMatter.Core.Variational;
using QuantumMatter.Infrastructure;

var hamiltonian = new PauliHamiltonian(1,
    [new PauliTerm(0.7, new PauliString("Z")), new PauliTerm(0.3, new PauliString("X"))]);
var template = new ParameterizedCircuit(qubits: 1, parameterCount: 1).Ry(target: 0, parameterIndex: 0);
var evaluator = new VariationalEnergyEvaluator(new CpuStateVectorBackend());
var result = evaluator.EvaluateWithGradient(template, hamiltonian, [0.73]);
// E = 0.7*cos(theta) + 0.3*sin(theta)
// dE/dtheta = -0.7*sin(theta) + 0.3*cos(theta)
```

## Hamiltonians and energies

PauliTerm has one finite real Coefficient and an immutable PauliString Observable.
PauliHamiltonian(qubits, terms) requires 1–30 qubits and matching nonnull terms.
It snapshots the term list and exposes a read-only view. Empty lists represent the
zero operator. Negative/zero weights, identity strings and duplicate terms are
allowed and retain their supplied order; coefficients are not aggregated.

For H = sum_k c_k P_k and M = psi^dagger psi, the energy is

```text
E(H) = Re(psi^dagger H psi) / M = sum_k c_k <P_k>.
```

StateVector.ExpectationEnergy computes that normalized value without modifying
amplitudes, sampling, constructing a dense Hamiltonian, or allocating an amplitude
snapshot. The separate method name preserves source compatibility for existing
ExpectationValue(null) calls. Even an empty/all-zero Hamiltonian checks finite
positive Born mass. Neumaier summation retains small contributions between larger
canceling terms. Energies are not restricted to [-1,1] and are never clamped.

Nonfinite intermediate/result arithmetic throws InvalidOperationException. Finite
coefficients alone do not guarantee representable sums: ordered partial sums can
overflow, and ill-conditioned inputs can lose digits. This is double-precision
arithmetic, not an arbitrary-precision guarantee. Existing phase, Pauli-label and
normalization conventions remain in the [observation guide](observations.md).

## Templates and binding

ParameterizedCircuit has a fixed qubit width and nonnegative ParameterCount.
Append snapshots every gate of a size-compatible constant QuantumCircuit. Rx, Ry
and Rz reference a parameter index in [0,ParameterCount). These symbolic gates are
uncontrolled Pauli rotations with angle exactly theta[index]. Constant snapshots
may contain any existing validated gate, including controlled rotations, but their
angles are not differentiated. There are no parameter expressions or implicit scales.

Bind accepts exactly ParameterCount finite values, including values for unused
indices, and returns an independently owned QuantumCircuit. Parameters may be shared
by several gates or unused. RotationParameterIndices is a live read-only view in
symbolic occurrence order; OperationCount counts constant and symbolic gates.

BindWithRotationShift selects one symbolic occurrence and composes a finite
same-axis rotation immediately after it. It does not shift other occurrences of a
shared parameter or numerically add the offset to the original angle. This realizes
R_a(theta + s) = R_a(s) R_a(theta), including phase, even when theta+s would round
back to theta in double precision. Ordinary platform trigonometric argument-reduction
limits still apply. A shifted binding has one additional gate.

Finish template construction before evaluating; templates are mutable and not
thread-safe. Do not mutate parameter buffers during binding/evaluation. Bound
circuits, Hamiltonian snapshots and returned gradient lists have independent ownership.

## Gradients and shared parameters

For an uncontrolled Pauli rotation exp(-i*theta*P/2), an occurrence's derivative
uses half the difference of the expectations with offsets +pi/2 and -pi/2 at that
occurrence. When a parameter occurs multiple times, the product rule adds the
contribution from each occurrence while holding all other gate angles fixed.
Shifting every occurrence together generally gives a wrong derivative. For example,
two Ry(theta) gates with H=Z have E=cos(2*theta) and derivative -2*sin(2*theta).
Reference: [Schuld et al., evaluating analytic gradients](https://arxiv.org/abs/1811.11184).

Evaluate performs one bound execution from |0...0>. EvaluateWithGradient performs
one base execution plus two per symbolic rotation occurrence, reusing/resetting one
state for every execution. It returns VariationalEnergyResult with Energy and an
immutable Gradient in parameter-index order. Unused parameters have zero derivative;
zero-parameter constant circuits return an empty gradient.

Shifted expectations are differenced separately for each Pauli term before applying
its coefficient and accumulating shared-parameter contributions. This prevents a
large constant identity energy from erasing a small gradient and avoids forming an
overflowing subtraction of opposite large Hamiltonian energies. Gradient sums use
compensation and reject nonfinite arithmetic. There is no finite-difference step,
shot sampling, optimizer policy or convergence guarantee in this evaluator.

The APIs evaluate unitary pure-state preparation only. An energy queried on one
noisy/dynamic trajectory describes that conditional state, not a mixed ensemble;
noise/dynamic variational workflows and stochastic gradient estimation are outside
this increment.

## Resources, cancellation and hardware

Let N=2^qubits, P=ParameterCount, T=Hamiltonian.Terms.Count and R be the number of
symbolic rotation occurrences. Evaluate preflights 16N host amplitude bytes.
EvaluateWithGradient preflights 16N+24P+8T bytes: one state, a parameter snapshot,
gradient and compensation arrays, and one reused array of positive-shift term
expectations. It never retains all shifted states or builds full matrices.
State energy queries cost O((T+1)N) work with constant auxiliary storage. Gradient
cost includes 1+2R complete evolutions and term-expectation scans; no speedup is claimed.

Circuit/term descriptions, bound gate lists, wrappers, runtime overhead and caller
snapshots are outside the host-array budget. CUDA device/staging budgets remain
separate. Array allocation/copying is not interruptible, but cancellation is checked
before execution and during binding, state scans and gradient accumulation.
No successful partial energy/gradient result is returned on cancellation or failure.

The evaluator constructor-injects IStateVectorBackend and SimulationOptions.
Scalar, parallel/SIMD and CUDA can prepare every state. Energy reductions and
gradient accumulation run on the host after CUDA's existing download, with a full
upload/download per circuit evaluation. There is no new backend interface, GPU
observable reduction, automatic backend selection, or runtime dependency. Callers
retain ownership and disposal responsibility for CUDA backends.

## Examples and validation

```sh
dotnet run --project src/QuantumMatter.Demo -- variational
dotnet run --project src/QuantumMatter.Demo -- variational parallel-simd
dotnet run --project src/QuantumMatter.Demo -- variational cuda
```

The demo composes the evaluator with a small caller-owned gradient-descent loop
and checks an analytic one-qubit ground energy. That example is not a general
optimizer or a convergence guarantee.

Independent dense tensor operators verify energies through four qubits, including
scaled complex states. Dense analytic gate derivatives propagate the product rule
through entangled mixed-axis circuits with shared parameters; central differences
are supplementary checks. Tests also cover large identity offsets/angles, overflow,
empty/unused parameters, snapshots, immutable results, exact budgets and failures.

Test-Packages.ps1 runs samples/VariationalConsumer in a fifth sibling directory
from fresh packages, leaving all four earlier consumer sources unchanged.
Test-PublishedPackages.ps1 opts into this candidate's APIs with -ValidateVariational;
omit that flag for older releases. See the [compatibility policy](compatibility.md).
