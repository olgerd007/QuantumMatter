# Partial measurement and Pauli observables

These additive APIs are included in **1.2.0**; the [release record](releases/1.2.0.md)
tracks publication. Libraries retain net9.0 assets and support .NET 9/10 consumers.

```csharp
var simulator = new StateVectorSimulator(new CpuStateVectorBackend());
var state = simulator.Simulate(new QuantumCircuit(3).H(0).CNot(0, 2).H(1).S(1));
double probability = state.MarginalProbability([2], 1);       // 0.5
double correlation = state.ExpectationValue(new PauliString("XIX")); // +1
int outcome = state.Measure([2, 0], new Random(42));          // 0 or 3
double coherence = state.ExpectationValue(new PauliString("IYI")); // still +1
```

## Selection and marginal probabilities

Pass a nonempty ordered selection of distinct qubits in [0,QubitCount). Bit k of
an outcome corresponds to qubits[k]; the first selected qubit is the least
significant output bit. For selection [2,0], outcome 1 means q2=1 and q0=0.
Printed two-bit outcome labels show q0 then q2. This differs from printing the
entire register, whose highest physical qubit is always on the left.

For selection S and packed outcome b, let Pi_b retain exactly the basis states
whose selected bits equal b. MarginalProbability returns

```text
M = sum_j |alpha_j|^2
p(b) = ||Pi_b psi||^2 / M
```

No state mutation or random sampling occurs. It returns zero for unsupported
outcomes within the valid outcome range; negative and out-of-range outcomes are
rejected. Unlike the existing Probability(basisIndex), which returns raw squared
magnitude, this method divides by the actual total mass to account for norm drift.
Neither query changes or silently normalizes the stored amplitudes.

## Projective collapse

Measure samples a full basis index with probability |alpha_j|^2/M, extracts its
selected bits, and retains the **whole** matching branch:

```text
psi' = Pi_b psi / sqrt(sum_(j matching b) |alpha_j|^2)
```

Summing the full-basis draw probabilities over each branch gives exactly p(b).
The discarded basis index is an internal sampling aid; it does not collapse
spectators. Relative and global phase survive on the selected branch. Entangled
qubits can become correlated with the outcome, while independent spectators keep
their coherence. Repeated measurement of the same selection is deterministic.

Exactly one NextDouble draw is used per successful measurement. Strict full-basis
CDF comparisons skip zero-mass plateaus; rounding fallback uses the last supported
basis index. Thresholds and seeded sequences follow full-basis order, rather than
a CDF sorted by packed subset outcomes. Reproducibility is within the same
runtime/backend; cross-runtime random sequences are not promised.

The existing MeasureAll contract stays unchanged: it sets the selected basis
amplitude to +1. Measure with a selection of every qubit retains the surviving
global phase instead. Run still samples independent terminal shots without collapse.
These are explicit host-state operations between Evolve calls; they do not add
measurement instructions, classical registers or feedback to QuantumCircuit.

## Pauli expectations

PauliString accepts 1 through 30 uppercase I/X/Y/Z labels, with the rightmost
label acting on q0, as in printed kets. "XYZ" means X on q2, Y on q1, Z on q0.
Its length must match the state, including identity factors. Signs, scalar
coefficients, lowercase labels and arbitrary matrices are rejected.

ExpectationValue returns the real normalized quadratic form

```text
E(P) = Re(psi^dagger P psi) / M
P|j> = i^(number of Y factors) (-1)^popcount(j & signMask) |j XOR flipMask>
flipMask selects X/Y factors; signMask selects Y/Z factors.
```

For normalized states a Pauli expectation lies in [-1,1], up to floating-point
roundoff; values are not clamped. No sampling error is introduced. The Bell state
(|00>+|11>)/sqrt(2) has <XX>=<ZZ>=+1 and <YY>=-1. The +i eigenstate of Y has
<Y>=+1, distinguishing coherent phase from measurement probabilities.
Weighted sums can be formed by callers from individual expectations. A noisy
trajectory expectation describes that conditional pure state, not an ensemble;
do not interpret one trajectory as a mixed-state expectation.

## Cost, cancellation and validation

Each query and measurement costs O(2^n) work with constant auxiliary storage.
Pauli masks are compiled once per immutable observable; there is no full matrix,
amplitude snapshot, marginal array or CDF allocation. Existing state/device budgets
and ownership still apply. Observation runs on the host buffer after backend
execution, including CUDA's existing final download; no GPU reduction is claimed.

Arguments are checked before mutation or randomness. Nonfinite or nonpositive
total Born mass is rejected. Cancellable scans check at least every 4096 indices.
Partial measurement performs all validation and cancellable scans before a final
noncancellable O(2^n) projection, so cancellation observed before commit leaves
the state unchanged. Concurrent access to the mutable StateVector is unsupported.

Tests build independent tensor-product Pauli matrices through four qubits and
projectors for every nonempty subset through five qubits. They check complex
phases, ordered selections, exact boundaries, spectator coherence, invalid mass,
normalization, seeds, cancellation and all enabled backend preparation paths.

References: [projective measurement](https://learning.quantum.ibm.com/course/basics-of-quantum-information/quantum-circuits)
and [observable expectations](https://learning.quantum.ibm.com/course/variational-algorithm-design/cost-functions).

Run the example with a selected backend:

```sh
dotnet run --project src/QuantumMatter.Demo -- observations
dotnet run --project src/QuantumMatter.Demo -- observations parallel-simd
dotnet run --project src/QuantumMatter.Demo -- observations cuda
```
