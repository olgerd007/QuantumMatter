# Repository milestones

These milestones define the proposed release sequence for QuantumMatter. They are
scope targets, with no delivery dates assigned. A version is ready to release only
when its acceptance criteria and the shared release checks below pass.

The v0.x validation paragraphs below are historical records of local milestone
work. Current cross-platform CI and publication status are in the v1.2 section
and [release record](releases/1.2.0.md).

| Version | Goal | Current implementation coverage |
| --- | --- | --- |
| v0.1 | Basic state-vector simulator | Functional criteria verified |
| v0.2 | Quantum circuit engine and measurement | Functional criteria verified |
| v0.3 | Complete quantum gates and validation | Functional criteria verified |
| v0.4 | Quantum algorithms | Functional criteria verified |
| v0.5 | Parallel CPU execution | Functional criteria verified |
| v0.6 | NVIDIA GPU acceleration | Functional criteria verified on NVIDIA hardware |
| v0.7 | Quantum noise simulation | Functional criteria verified |
| v1.0 | Stable quantum computing SDK | Released as v1.0.0; NuGet publication, platform CI and fresh public-feed consumers verified |
| v1.1 | Full .NET 9/10 validation and compatibility | Released as v1.1.0; runtime CI, NuGet publication and fresh CPU/CUDA consumers verified |
| v1.2 | Partial measurement and observables | Released as v1.2.0; platform CI, NuGet publication and fresh CPU/CUDA observation consumers verified |

Earlier milestones were delivered together in the v1.0.0 release rather than
separate v0.x tags. The current foundation includes scalar CPU, parallel CPU and CUDA backends,
17 gate kinds with controlled single-qubit forms, validated custom unitaries,
terminal sampling, full-register collapse, memory limits, cancellation and
stochastic noise trajectories with four single-qubit channels, protected by 664
xUnit cases with hardware tests enabled. Checks include algorithms, parallel/SIMD,
NVIDIA execution and independent noise-channel/density oracles. Release review and
applicable packaging/platform checks remain separate from local functional coverage.

The v0.3 implementation includes the [gate reference](gates.md), immutable custom
matrix validation, controlled CPU kernels, exhaustive four-qubit operand/basis
tests, and an inherited backend conformance suite. The v0.4 builders and examples
are documented in the [algorithm guide](algorithms.md). The v0.5 implementation
and raw measurements are documented in the [CPU execution guide](cpu-performance.md).
The v0.6 backend, hardware test requirements and measured results are documented in
the [GPU guide](gpu-execution.md). The v0.7 channels, trajectory representation,
sampling uncertainty and explicit placement are defined in the [noise guide](noise.md).
v1.0 is tagged and published as version 1.0.0. Three release packages include frozen
API inventories, XML documentation and isolated local/public-feed consumer checks.

Local v0.3 validation: restore, Debug/Release builds and all 117 tests, formatting
verification, and the Bell-state demo pass on Windows x64 with project-selected SDK.
Both builds report zero warnings and errors. Documentation links and
git diff --check also pass. The remote Windows/Linux CI matrix has not run.

## v0.1 — Basic state-vector simulator

Acceptance criteria:

- Initialize an n-qubit register to |0...0> using double-precision Complex values.
- Query amplitudes and probabilities without exposing mutable storage.
- Apply basic single-qubit gates using in-place scalar kernels.
- Document indexing, tensor order, gate order, and floating-point tolerances.
- Reject invalid register sizes and enforce a configurable allocation budget.
- Verify known amplitudes, normalization, and endianness with xUnit tests.

## v0.2 — Quantum circuit engine and measurement

Acceptance criteria:

- Build validated circuits and execute operations in insertion order.
- Support CNOT and SWAP with distinct operands in either index order.
- Implement Born-rule full-register measurement with collapse.
- Sample independent terminal shots without collapsing the returned final state.
- Support reproducible seeded sampling within the documented runtime contract.
- Propagate cancellation through execution and sampling.
- Verify Bell/GHZ states, interference, collapse, shot totals, zero-probability
  outcomes, and cumulative-distribution boundaries.

This milestone covers full-register measurement and terminal sampling. Mid-circuit
measurement, partial-register measurement, classical control, and reset require
separate scope and design decisions before implementation.

## v0.3 — Complete quantum gates and validation

