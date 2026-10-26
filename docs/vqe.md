# Deterministic pure-state VQE

Version **1.6.0** adds Application/Variational.VqeSolver. It composes
the existing [energy/gradient evaluator](variational.md) with deterministic
gradient descent and bounded Armijo backtracking. Core, backend ports and all 283
earlier API declarations remain unchanged. See the [validation record](releases/1.6.0.md).

```csharp
using QuantumMatter.Application.Variational;
using QuantumMatter.Core;
using QuantumMatter.Core.Observables;
using QuantumMatter.Core.Variational;
using QuantumMatter.Infrastructure;

var h = new PauliHamiltonian(1,
    [new PauliTerm(0.7, new PauliString("Z")), new PauliTerm(0.3, new PauliString("X"))]);
var circuit = new ParameterizedCircuit(1, 1).Ry(0, 0);
var result = new VqeSolver(new CpuStateVectorBackend()).Solve(circuit, h, [0.73]);
Console.WriteLine($"{result.Energy}: {result.TerminationReason}");
```

VQE combines parameterized state preparation and Hamiltonian expectations with
classical optimization; see [Peruzzo et al.](https://www.nature.com/articles/ncomms5213).
This simulator computes pure-state expectations and offers no hardware speedup claim.

## Policy and mathematical contracts

Solve copies an exact-width finite parameter vector and evaluates its initial
normalized energy. Before each update it requests the existing occurrence-wise
parameter-shift gradient, including shared/unused parameters. The stationarity
test is the infinity norm max(abs(g_i)). For positive step t, the candidate is
theta_trial = theta - t*g. Acceptance requires strictly lower energy and:

```text
E(theta) - E(theta_trial) >= c * t * sum_i(g_i^2).
```

Every search starts at InitialStepSize. Rejection reduces t by BacktrackingFactor,
subject to MinimumStepSize and the search-attempt limit. The
[Boyd/Vandenberghe notes](https://web.stanford.edu/class/ee364a/lectures/unconstrained.pdf)
describe sufficient-decrease backtracking. This objective is generally nonconvex;
convex convergence guarantees do not apply.

Updates use fused multiply-add. Norms are scaled before squaring; the positive
decrease comparison uses logarithms to avoid overflow/underflow on either side.
The actual floating-point candidate must still decrease reported energy. Accepted
energies are monotone for a deterministic backend. Rejected trials, including lower
energies failing Armijo, are excluded from results. No angle wrapping, energy
clamping or implicit normalization is introduced.

| VqeOptions setting | Default | Accepted range |
| --- | --- | --- |
| MaximumIterations | 200 | Nonnegative accepted-update limit |
| MaximumCircuitEvaluations | 100000 | Positive long; every complete circuit execution |
| MaximumLineSearchIterations | 40 | Positive; attempted candidates, including nonfinite ones |
| GradientTolerance | 1e-6 | Finite, nonnegative absolute infinity norm |
| InitialStepSize | 1 | Finite and positive |
| MinimumStepSize | 1e-12 | Finite, positive and no larger than initial step |
| BacktrackingFactor | 0.5 | Finite and strictly between zero and one |
| SufficientDecrease | 0.1 | Finite and strictly between zero and one |

## Results and stopping

VqeResult owns immutable Parameters and the Energy of the last accepted iterate
(initial iterate when no update is accepted), plus IterationCount and:

| TerminationReason | Meaning |
| --- | --- |
| GradientToleranceReached | Current gradient infinity norm meets tolerance |
| IterationLimitReached | Accepted-update limit reached; final gradient may be unchecked |
| EvaluationLimitReached | Remaining budget cannot fund the next complete energy/gradient call |
| LineSearchStalled | No sufficient-decrease representable candidate within search/step limits |

The update limit is checked before another gradient; zero evaluates initial energy
only. Gradient tolerance is checked after a full gradient call, before trials.
Within a search the execution budget is checked before each attempt; an unsuccessful
search finishing its attempt/step limits returns LineSearchStalled.

Stationarity may be a maximum, saddle, plateau or restricted-ansatz minimum.
Ry(0) with H=Z is stationary at energy +1 while the ground energy is -1.
There is no global-ground-state or universal convergence guarantee. Huge parameter
values, tiny steps or dominant identity offsets may prevent representable progress;
this returns stalled, rather than incorrectly declaring gradient convergence.

EnergyEvaluationCount counts standalone Evaluate calls: initial energy and evaluated
trials. GradientEvaluationCount counts full EvaluateWithGradient calls, including
their base energy. For R symbolic rotation occurrences, accounting is exact:

```text
CircuitEvaluationCount = EnergyEvaluationCount + (1 + 2*R)*GradientEvaluationCount.
```

The full gradient cost must fit before calling it. Unused budget smaller than 1+2R
may remain at termination. Nonfinite/unchanged parameter candidates execute no backend.
Shared parameters are charged per occurrence, not distinct parameter index.

## Ownership, resources and failures

The solver injects a caller-owned IStateVectorBackend and immutable VqeOptions and
SimulationOptions. CUDA disposal remains with the caller. No new backend port,
runtime project, runtime dependency or generic optimizer abstraction is introduced. Finish template
construction first and do not mutate inputs during Solve. Calls own separate
arrays/results; concurrent execution still requires a suitable backend.

For N=2^qubits, P=ParameterCount and T=term count, every call conservatively
preflights **16N+48P+8T** live host-array bytes, even if configured to stop early.
This covers two solver parameter arrays, a previous immutable gradient potentially
overlapping the next evaluation, and evaluator state/parameter/gradient/compensation/
term buffers. The nested evaluator receives a reduced budget reserving solver arrays.
State resets/reuses within an evaluator call; different calls allocate fresh states.
The budget excludes uncollected dead arrays, runtime/circuit metadata, wrappers,
caller snapshots and separate CUDA device/staging buffers. It does not guarantee
available physical memory. No iterate or state history is retained.

Cancellation propagates through loops, evaluator scans and backend execution.
Cancellation, allocation failures, backend errors and nonfinite state/energy/gradient
arithmetic throw without a successful partial result. Only nonfinite *trial
parameters* backtrack without execution. Invalid evaluated states or nonfinite
energies are failures, not silently rejected steps hiding broken arithmetic.

## Examples and scope

```sh
dotnet run --project src/QuantumMatter.Demo -- vqe
dotnet run --project src/QuantumMatter.Demo -- vqe parallel-simd
dotnet run --project src/QuantumMatter.Demo -- vqe cuda
```

The one-qubit example H=-0.2I+0.7Z+0.3X has ground energy -0.2-sqrt(0.58).
The two-qubit H=XX+YY+ZZ has singlet ground energy -3. The X(0), Ry(1,theta),
CNot(1,0) ansatz prepares cos(theta/2)|01>+sin(theta/2)|10>, with E=-1+2sin(theta).
Independent dense eigenpair/residual tests check that entangled result.

This increment supports exact unitary pure-state preparation with v1.5 rotation
parameters. Noise/shot objectives, noisy/dynamic gradients, parameter bounds or
expressions, restarts, optimizer plugins and device-resident optimization are outside
scope. Existing scalar, parallel/SIMD or CUDA backends execute preparations;
reductions, parameter updates and line search stay on the host.

The sixth sibling samples/VqeConsumer consumes fresh packages. Public-feed checks
opt in with -ValidateVqe; all five earlier consumer sources and modes are preserved.
