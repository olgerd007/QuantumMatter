# Quantum cryptography, cryptanalysis and post-quantum experiments

Research date: **2026-10-26**. This is an experiment proposal grounded in the
current repository and primary sources. Numerical targets below are analytic
predictions, not measurements from newly implemented experiments.

The bounded baseline experiments are now implemented for **v1.7.0-rc.1**. See the
[runnable guide](cryptography.md) and [candidate record](releases/1.7.0.md).
The capability assessment and proposed sequence below retain the original research
snapshot; the runnable guide distinguishes implemented behavior from future work.

## Recommendation

Start with **BB84 and intercept-resend**, then add channel noise and an
entanglement-correlation experiment. These require only one or two simulated
qubits and exercise existing measurement and trajectory APIs. In parallel with
that research direction, plan a separate classical PQC benchmark harness. Use
Grover and small-instance Shor experiments to explain attacks and their costs.

| Track | Question | First experiment | Fit today |
| --- | --- | --- | --- |
| Quantum key distribution (QKD) | How does observation disturb transmitted states? | BB84 with optional intercept-resend | Existing gates, partial measurement and dynamic circuits |
| Entanglement-based QKD foundations | How do noise and measurement settings change correlations? | Bell pair and CHSH statistic | Existing gates, observations and noise trajectories |
| Quantum cryptanalysis | What does quantum search amplify, and what does an oracle cost? | Known-target Grover baseline, then a reversible toy verifier | Baseline exists; verifier needs implementation |
| Quantum cryptanalysis | How does period finding lead to factors? | Order finding for a=2 modulo 15 | QFT exists; controlled arithmetic and postprocessing are missing |
| Post-quantum security (PQC) | What are the interoperability and performance costs of replacement primitives? | ML-KEM and ML-DSA round trips and benchmarks | Separate classical tooling; no state-vector simulation required |
| Migration engineering | Which dependencies remain vulnerable after replacing one primitive? | Local authenticated key-establishment/transcript experiment | Later tooling using reviewed protocol specifications |

