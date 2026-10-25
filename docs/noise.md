# Quantum noise trajectories (v0.7)

Noisy simulation samples normalized conditional pure states. The mixed ensemble is
rho = E[|psi><psi|]; averaging amplitudes is incorrect. No production density matrix
or external quantum library is used. All arithmetic uses double precision.

## API and placement

```csharp
using QuantumMatter.Application.Noise;
using QuantumMatter.Core;
using QuantumMatter.Core.Noise;
using QuantumMatter.Infrastructure;

var circuit = new NoisyCircuit(1)
    .Append(new QuantumCircuit(1).X(0))
    .ApplyNoise(NoiseChannel.AmplitudeDamping(0.3), target: 0);
var simulator = new NoisyStateVectorSimulator(
    new CpuStateVectorBackend(), new CpuNoiseChannelBackend());
var samples = simulator.Run(circuit, shots: 10000, seed: 42);
var probabilities = simulator.EstimateProbabilities(circuit, trajectories: 10000, seed: 42);
ProbabilityEstimate excited = probabilities.Estimate(1);
Console.WriteLine($"P(1) = {excited.Probability}, estimated SE = {excited.StandardError}");
```

Append copies the unitary segment immediately; later edits to its source do not
affect the noisy circuit. ApplyNoise inserts one channel at exactly that point.
Another Append places gates after the channel. There is no implicit insertion
after every gate or calibration model. Targets use little-endian indexing and all
segments match the register size. Invalid additions leave the builder unchanged.
Do not mutate builders/states during execution.

The original QuantumCircuit, IStateVectorBackend, StateVectorSimulator and
SimulationResult remain unitary. Noise uses INoiseChannelBackend and separate results:

- SimulateTrajectory returns one unmeasured conditional StateVector, not a mixed
  ensemble's final ket.
- Run executes a fresh trajectory per shot and measures it once, returning Shots
  and immutable Counts. It has no FinalState and never draws every shot from one
  selected noise branch.
- EstimateProbabilities averages computational-basis Born probabilities without
  terminal measurement. It returns probabilities and estimated standard errors,
  not amplitudes, off-diagonal coherences or a density matrix.

StateVector.EvolveTrajectory accepts an existing valid pure state, injected
backends and Random. StateVector.Reset restores |0...0> on the host; it is not a
mid-circuit reset instruction.

## Channel conventions

Factories require finite parameters in [0,1]. Immutable channels act only on the
target, with spectator identities. The map is E(rho) = sum_j K_j rho K_j^dagger.
Each satisfies sum_j K_j^dagger K_j = I, preserving trace.

| Factory | Single-qubit map | Kraus operators |
| --- | --- | --- |
| BitFlip(p) | (1-p) rho + p X rho X | sqrt(1-p) I, sqrt(p) X |
| PhaseFlip(p) | (1-p) rho + p Z rho Z | sqrt(1-p) I, sqrt(p) Z |
| Depolarizing(p) | (1-p) rho + p I/2 | sqrt(1-3p/4) I, sqrt(p/4) X, sqrt(p/4) Y, sqrt(p/4) Z |
| AmplitudeDamping(gamma) | zero-temperature relaxation | K0 = diag(1,sqrt(1-gamma)); K1 = [[0,sqrt(gamma)],[0,0]] |