Here, "complete" means the documented release gate set and extension contract.
It does not imply enumerating every possible unitary operation.

Acceptance criteria:

- Retain H, X, Y, Z, S, T, Rx, Ry, Rz, CNOT, and SWAP.
- Add identity, S/T adjoints, phase gates, controlled single-qubit gates and
  rotations, and Toffoli with explicit operand-order conventions.
- Support a custom single-qubit unitary and its controlled form, validating
  dimensions, finite entries, and unitarity before mutation.
- Document each gate matrix, adjoint, parameter domain, and validation tolerance.
- Extend the independent dense-matrix oracle and inverse/normalization tests to
  every supported gate, including invalid inputs and nonunitary matrices.
- Run a reusable backend conformance suite against the scalar reference.

All criteria above are implemented for the scalar backend. General multi-target
custom matrices need their own resource and indexing design.

## v0.4 — Quantum algorithms

Acceptance criteria:

- Provide documented Deutsch–Jozsa, quantum Fourier transform and inverse QFT,
  and Grover search circuit examples built from the simulator's own gates.
- Keep reusable circuit construction independent of concrete execution hardware.
- Verify small analytic cases, QFT/inverse recovery, Deutsch–Jozsa classifications,
  and Grover amplitude amplification against independent expected results.
- Include runnable examples explaining input encoding, qubit order, measurement,
  and the classical simulation cost.

Additional algorithms can follow once these examples validate the gate API.

The v0.4 implementation covers all criteria above: independent Application builders,
selectable runnable demos, analytic Fourier/Walsh/amplification tests, and explicit
scratch-qubit and classical simulation cost documentation.

Local v0.4 validation on Windows x64 with project-selected SDK: restore, Debug/Release
builds, all 152 tests in both configurations, formatting verification, and every
runnable example pass. Both builds report zero warnings and errors. The examples
classify constant/balanced functions, restore |101> after QFT/inverse QFT, and
produce Grover success probability 121/128. Remote CI and release review remain
pending; no version was tagged or published.

## v0.5 — Parallel CPU execution

Acceptance criteria:

- Introduce an explicitly selectable parallel CPU backend behind the existing
  unitary execution contract, retaining the scalar reference backend.
- Partition work so each amplitude or pair has a single writer per gate.
- Bound worker count and account for any additional buffers.
- Preserve cancellation and pass the shared numerical conformance suite at
  multiple worker counts and target strides.
- Add SIMD kernels where measurements justify them, with hardware detection
  and a portable fallback.
- Publish reproducible comparisons by register size, circuit depth, and gate mix,
  including hardware, precision, allocations, and parallelization thresholds.

No speedup is assumed for small registers. Backend selection must reflect measured
costs and retain a reliable scalar execution path.

Implemented: bounded range-based parallel execution, a per-gate barrier, scalar
fallback, opt-in SIMD Hadamard kernels, shared conformance at multiple worker
limits, deterministic worker-cancellation/failure tests, and quick/full benchmark
matrices with raw results and metadata. Default policy is at most four workers,
parallel from 16 qubits, and SIMD off; all are explicitly configurable.

Local v0.5 validation on Windows x64 with project-selected SDK: restore, Debug/Release
builds, all 498 tests in both configurations, hardware-intrinsic-disabled Release
tests, formatting verification, both parallel demo modes, documentation links,
and git diff --check pass. Both builds report zero warnings and errors. Quick
and full benchmarks completed with raw CSV/metadata retained. Remote CI and
release review remain pending; no version was tagged or published.

## v0.6 — NVIDIA GPU acceleration

Acceptance criteria:

- Implement an NVIDIA GPU backend with driver/device capability checks and
  explicit backend selection. Keep hardware dependencies in Infrastructure.
- Preserve the double-precision reference contract; any reduced-precision mode
  requires explicit opt-in and separate tolerances.
- Keep the state device-resident across circuit execution, transferring the
  initial and final state at the execution boundary.
- Enforce host/device/scratch memory limits and release resources on success,
  failure, and cancellation.
- Validate the full supported gate set against the CPU reference on actual
  NVIDIA hardware, with documented device and driver requirements.
- Report transfer-inclusive timings as well as kernel timings and define behavior
  for unavailable or unsupported hardware.

GPU acceptance requires hardware-backed tests; CPU-only CI cannot establish it.

