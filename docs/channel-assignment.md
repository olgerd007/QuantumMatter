# Two-channel assignment with variational optimization

Run from the repository root:

```sh
dotnet run --project src/QuantumMatter.Demo -- channel-assignment
```

The example assigns six transmitters A-F to two channels. A synthetic, undirected
graph gives each interfering pair a positive integer weight. A pair contributes
residual interference when its endpoints use the same channel. There are no
capacity, balance, distance or regulatory constraints. These illustrative weights
are not measured radio data, and the result is not a deployment-ready radio plan.

| Pair | Weight |
| --- | ---: |
| A-B | 8 |
| A-C | 6 |
| B-C | 3 |
| B-D | 5 |
| C-E | 7 |
| D-E | 4 |
| D-F | 6 |
| E-F | 5 |
| A-F | 2 |

## Objective and encoding

Qubit q represents transmitter q (A is q0); its measured bit chooses channel 0 or
1. Printed bit strings have A on the right, following the simulator's little-endian
convention. The program prints transmitter names directly to avoid ambiguity.

For an assignment x, maximize separated weight

```text
C(x) = sum_(i,j) w_ij * [x_i != x_j].
residual interference = 46 - C(x).
H = -sum_(i,j) w_ij * (I - Z_i Z_j)/2.
```

This is weighted Max-Cut. A computational basis state's energy is exactly -C(x),
so minimizing expected energy maximizes expected separated weight. Independent
uniform channel choices separate half the edge weight on average: 23.
Swapping the names of the two channels leaves the objective unchanged.

## Experiment

The template has six Ry rotations, a five-CNOT chain, then six more Ry rotations.
All 12 angles are independently parameterized. Twelve starts use Random(42);
each takes 120 gradient-descent steps with learning rate 0.08. Gradients come from
the existing occurrence-wise parameter-shift evaluator. The lowest final energy
selects the circuit; exhaustive search never initializes or guides optimization.

The optimized circuit is sampled 2048 times with seed 42. Classical scoring selects
the best assignment observed in those samples, breaking ties by ascending basis
index. The program prints both channel groups, separated and residual weights,
expected separated weight, exact optimum, sampled objective gap and final-state
probability of an optimum. Expected score and best sampled score are different
quantities; finite shots do not guarantee observing an optimal assignment.

The exact baseline enumerates all 64 assignments, independently of circuit
optimization. Tests compare Hamiltonian energies and classical scores for every
basis assignment, verify a seeded end-to-end run, and check cancellation.

A local scalar-CPU run found channel 0 = A, D, E and channel 1 = B, C, F:
separated weight 39, residual weight 7, matching the exhaustive optimum with a
zero sampled objective gap. Its expected separated weight printed as 39.000000.
The channel-swapped assignment is equivalent. These results describe this small
fixture; they are not a guarantee for other graphs or optimization settings.

## Scope and costs

This is a small application example built entirely on existing SDK APIs. Core,
Application and Infrastructure contracts are unchanged. The cost model and
optimizer loop live in Demo; they are not a general channel-planning SDK.

The model uses six physical qubits without scratch. The host amplitude array is
1024 bytes and terminal sampling adds a 512-byte CDF; gradient buffers, gates,
objects and result counts add overhead. Each gradient needs 25 circuit executions
(one base plus two per rotation), so 1440 steps use 36,000 executions plus twelve
final energy evaluations and one sampled circuit execution.

The optimizer is heuristic and has no general convergence guarantee. This tiny
problem is cheaper to solve by classical enumeration; no quantum advantage is
claimed. Larger state-vector runs require exponential memory. Scalar CPU is the
default; backend arguments `parallel`, `parallel-simd` and `cuda` work through the
existing selection mechanism. CUDA requires compatible hardware and may incur
substantial transfer/dispatch overhead for this small register.
