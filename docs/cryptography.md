# v1.7 cryptography experiments

The [v1.8 development candidate](authenticated-pqc.md) retains these experiments
and adds an authenticated-session lab and strict CI policies in the same branch.
The behavior below describes the v1.7 baseline; discovery remains the default.

Version **1.7.0-rc.1** implements the bounded experiments proposed in the
[research guide](cryptography-research.md). These are runnable educational tools,
not an operational QKD system, production attack toolkit, or security certification.
See the [candidate validation record](releases/1.7.0.md).

## Run the quantum experiments

From the repository root, after restore/build:

```powershell
dotnet run --project src/QuantumMatter.Demo --no-build -- bb84
dotnet run --project src/QuantumMatter.Demo --no-build -- bb84-noise
dotnet run --project src/QuantumMatter.Demo --no-build -- chsh
dotnet run --project src/QuantumMatter.Demo --no-build -- grover-key
dotnet run --project src/QuantumMatter.Demo --no-build -- shor
dotnet run --project src/QuantumMatter.Demo --no-build -- cryptography
```

The last command runs all five quantum commands. The existing `all` command keeps
its earlier meaning. Backend/preset arguments retain their existing positions:

```powershell
dotnet run --project src/QuantumMatter.Demo --no-build -- shor parallel-simd
dotnet run --project src/QuantumMatter.Demo --no-build -- grover-key cuda
```

BB84 uses one qubit and CHSH uses two, so scalar execution is appropriate. CUDA
unitary segments transfer at each measurement/noise boundary; the channel kernels
remain on CPU. Larger presets do not improve these tiny experiments by themselves.

## BB84 behavior and statistics

Each run resets a single state and samples independent Alice bits, Alice/Bob
Z/X bases, interception decisions and Eve bases. Cached noisy dynamic programs
prepare `X^bit` followed by optional H, perform Eve's projective measurement and
forward its eigenstate, apply optional noise, and measure in Bob's basis.
Classical c0 holds Eve's private result; c1 holds Bob's. Sifting and error
estimation never consult c0 or state amplitudes.

Settings use 100,000 signals, seed 42 and independent 25% disclosure among sifted
bits. Only aggregate counts are retained: signals, sifted bits, disclosed bits,
disclosed errors, and retained candidate bits, separately for Z and X. Disclosed
bits are excluded from retained candidates. There is no stored transcript or
output key. Empty disclosed sets have undefined QBER, represented internally by
null. Internal runs accept 1–10,000,000 signals and finite probabilities in [0,1].

The noiseless experiment sweeps interception fraction f over 0, .25, .5, .75 and
1. The noisy command sweeps f over 0 and 1, and p over 0, .1, .3 and 1. With the
repository's replacement channel `(1-p)rho+p I/2`, the analytic target is
`QBER=(1-p)*f/4+p/2`. In particular, full intercept-resend without noise gives
25% QBER; no Eve/no noise gives zero ideal errors.

For n disclosed independent bits and estimated error q, the printed 99%
Hoeffding interval is `[max(0,q-epsilon), min(1,q+epsilon)]` with
`epsilon=sqrt(log(200)/(2*n))`. This is a conservative per-basis statistical
interval. Simultaneous coverage of both bases is at least 98% by a union bound;
a whole sweep needs an additional multiple-comparison allowance. It is not a
finite-key security proof. The printed `max(0,1-h2(eZ)-h2(eX))` uses point
estimates and is labeled an idealized asymptotic bound, not a final key length.

Authentication, reconciliation, finite-key phase-error bounds, privacy
amplification, loss and detector/source attacks remain outside this experiment.
Seeded `System.Random` is for reproducible simulation only.

## Bell/CHSH correlations

Prepare Phi+ with H(0), CNOT(0,1), then apply a single depolarizing placement on
Bob's q1. Alice settings are Z/X; Bob settings are `(Z+X)/sqrt(2)` and
`(Z-X)/sqrt(2)`. Measuring `cos(beta)Z+sin(beta)X` uses `Ry(-beta)` before Z
measurement. Independently re-execute 10,000 histories per setting, with seed 42.

The statistic is `S=E00+E01+E10-E11`, where E is equal-outcome frequency minus
different-outcome frequency. Compare with `2*sqrt(2)*(1-p)` and the local bound
`|S|<=2`. The estimated aggregate standard error is
`sqrt(sum_settings((1-E^2)/shots))`; it is an estimate, not a rigorous Bell-test
confidence bound. This lab trusts its simulated devices and sampling model and
does not implement a complete E91 or device-independent key protocol.

## Grover with a reversible toy cipher

The cipher is deliberately weak and classically invertible. On three-bit values,
first compute `v=plaintext XOR key`, then update `v2 XOR= v0 AND v1`, then rotate
the three bits right by one. Every plaintext/ciphertext pair identifies one key.
The demonstration generates a public challenge from plaintext 3 and key 5;
the search builder receives only the resulting plaintext/ciphertext pair.

Qubits 0–2 hold the key, 3–5 hold cipher work, and 6 is zero scratch. The oracle
computes the cipher, flips phase when all output bits match the public ciphertext,
and uncomputes the work. The diffuser also restores scratch and keeps its exact
global sign. Two Grover rounds give key 5 probability `121/128=0.9453125`.
The complete circuit has seven physical qubits and 83 gates. Internal iteration
counts are bounded to 0–64; this is not the supplied-marked-index Grover API.