For entangled registers, depolarization mixes rho with the maximally mixed target
tensored with the reduced spectator state in the existing qubit order. It does
not replace the whole global state by I/2. The Pauli error probability is 3p/4,
not p; p=1 completely depolarizes the target. This implementation restricts the
replacement parameter to [0,1]. See the
[replacement-channel definition](https://qiskit.github.io/qiskit-aer/stubs/qiskit_aer.noise.depolarizing_error.html).
PhaseFlip(1) is deterministic Z; PhaseFlip(0.5) removes single-qubit coherence.
BitFlip(1) is deterministic X. Zero-parameter channels are exact identity.

Damping gives rho_11' = (1-gamma) rho_11 and rho_01' = sqrt(1-gamma) rho_01.
The ground state is unchanged. These are the zero-excited-population
[amplitude-damping Kraus operators](https://qiskit.github.io/qiskit-aer/stubs/qiskit_aer.noise.amplitude_damping_error.html).
Sample branch j with probability ||K_j psi||^2, then set
psi <- K_j psi / ||K_j psi||. Conditional normalization is mathematically required;
unitary gates still never renormalize after execution.

Damping uses compensated target-zero/one masses m0,m1. Jump mass is gamma*m1;
no-jump mass is m0+(1-gamma)*m1, avoiding subtractive cancellation. Probabilities
use their actual total to account for small unitary norm drift. Zero-weight
branches cannot be selected. Jump preserves phase and spectator correlations.

Sampling uses strict boundaries: u < probability selects a branch; equality picks
the next interval. Order is error/identity for bit/phase flip, X/Y/Z/I for
depolarization, jump/no-jump for damping. Zero channels, deterministic bit/phase
flips and deterministic damping branches consume no draw. Nonzero depolarization
uses one draw, including at p=1.

## Estimation and reproducibility

For M trajectories with Born probabilities p_t(j), the estimator computes:

```text
mean_j = sum_t p_t(j) / M
M2_j = sum_t (p_t(j) - mean_j)^2
SE_j = sqrt(M2_j / (M * (M - 1)))
```

Welford updates compute mean/M2 online; at least two trajectories are required.
Probabilities are divided by their actual finite positive total, as in terminal
sampling, accounting for roundoff without changing the ket. The variance buffer
becomes the SE result buffer; no trajectory history is stored.

SE measures noise-trajectory sampling uncertainty only. It excludes measurement,
roundoff, model and calibration errors. A noise-free superposition has zero
trajectory SE while its counts still fluctuate. SE is an estimate, not a guaranteed
confidence interval; it can be zero if a rare branch was never seen. Typical error
decreases as 1/sqrt(M). Count frequencies f additionally have approximate binomial
standard error sqrt(f*(1-f)/shots).

A seed creates a local Random per call. Sequential trajectories share its stream.
Run interleaves channel and measurement draws; EstimateProbabilities has no
measurement draws, so branch histories need not match for the same seed.
Reproducibility applies to the same API, runtime, backend and circuit, not across
versions/devices. Concurrent seeded calls have separate random streams.

## Architecture, memory and cancellation

Core owns channel descriptions, noisy circuits and the noise port. Application
orchestrates trajectories/results. Infrastructure supplies stateless CPU channel
kernels, O(N) work and O(1) extra amplitude storage for N=2^n. Backends are trusted
to honor their normalized-state and buffer-ownership contracts.

SimulateTrajectory/Run use one reusable 16N-byte state; noisy Run uses linear
measurement without an N-sized CDF. Estimation adds two 8N-byte arrays, 32N bytes
total. SimulationOptions.MemoryBudgetBytes preflights these arrays. The 512 MiB
default permits at most 25 qubits for single trajectories/noisy Run and 24 for
estimates. These are allocation limits, not throughput promises. Circuit snapshots,
counts, object/runtime overhead, caller-owned results, concurrent calls and backend
staging/device memory are additional. No performance improvement is claimed.

Scalar, parallel and CUDA backends can execute unitary segments. Noise runs on
the CPU. Each CUDA segment uploads/downloads normally, so noise boundaries
interrupt residency. No GPU noise kernels or trajectory batching are included.
CUDA ownership follows the [GPU guide](gpu-execution.md).

Cancellation propagates before allocation, between trajectories/steps, through
unitary/measurement execution and every 4096 iterations in channel/estimation loops.
Array allocation/clear and active CUDA work are noncancellable. Direct
EvolveTrajectory can leave partial state; Application methods throw rather than
return partial results. Channels retain neither state nor Random. Independent calls
may share stateless CPU backends; caller-owned mutable states/random sources must
not be shared unsafely. Noise introduces no new packages or native resources.

## Examples and verification

```sh
dotnet run --project src/QuantumMatter.Demo -- noise
dotnet run --project src/QuantumMatter.Demo -- noise parallel-simd
dotnet run --project src/QuantumMatter.Demo -- noise cuda
dotnet test QuantumMatter.sln
dotnet test QuantumMatter.sln -p:EnableCudaTests=true
```

On the local .NET 9 runtime, the gamma=0.3 example on |1>, with 10,000 trajectories
and seed 42, gives P(1)=0.6994, SE=0.004585, against analytic 0.7. The separate
measurement run returns 3,017 zeros and 6,983 ones.

Tests compare every branch with independent dense Kraus operators on complex,
entangled states across every target on 1–4 qubits. Weighted outer products check
full density matrices and trace. A separate two-qubit density calculation checks
interleaved channels/unitaries. Further checks cover analytic channels, Bell
damping/coherence, p=0/1, boundaries, impossible branches, zero-noise equivalence,
seeds, standard errors, memory, offset buffers, concurrency and cancellation.
CUDA-enabled tests compare hybrid trajectories against scalar execution.

Scope is independent single-qubit Markov channels at explicit circuit positions.
Continuous-time/Lindblad evolution, arbitrary Kraus input, density-matrix execution,
correlated noise, thermal excitation, readout errors, mid-circuit measurement,
classical feedback and automatic calibration remain outside NoisyCircuit's scope.
Version 1.3 provides separate [dynamic circuits](dynamic-circuits.md) with
measurement, feedback and reset; combining them with noise remains future work.