Local v0.6 validation: restore, Debug/Release builds with zero warnings/errors,
582 passing tests in both configurations on a compatible CUDA device, Release tests
with CPU intrinsics disabled, default 504-test pass plus one explicit GPU-suite
skip, formatting verification, all CUDA algorithm demos, and unavailable-device
error checks. A 24-case benchmark records transfer-inclusive and CUDA event
sequence timings, allocations, memory costs and numerical error. See the GPU guide
for the exact environment, raw evidence and timing limits. Remote CI has not run.

## v0.7 — Quantum noise simulation

Acceptance criteria:

- Select and document a noisy-state representation, such as stochastic state-vector
  trajectories or density matrices, with explicit memory and precision costs.
- Introduce a separate noise/channel contract; preserve the unitary backend's
  normalization and evolution guarantees.
- Implement bit-flip, phase-flip, depolarizing, and amplitude-damping channels
  with validated parameters and documented placement within circuits.
- Support reproducible stochastic experiments with explicit seeds where applicable.
- Verify zero-noise equivalence, analytic one-qubit cases, probability/trace
  preservation, and agreement with small independent reference calculations.
- Document sampling uncertainty for trajectory estimates and provide examples.

Noise work must explicitly revisit the pure-state result model and execution
workflow before extending them to mixed-state behavior.

v0.7 uses stochastic normalized pure-state trajectories, not a production density
matrix. It adds NoisyCircuit, a separate channel port/reference CPU backend and
Application workflow/results. Run samples one measurement per fresh trajectory;
estimates average Born probabilities with Welford standard errors. There is no
ensemble FinalState ket. Scalar/parallel/CUDA unitary segments remain injectable;
noise itself runs on the CPU, with host round trips at CUDA segment boundaries.

Local validation: restore, zero-warning Debug/Release builds, 659 passing tests
with real CUDA hardware enabled, Release tests with CPU intrinsics disabled,
formatting verification and the seeded noise example. New tests check dense Kraus
branches/weighted density matrices, a mixed-channel density oracle, analytic
probabilities, Bell damping/coherence, seeds, error estimates, memory, cancellation
and hybrid CUDA execution. Remote Windows/Linux CI has not run.

CPU-compatible Debug/Release runs with CUDA hidden each pass 580 tests and
explicitly skip the hardware-suite marker. Noise demos also pass with scalar,
SIMD-selected and CUDA unitary execution. No new package dependency was introduced.

## v1.0 — Stable quantum computing SDK

Acceptance criteria:

- Review and stabilize public APIs, namespaces, backend contracts, and error
  behavior across the completed milestones.
- Publish installable NuGet packages with versioned API documentation, examples,
  release notes, licensing, and a compatibility/deprecation policy.
- Verify package consumption from a fresh sample project.
- Document supported frameworks, platforms, CPU/GPU capabilities, precision,
  resource limits, and reproducibility guarantees.
- Maintain automated numerical and integration checks across supported platforms,
  plus a documented hardware validation process and performance baselines.
- Resolve release-blocking correctness defects and document remaining limitations.

The prepared release implements centralized package metadata, MIT/third-party license
assets, full public XML documentation and compiler-enforced API inventories. SDK
package validation and an isolated, hash-checked package consumer cover scalar,
parallel/SIMD, algorithms, cancellation, budgets, noise and optional real CUDA.
The same net9.0 packages are consumed successfully by .NET 9 and .NET 10 locally.
The index-error contract correction is documented and tested before API promotion.

Local Debug/Release hardware-enabled suites pass 664 tests, with zero build warnings
or errors. The API negative check deliberately removes one declaration, verifies
RS0016 rejects it, and restores the inventory. The Windows/Linux CI matrix now
includes both consumer frameworks; a separate manual NVIDIA workflow requires a
maintained self-hosted runner. The Windows/Linux Debug/Release CI matrix and both
package consumer frameworks now pass remotely. NVIDIA validation is local hardware
evidence; no remote self-hosted NVIDIA run is claimed.