Classical enumeration is used only to validate the challenge and report its
number of solutions. All compute, compare, uncompute and diffuser gates count
toward the circuit cost. Simulated execution offers no classical speedup.

## Small Shor order finding

The default uses N=15, a=2, eight counting qubits, four work qubits and two scratch
qubits: **14 physical qubits and 633 gates**. Initialize work to integer 1, apply
counting-controlled multiplication by `a^(2^j) mod N`, then inverse QFT on the
ordered low counting bits. Full-register terminal samples are aggregated by
those counting bits; this has the same counting marginal as subset readout.

Arithmetic builders accept odd moduli 3–15 and coprime multipliers in [1,N).
Counting width is bounded to 1–8. They discover finite permutation cycles and
synthesize endpoint transpositions along Gray paths using X, CNOT and Toffoli
gates. Work values >=N are fixed. Controlled-zero branches are identity, and
scratch returns to zero. No dense quantum matrix or prepared periodic state is
used. These small finite bounds also bound generated gates below 40,000.

For N=15/a=2, ideal peaks are 0, 64, 128 and 192. Continued fractions of y/256
produce candidate order denominators; bounded multiples recover reduced fractions.
Verify the order by integer modular exponentiation before trying nontrivial gcds.
Zero outcomes, odd orders and trivial gcds can fail legitimately; the result is
nullable and no factors are fabricated. With 512 seed-42 shots, the recorded run
recovers 3 and 5. Prime moduli are allowed as arithmetic demonstrations but need
not yield factors. Modulo 7/a=2 tests a non-power-of-two order against direct DFT.

Finite truth-table permutation synthesis and bounded multiple search are teaching
methods. They do not scale to cryptographic moduli or establish RSA/ECC attack costs.

## Classical PQC lab (.NET 10)

This separate nonpackable console tool uses platform cryptography, with no
reference from any runtime library and no new NuGet dependency:

```powershell
dotnet run --project experiments/QuantumMatter.CryptographyLab -c Release -- --repetitions 30 --warmup 5 --output artifacts/pqc.json
dotnet run --project experiments/QuantumMatter.CryptographyLab -c Release -- --repetitions 30 --warmup 5 --include-slh-dsa --output artifacts/pqc-with-slh.json
```

ML-KEM-512/768/1024 and ML-DSA-44/65/87 are attempted only when their .NET
`IsSupported` check passes. Unsupported primitives have explicit `unsupported`
rows and no measurements. The process can exit zero with unsupported rows, so
automation must inspect each row's status. No crypto implementation is emulated.
Malformed CLI arguments return 2; cancellation returns 130. Cancellation is
checked between provider operations, which themselves are synchronous.

SLH-DSA-SHA2-128f is opt-in. Its .NET 10 API is experimental; only the dedicated
helper file opts into diagnostic SYSLIB5006, with an explanatory comment. The
SDK-wide warnings-as-errors policy remains intact. The recorded Windows provider
reports SLH-DSA unsupported, so no SLH execution/conformance claim is made.

Before timing, supported ML-KEM/ML-DSA parameter sets check two pinned NIST ACVP
cases each: valid and implicit-rejection decapsulation, or valid/invalid signature
verification. All twelve cases are public synthetic fixtures, with source commit,
case IDs and original file hashes in the embedded JSON. See the
[vector provenance](../experiments/QuantumMatter.CryptographyLab/Vectors/README.md)
and [NIST notice](../licenses/NIST-ACVP.txt). Selected vectors do not constitute
full ACVP/CAVP/FIPS module certification or cross-provider interoperability.

Additional checks cover key export/import, shared-secret/signature round trips,
message/signature changes, valid-length ciphertext mutation, malformed KEM lengths,
and HKDF-SHA256/AES-256-GCM success/failure after KEM decapsulation. Implicit
rejection returns an unrelated secret rather than requiring an exception. The
AEAD composition is a local check, without peer authentication or negotiation;
it is not presented as a network protocol. Secret buffers and exported private
material are cleared after use. Reports contain sizes and timings, not keys.

Each operation records raw microsecond samples, managed allocations on the current
thread, and nearest-rank p50/p95 after configurable warmup. Key generation,
encapsulation/decapsulation and sign/verify are measured separately. ML-DSA uses
1024-byte messages; optional SLH records its different message size in its row.
Raw encodings are distinguished from PEM/ASN.1. Repetitions are bounded to 1–1000,
warmup to 0–100. Native allocations, cold provider initialization, network effects,
and timing side-channel resistance are not measured.

## Evidence and limits

Recorded scalar Release output and PQC measurements:

- [Quantum experiment output](benchmarks/cryptography-v1.7-2026-10-26.txt)
- [PQC raw report](benchmarks/pqc-v1.7-2026-10-26.json)
- [Run metadata](benchmarks/cryptography-v1.7-2026-10-26.metadata.json)

Independent tests enumerate BB84 projection branches with phase, verify CHSH
amplitudes, compare every toy oracle input and Grover overshoot with independent
formulas, and check every small modular permutation against integer arithmetic.
Full Shor amplitudes match an independent direct DFT, including entangled work
and zero scratch. Additional tests cover null statistics, budgets, cancellation,
invalid inputs, parallel/SIMD execution and opt-in real CUDA histories.

State/CDF budgets remain governed by existing simulator contracts. BB84/CHSH keep
one state and bounded circuit/statistics storage; no per-signal transcript or
per-shot strings are allocated by these workflows. Shor terminal sampling uses
the normal state/CDF preflight. Backend ownership stays with the caller.