QKD uses quantum communication and classical postprocessing. PQC uses classical
algorithms intended to resist quantum adversaries. A simulated QKD transcript
does not create a physical quantum channel. QKD also needs an authenticated
classical channel; source authentication is not supplied by quantum transmission.
See [NSA's discussion of QKD limitations](https://www.nsa.gov/Cybersecurity/Post-Quantum-Cybersecurity-Resources/).

## Current capabilities and boundaries

The assessment uses [architecture](architecture.md), [mathematics](mathematics.md),
[algorithms](algorithms.md), [observations](observations.md),
[dynamic circuits](dynamic-circuits.md), [noise](noise.md) and
[noisy dynamic circuits](noisy-dynamic-circuits.md), together with their source
implementations and focused tests.

- `QuantumCircuit` has H, Pauli gates, rotations, controlled gates, Toffoli and
  SWAP. It remains unitary; measurement is not inserted into this model.
- `StateVector.Measure` projects an ordered subset while retaining the surviving
  branch and spectator coherence. It can model an intervening observation.
- `DynamicCircuit` records measurements and conditional actions.
  `NoisyDynamicCircuit` adds explicitly ordered channel placements. Their `Run`
  methods re-execute a history for each shot and count explicit classical memory.
- `NoiseChannel` supports bit flip, phase flip, depolarization and amplitude
  damping. Noise results describe conditional trajectories or probability
  estimates; there is no single final ket for a mixed ensemble.
- `QuantumFourierTransform` supports ordered register subsets and inverse QFT.
  `GroverSearch` supports one **supplied marked basis index**, rather than a
  caller-provided cryptographic verification oracle.
- There is no QKD protocol, reconciliation/privacy-amplification workflow,
  reversible modular arithmetic builder, Shor implementation, or PQC harness.

Keep protocol orchestration in Application or experiment tooling, circuit/state
rules in Core, and backend kernels in Infrastructure. Select and inject backends
at the composition root. A first BB84 demonstration needs no backend port, new
runtime package, or public API change.

Use a short-lived state for each signal or entangled pair. A million BB84 signals
do not require a million-qubit register. Classical transcript storage is a
separate, explicitly bounded resource. Dynamic classical registers have at most
30 bits, so they cannot hold an entire large key; process signals in the host
workflow and aggregate results.

## Experiment 1: BB84 and intercept-resend

The protocol foundation is
[Bennett and Brassard's original BB84 paper](https://arxiv.org/abs/2003.06557),
originally published in 1984; the linked scan was uploaded in 2020.

### Setup

For each independent signal, choose Alice's bit x and basis a, Bob's basis b,
and, if interception occurs, Eve's basis e. Bases Z/X are independently uniform
in the first experiment. Start one qubit in zero.

1. Prepare `X^x`, followed by H if a=X. The four states are zero, one, plus
   and minus. Gate order matters for the minus state.
2. Eve intercepts with configured probability f. If e=X, apply H, measure,
   and apply H again before forwarding the collapsed state. If e=Z, measure
   directly. This models measurement and resending its eigenstate.
3. Bob applies H if b=X and measures in Z.
4. Publicly compare bases and retain only a=b. Reveal a random test subset,
   remove it from candidate key material, and estimate its error rate.

Use host choices and fixed-setting circuits initially. For a dynamic version,
store Eve's and Bob's outcomes in distinct classical destinations; expose Eve's
record only to the experiment evaluator. Alice and Bob's decisions must not read
Eve's private basis/outcome or simulator amplitudes.

### Analytic targets

With ideal preparation/measurement and independent signals, our elementary
branch calculation gives:

```text
P(sifted) = 1/2
QBER = errors / compared sifted bits
QBER(f) = f * P(Eve chooses wrong basis) * P(Bob disagrees then)
        = f * (1/2) * (1/2) = f/4
P(at least one mismatch among m independent revealed sifted bits)
        = 1 - (1 - f/4)^m
```

No interception gives zero ideal errors; full interception gives expected QBER
25%. For f=1 and m=32 the mismatch-detection probability is approximately
99.98995%. This calculation assumes a mismatch causes detection and there are
no background errors. It is not a general detector operating curve or security
proof; finite fixed-size populations instead require sampling-without-replacement
analysis.

Sweep f over 0, 0.25, 0.5, 0.75 and 1. Use initially 100,000 signals per setting,
with a separate disclosed sample budget. Report signal, sifted, disclosed and
error counts, QBER by basis, and confidence intervals. An empty sifted/test set
has undefined QBER and must not be reported as zero.

### Acceptance criteria

Enumerate all bit/basis branches for deterministic tests. Matching bases recover
Alice's bit exactly without Eve; incompatible bases give equal Born probabilities.
Check the projected states and their phases, then check the ensemble error rate.
Include both zero/one input bits, all Eve settings, and a regression that Bob does
not accidentally measure in Eve's basis.

Seeded simulation randomness is appropriate for reproducibility. `System.Random`
and simulator-generated educational keys are unsuitable for operational key
generation. Actual cryptographic operations require a cryptographic provider's
randomness and key-management rules.

## Experiment 2: noise, parameter estimation and key-rate assumptions

Insert noise between forwarding and Bob's basis change, using
`NoisyDynamicCircuit` so every shot gets an independent conditional history.
First vary noise without Eve, then combine the two effects.

Deriving from the repository's channel definitions:

| Channel acting once | Expected Z-basis QBER | Expected X-basis QBER |
| --- | ---: | ---: |
| `BitFlip(p)` | p | 0 |
| `PhaseFlip(p)` | 0 | p |
| `Depolarizing(p)` | p/2 | p/2 |

Here depolarization means `(1-p) rho + p I/2`; p is the replacement probability,
not total nonidentity Pauli probability. With intercept fraction f followed by
this depolarizing channel, the same elementary calculation gives
`QBER = (1-p)*f/4 + p/2`. Treat amplitude damping separately because it introduces
bit-dependent bias. These models omit loss, detector dark counts, multiphoton
sources and implementation side channels.

Use the
[Shor–Preskill BB84 security proof](https://arxiv.org/abs/quant-ph/0003004)
to motivate the difference between observed errors and a secret-key claim.
Under ideal single-photon qubit assumptions, asymptotic parameter estimation and
ideal one-way reconciliation, an illustrative secret fraction per retained key
bit is `max(0, 1-h2(eZ)-h2(eX))`, where
`h2(x)=-x log2(x)-(1-x) log2(1-x)` with endpoint values zero. In the symmetric
case this becomes `max(0,1-2*h2(q))` and reaches zero near q=0.1100.

The phase-error bound must be justified by the protocol's random sampling and
security assumptions. Add reconciliation leakage, finite-size confidence bounds,
authentication cost and privacy amplification before reporting a final key
length. The approximate 11% point is not a universal abort threshold. Noise and
interception can yield the same QBER; QBER alone cannot identify the cause.

Initial output should call the plotted quantity an **asymptotic idealized bound**.
Do not label sifted bits a final secure key. A later postprocessing study can
compare reconciliation strategies and universal-hash privacy amplification, with
their leakage and failure probabilities explicitly accounted for.

## Experiment 3: entanglement correlations and CHSH

Use an entanglement experiment inspired by
[Ekert's 1991 proposal](https://journals.aps.org/prl/abstract/10.1103/PhysRevLett.67.661).
The setup below is a Bell/CHSH experiment, not a complete implementation of E91.

Prepare `H(0); CNot(0,1)` to obtain `(|00>+|11>)/sqrt(2)`. Label q0 Alice and q1
Bob. Define observables:

```text
A0 = Z                  B0 = (Z+X)/sqrt(2)
A1 = X                  B1 = (Z-X)/sqrt(2)
Eab = P(equal outcomes | a,b) - P(different outcomes | a,b)
S = E00 + E01 + E10 - E11
```

For an observable `cos(beta) Z + sin(beta) X`, apply `Ry(-beta)` before Z
measurement. Thus Alice uses beta=0 or pi/2 and Bob uses pi/4 or -pi/4.
With the repository's rotation convention, direct Bell-state algebra gives
`S=2*sqrt(2)`, compared with the local hidden-variable bound `|S|<=2`.
An independent one-sided replacement depolarizing channel gives
`S=2*sqrt(2)*(1-p)`; the violation disappears at `p=1-1/sqrt(2)`.

Compute each conditional correlation from its own setting counts. Use exact
Pauli expectations as a separate simulator check, preserving the convention
that the rightmost Pauli label acts on q0. Test rotation signs with analytic
probabilities, rather than adjusting signs to make S large.

This does not certify device-independent security: the simulated devices,
random settings and sampling assumptions are trusted, and no detection/locality
loopholes or finite-key security proof are modeled.

## Experiment 4: Grover search and a reversible toy verifier

[Grover's algorithm](https://arxiv.org/abs/quant-ph/9605043) gives a quadratic
query improvement for unstructured search. The current known-target builder is
an excellent amplitude-amplification baseline but already receives the answer.

Start with 2–6 input bits and vary iteration count through and beyond the first
peak. For one marked input among N candidates, compare amplitudes and success
probability with `theta=asin(1/sqrt(N))` and
`Psuccess(k)=sin((2*k+1)*theta)^2`. Check scratch returns to zero, including
after overshoot. Record physical qubits, gates per round and total gates.

Only then build a small reversible verifier for a deliberately weak toy cipher:
public plaintext/ciphertext pairs define `f(key)`, computed into a flag,
phase-kicked, and uncomputed. Exhaustively enumerate the classical keyspace to
establish the number M of matching keys and independently verify every oracle
basis column. More than one matching key requires M-solution amplification;
the current builder must not be silently treated as supporting that case.

Count cipher evaluation, comparison, uncomputation, ancillas and diffusion.
Report classical exhaustive-search time separately from simulated quantum
time. A real k-bit key-search oracle may require order `2^(k/2)` ideal quantum
queries, but each query has a reversible implementation cost. That query count
is not a wall-clock estimate or a universal rule for all symmetric-cryptography
security properties. A classical state-vector simulator does not gain the
quantum query speedup.

## Experiment 5: Shor order finding for a=2 modulo 15

[Shor's factoring/discrete-logarithm paper](https://arxiv.org/abs/quant-ph/9508027)
explains why factoring-based and discrete-logarithm-based systems face quantum
attacks. Start with genuine coherent modular arithmetic on a tiny instance.

Use eight counting qubits (local bits least to most significant) and four work
qubits initialized to integer one: 12 data qubits before any synthesis ancillas.
Put the counting register into uniform superposition. Counting bit j controls
work-register multiplication by `a^(2^j) mod N`; then apply inverse QFT only to
the counting register and measure that subset.

Every modular multiplier must be a permutation over the full work register:
map `x<N` to `a*x mod N` for coprime a,N, and fix `x>=N` as one valid extension.
Synthesize controlled permutations with existing gates. Do not inject a prepared
periodic state, write known factors into an oracle, or introduce an unchecked
arbitrary matrix as a shortcut. Instance-specific compiled arithmetic may be a
separate labeled demonstration, but cannot establish general factoring cost.

The independent arithmetic target is:

```text
2^x mod 15 cycles through 1, 2, 4, 8; order r=4.
Ideal counting outcomes for t=8: y in {0,64,128,192}, each with probability 1/4.
Continued fractions of y/256 yield candidate denominators.
Verify a^r mod N=1; handle reduced denominators, retries and trivial outcomes.
For verified r=4: gcd(2^(r/2)-1,15)=3 and gcd(2^(r/2)+1,15)=5.
```

Test controlled arithmetic on every small-register basis input and its inverse,
including unused states and spectator qubits. Check the full complex output
against an independent small reference and test postprocessing failures: y=0,
odd candidate order, nontrivial-order verification failure and trivial gcds.
Then consider a non-power-of-two order to expose finite-resolution peaks;
modulo 15 alone makes period extraction unusually convenient.

Real RSA/ECC resource estimation is a different task from state-vector execution.
If added, record logical qubits, Toffoli/T counts and depth, error-correction
assumptions, physical error rates, factory costs and target failure probability.
For an example of a conditional estimate, see
[Gidney's 2025 RSA-2048 resource study](https://arxiv.org/abs/2505.15917).
It is neither a measurement of this simulator nor a forecast of an attack date.

## Experiment 6: classical PQC compatibility and performance

### Standards snapshot

The following status was checked against NIST sources on the research date:

| Primitive | Purpose | Source/status |
| --- | --- | --- |
| ML-KEM | Shared-secret establishment | [FIPS 203](https://csrc.nist.gov/pubs/fips/203/final), final August 13, 2024 |
| ML-DSA | Digital signatures | [FIPS 204](https://csrc.nist.gov/pubs/fips/204/final), final August 13, 2024 |
| SLH-DSA | Stateless hash-based signatures | [FIPS 205](https://csrc.nist.gov/pubs/fips/205/final), final August 13, 2024 |
| HQC | Code-based KEM diversity | [Selected March 2025](https://csrc.nist.gov/News/2025/hqc-announced-as-a-4th-round-selection); NIST's [current project page](https://csrc.nist.gov/projects/post-quantum-cryptography) describes ongoing standardization |
| Falcon/FN-DSA | Additional signature option | NIST's [current project page](https://csrc.nist.gov/projects/post-quantum-cryptography) describes ongoing standardization |

FIPS 203 and 204 landing pages list potential-update errata; pin the specification,
errata and provider versions used in any compatibility report. Selection for
standardization is not publication of a final standard. These algorithms are
designed for quantum resistance under their assumptions, rather than proven
immune to every future attack.

A KEM establishes a shared secret; it is not itself bulk encryption or peer
authentication. Use [NIST SP 800-227](https://csrc.nist.gov/pubs/sp/800/227/final)
for KEM usage guidance. A later integration experiment needs an authenticated
protocol, a specified key schedule and symmetric authenticated encryption.

### Implementation route

[.NET 10 introduces `MLKem`, `MLDsa` and `SlhDsa`](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/libraries).
Availability depends on the native platform/provider, and each type exposes
`IsSupported`. [Microsoft's platform support guide](https://learn.microsoft.com/en-us/dotnet/standard/security/cross-platform-cryptography)
documents the dependency; do not infer support from SDK installation alone.
The [Microsoft PQC announcement](https://devblogs.microsoft.com/dotnet/post-quantum-cryptography-in-dotnet/)
also documents experimental status for `SlhDsa`; review current API annotations
and justify any diagnostic opt-in rather than suppressing warnings globally.

A proposed `experiments/QuantumMatter.CryptographyLab` net10.0 console tool is a
justified separate tooling boundary. It can run PQC provider experiments while
referencing net9.0 QuantumMatter assets for optional educational comparisons.
Runtime libraries must never reference this tool. Do not retarget shipped
libraries or implement production cryptography from scratch to run the lab.
No project or dependency is added by this research document.

Start with ML-KEM-512/768/1024 and ML-DSA-44/65/87. Add representative SLH-DSA
parameter sets when the provider/API status supports a clearly documented run.
For every supported configuration:

1. Check key generation, export/import and shared-secret/signature round trips.
   Use available official known-answer vectors or an independent conforming
   implementation; a same-provider round trip alone cannot prove conformance.
2. Corrupt messages and signatures and require failed verification. For ML-KEM,
   distinguish malformed-length rejection from valid-length ciphertext changes:
   implicit rejection can yield an unrelated secret rather than an exception.
   Check secret disagreement and downstream authenticated-decryption failure.
3. Measure key generation, encapsulation/decapsulation or sign/verify separately.
   Record serialized key, ciphertext and signature sizes as well as allocations,
   latency distributions, concurrency and message sizes. Document whether sizes
   refer to raw encodings or ASN.1/PEM containers.
4. Record exact runtime, OS, provider, CPU, build, warmup, repetitions and raw
   results. Distinguish cold-start and steady-state costs. Unsupported providers
   must be reported explicitly and must not become successful benchmark rows.

Compare classical baselines at stated security targets and distinguish different
operations. PQC throughput does not measure resistance to cryptanalysis. Toy
LWE/noise/reduction studies can explain assumptions, but tiny instances and
failure to find an attack establish no production security level.

For migration research, inventory key establishment and signatures separately,
plus data confidentiality lifetime, certificate/update chains, crypto-provider
support and downgrade behavior. Replacing key establishment alone leaves a
quantum-vulnerable authentication dependency unresolved. A hybrid study should
pin a reviewed protocol/combiner and test transcript binding and negotiation;
avoid inventing an XOR or concatenation scheme and calling it a secure protocol.

## Resource and validation plan

For q physical simulated qubits, amplitudes require `16*2^q` bytes. Unitary
terminal sampling adds an `8*2^q`-byte CDF. Dynamic/noisy-dynamic runs instead
re-execute trajectories and use their documented amplitude-only array policy.
Noise probability estimation uses `32*2^q` bytes for state/mean/variance arrays.
Circuit objects, transcripts, counts, snapshots and backend staging/device arrays
are additional. Check the configured budget before allocation.

| Example register | Amplitudes only | Plus terminal CDF |
| --- | ---: | ---: |
| 12 qubits | 64 KiB | 96 KiB |
| 20 qubits | 16 MiB | 24 MiB |
| 24 qubits | 256 MiB | 384 MiB |
| 30 qubits | 16 GiB | 24 GiB |

Count ancillas as physical simulated qubits. The existing Grover synthesis uses
`n+max(0,n-2)` physical qubits. Small QKD experiments are likely dominated by
workflow overhead; benchmark before choosing parallel CPU or CUDA. CUDA unitary
segments transfer at measurement/noise boundaries and noise kernels run on CPU.
GPU acceleration does not remove exponential state storage.

For future implementations:

- Use analytic probability/amplitude tests before statistical tests. Independent
  dense projectors/Kraus maps are appropriate only for small test references.
- Preserve little-endian indexing, phases and ordered subset packing. Check
  both operand orders and scratch cleanup for arithmetic/oracle circuits.
- Keep stochastic checks seeded, with predefined bounds. For a Bernoulli estimate
  on n independent compared bits, Hoeffding gives
  `P(|estimate-p|>=epsilon)<=2*exp(-2*n*epsilon^2)`; choose an explicit failure
  budget and account for multiple comparisons. This interval is not a finite-key
  security proof. Handle stratified/without-replacement designs separately.
- Record per-stage counts and uncertainty. Do not average trajectory amplitudes,
  reuse a single noise branch for all shots, or claim an ensemble final ket.
- Propagate cancellation and preflight state, transcript and generated-gate
  budgets. Never mutate a circuit during execution or share mutable states/RNGs
  concurrently. Preserve ownership of injected/disposable backends.
- Run restore, build, complete .NET 9/10 tests and formatting verification from
  the root. Run package checks for SDK/API changes. CUDA claims additionally
  require both runtime suites with `EnableCudaTests=true` on real hardware.

## Proposed implementation sequence

| Milestone | Deliverable | Completion evidence |
| --- | --- | --- |
| 1 | BB84 demonstration and focused tests, initially in Demo/tests | Exact branch oracle; no-Eve and 25%-QBER targets; bounded transcript counts |
| 2 | Noisy BB84 sweep and documented idealized key-rate plot | Basis-specific noise formulas; independent trajectory checks; uncertainty and assumptions |
| 3 | CHSH correlation demonstration | Correct setting signs; ideal 2*sqrt(2); one-sided-noise prediction |
| 4 | Standalone PQC provider harness | Explicit support matrix; round trips, negative tests and independent vectors; raw benchmark data |
| 5 | Reversible toy verifier with Grover | Every basis input verified; clean ancillas; costs include compute/uncompute |
| 6 | Small Shor order finding and classical postprocessing | Verified arithmetic permutations, period histogram, retry paths and recovered factors |

Suggested first implementation files are `src/QuantumMatter.Demo/Bb84Example.cs`,
`tests/QuantumMatter.Tests/Bb84ExampleTests.cs` and `docs/bb84.md`, with a focused
addition to the existing demo command dispatch. Keep types internal initially;
promote a reusable protocol workflow into Application only when a concrete
consumer justifies that SDK surface. Update README after the example exists.

This sequence prioritizes inexpensive, independently checkable experiments.
It does not depend on a faster kernel, a general quantum-network stack, a new
database/API layer, or large-scale cryptographic attack simulation.