Stable 1.0.0 is released with a frozen 211-declaration API baseline and centralized
version 1.0.0. Framework lifecycle has been reviewed; CUDA support is Windows x64,
with Linux CUDA experimental. Platform CI and committed-source symbol validation
pass. [PR #1](https://github.com/olgerd007/QuantumMatter/pull/1) was merged after
successful CI; v1.0.0 was tagged and released. The release workflow successfully
published all three packages and symbols through OIDC. Fresh .NET 9/10 consumers
restored exclusively from nuget.org and passed CPU, algorithms, noise and real CUDA
checks, with tagged-source metadata verified. The [release record](releases/1.0.0.md)
contains the package/feed links and exact evidence. Milestone v1.0 is complete.

Publication is configured for nuget.org user olgerd007 via the release-triggered
publish-nuget.yml OIDC workflow, matching XBullet.EasyTesting. Its QuantumMatter
trust policy settings match the user-provided NuGet account screenshot. Successful
OIDC publication meets NuGet's permanent-activation condition. Package IDs are now
published under olgerd007; a reserved package-prefix claim is not made.

## v1.1 — Full .NET 9/10 validation and release hardening

Acceptance criteria:

- Build with the .NET 10 SDK and run the complete suite on actual .NET 9 and .NET 10
  runtimes under Windows/Linux Debug and Release.
- Keep net9.0 package assets, existing mathematical conventions, all 211 shipped
  API declarations and the 1.x assembly binding version compatible with 1.0.0.
- Validate each package against its published 1.0.0 baseline at pack time.
- Compile a consumer against published 1.0.0, inject candidate SDK DLLs and run it
  on both runtimes without recompiling the consumer or modifying its dependency manifest.
- Run CUDA conformance and package/binary consumers on both runtimes with real hardware.
- Verify that intentional compatibility breaks are rejected and record actual evidence.

Version 1.1.0 implements these checks. Local Debug/Release suites pass 666
cases per runtime with real CUDA. Fresh and baseline-compiled consumers pass on
both runtimes, including CUDA. All eight Windows/Linux Debug/Release runtime CI
jobs pass, including package/binary compatibility consumers on both platforms.
[PR #3](https://github.com/olgerd007/QuantumMatter/pull/3) and stable promotion
[PR #4](https://github.com/olgerd007/QuantumMatter/pull/4) are merged. The v1.1.0 tag
is released, and all three packages and symbols are published on nuget.org.
Fresh public-feed consumers pass CPU/CUDA checks on .NET 9/10 with the exact
tagged-source commit verified. Milestone v1.1 is complete. See
the [v1.1 validation record](releases/1.1.0.md).

## v1.2 — Partial measurement and observables

Acceptance criteria:

- Query normalized marginal probabilities for ordered, nonempty qubit selections.
- Projectively measure a subset while preserving spectator coherence and surviving phase.
- Evaluate validated Pauli-string expectations without sampling or full matrices.
- Specify qubit/output ordering, normalization, cancellation and constant-space behavior.
- Verify independent projectors and dense Pauli oracles, invalid inputs, seeds and all backends.
- Preserve all shipped APIs and validate packages and unchanged baseline consumers on .NET 9/10.

Version 1.2.0 is released after merging [PR #6](https://github.com/olgerd007/QuantumMatter/pull/6)
and stable promotion [PR #7](https://github.com/olgerd007/QuantumMatter/pull/7).
Both candidates passed all eight Windows/Linux Debug/Release runtime jobs.
The tagged-source publication workflow passes platform validation and OIDC uploads
for all three packages and symbols. Fresh nuget.org-only consumers pass both existing
and new observation checks on .NET 9/10 with real CUDA and the exact source commit
verified. Milestone v1.2 is complete. Evidence is in the [release record](releases/1.2.0.md).
See the [observation guide](observations.md). Dynamic circuits and classical
feedback require a later milestone; existing terminal sampling is unchanged.

## Shared release checks

- Pass restore, Debug/Release builds, the complete xUnit suite, and formatting
  verification. Run platform-specific and hardware checks where required.
- Preserve Clean Architecture boundaries and keep quantum execution implemented
  from scratch. Acceleration runtimes must not replace the simulator with an
  external quantum computing library.
- Update mathematical conventions, architecture, examples, and migration notes
  alongside changes to public behavior.
- Record actual validation evidence and known limitations in release notes.
- Review changes through a feature branch before tagging or publishing a release.

See [architecture](architecture.md), [mathematical conventions](mathematics.md),
and [development rules](../AGENTS.md) for the implementation contracts.
