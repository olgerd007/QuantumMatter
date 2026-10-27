# QuantumMatter development rules

## Scope and intent

Build a mathematically correct, maintainable quantum simulator from scratch in C#/.NET 9.
Use System.Numerics.Complex (double precision) and xUnit. Do not replace numerical
execution with an existing quantum computing library. Keep changes incremental and
prefer explicit code to speculative abstractions.

## Public repository privacy

Do not commit machine-identifying CPU/GPU models, detected processor counts,
physical RAM/VRAM capacities, device identifiers, driver/OS installation fingerprints,
or absolute host paths in reports, metadata, logs or documentation. Reporting tools
must omit these details by default. Keep local diagnostic captures only under the
ignored artifacts/ directory. Requested worker limits, algorithm memory costs,
target frameworks, precision and anonymized measurements may be recorded.
This privacy rule takes precedence over benchmark provenance guidance.

## Workflow

1. Read the affected code, tests, docs/architecture.md, and docs/mathematics.md.
2. State the goal, affected files/layers, mathematical assumptions, risks, and validation plan.
3. Name new branches `feature/<purpose>` with a short, lowercase, hyphen-separated purpose
   (for example, `feature/avx512-optimization`). Use feature branches for significant work.
   Never push directly to main.
4. Protect existing behavior with tests before risky refactoring. Implement the smallest complete change.
5. Run restore, build, the complete test suite, and formatting verification from the repository root.
6. Inspect the diff for correctness, boundary violations, numerical regressions, and unrelated edits.
7. Report what changed, architectural decisions, changed files, actual validation results,
   assumptions, and remaining concerns. Never claim an unexecuted command passed.

## Project boundaries

- src/QuantumMatter.Core: domain circuit model, operations, state ownership, measurement,
  and the domain IStateVectorBackend port. No infrastructure, logging, SDK, DI-container,
  HTTP, persistence, SIMD-intrinsic, or GPU dependencies.
- src/QuantumMatter.Application: simulation workflows, allocation policy, shot sampling,
  and results. Depends only on Core. Never instantiate a concrete backend here.
- src/QuantumMatter.Infrastructure: concrete CPU/backend kernels. Depends only on Core
  today; may implement future Application ports. Hardware-specific packages stay here
  or in a separately justified backend project.
- src/QuantumMatter.Demo: composition root; constructor-inject the selected backend.
- benchmarks/QuantumMatter.Benchmarks: standalone CPU/CUDA benchmark tooling; never
  reference it from the runtime libraries. Record settings and raw results for claims.
- tests/QuantumMatter.Tests: domain, workflow, numerical, and backend conformance tests,
  separated into focused files. Add projects only for a real boundary or tooling need.

Core is the Domain layer despite its historical name. No API or persistence project is
needed for a numerical library. Interfaces belong at architectural boundaries, not on
every class. Keep dependencies inward and avoid circular dependencies, service
locators, catch-all exception handling, and unrelated abstractions.

## Mathematical contracts

- Preserve the little-endian convention: qubit q is bit q, basis index is sum(b_q * 2^q),
  and printed kets are |q_(n-1)...q_0>. See docs/mathematics.md for matrices and phases.
- Apply gates in insertion order to a column state vector. Angles are finite radians.
  Rotations are exp(-i * theta * Pauli / 2). Relative and global phase are retained.
- Validate all public inputs before mutating state. Two-qubit operands must be distinct.
- Preserve normalization within documented floating-point tolerances. Never silently
  normalize after every gate or hide a broken kernel with broad tolerances.
- State storage is private. Snapshot returns a copy. Backends are trusted, synchronous
  extensions; do not retain host buffers or use them after Execute returns.
- Run samples terminal measurements without collapse. MeasureAll collapses the state.
- Partial Measure selects ordered qubits, retains the entire projected branch and its
  phases, and packs qubits[k] into outcome bit k. MarginalProbability and Pauli
  expectations divide by actual Born mass without mutating amplitudes. Pauli labels
  place q0 on the right. Keep host observations allocation-free per amplitude; do not
  substitute full collapse for a subset measurement. See docs/observations.md for
  cancellation and full-basis sampling conventions.
- Noise uses Core/Noise, INoiseChannelBackend and the separate Application/Noise
  trajectory workflow. Each noisy shot gets a fresh trajectory; never sample all
  shots from one selected noise branch or average trajectory amplitudes. Do not
  expose a single ket as a mixed ensemble's final state. Document Kraus/parameter
  conventions, conditional normalization, sampling uncertainty and array budgets.
  Do not introduce mid-circuit measurement, classical control, reset, or noise by
  silently changing these contracts.
- DynamicCircuit is a separate finite Core/Dynamic model. Each trajectory starts
  classical bits at zero; measurement overwrites mapped destinations, and If uses
  (register & mask) == value. Reset is a measurement/conditional-X trajectory, never
  a coherent merge of branches. Run re-executes the program for every shot and counts
  explicit final classical memory, without implicit readout or ensemble FinalState.
  Preserve QuantumCircuit and NoisyCircuit semantics; see docs/dynamic-circuits.md.
- NoisyDynamicCircuit composes private dynamic/noise segments. Carry classical memory
  across placements but start it at zero for every new public execution. Channels
  do not write classical bits; preserve explicit order and each history's conditional
  state. Reuse Kraus sampling and dynamic measurement/reset logic; never average
  amplitudes or implicitly add terminal readout. See docs/noisy-dynamic-circuits.md.
- Pauli Hamiltonians are immutable finite real sums with matching Pauli widths.
  Preserve ordered duplicate/zero terms and normalized energy-query semantics.
  Variational templates parameterize only uncontrolled Rx/Ry/Rz gates. For shared
  parameters, sum occurrence-wise shift contributions, never shift every occurrence
  together. Compose shifts without losing offsets at large angles; difference
  expectations termwise before weighting. Budget parameter/gradient/term buffers,
  preserve caller-owned backends, and use independent dense derivative oracles.
  See docs/variational.md.
- VQE uses Application/Variational and the existing evaluator. Preserve bounded
  Armijo acceptance, occurrence-wise circuit counts, immutable accepted iterates
  and conservative overlapping-array budgets. Stationarity is not a global-ground-
  state guarantee; distinguish limits/stalls and propagate cancellation/failures.
  Validate analytic energies and independent dense eigenpair residuals. See docs/vqe.md.
- Do not expose arbitrary matrices without finite-value and U-dagger U = I validation,
  a documented tolerance, dimension validation, and tests for rejected nonunitaries.

## Numerical tests and performance

- Test amplitudes and phases, not just probabilities. Include known matrix columns,
  interference, entanglement, gate inverses, norm preservation, and basis permutations.
- Compare new kernels to the independent dense-matrix oracle on small registers.
  Never reuse a production kernel in its own reference implementation.
- Use deterministic seeds and explicit absolute tolerances. Statistical checks must
  have generous justified bounds; avoid timing-dependent or nondeterministic tests.
- Test endianness, both control/target orderings, highest/lowest qubits, exact CDF
  boundaries, zero-probability plateaus, cancellation, and allocation limits.
- Never construct full 2^n by 2^n gate matrices in production. Operate in place on
  amplitude pairs or permutations; preserve O(2^n) work per supported gate.
- Avoid per-amplitude allocations and per-shot strings. Measure before claiming a
  performance improvement or adding parallelism, SIMD, pooling, or GPU kernels.
- SIMD/GPU work must preserve the same contract and run conformance tests before
  throughput comparisons. Explicitly report precision and host/device memory costs.
- NVIDIA validation requires `dotnet test QuantumMatter.sln -p:EnableCudaTests=true`
  on real CUDA hardware. Default CPU-only runs explicitly skip the hardware suite;
  never claim those runs validate GPU execution. Keep acceleration runtimes in
  Infrastructure; preserve explicit backend selection and IDisposable ownership.

## Resources, failures, and concurrency

- Enforce the configurable host-array budget before allocating. Account for the CDF
  during sampling. Never allocate a large register merely to test rejection.
- Propagate CancellationToken through workflows and kernels. Check it within long
  loops. CPU work stays synchronous; do not wrap it in Task.Run just to expose async.
- Use async I/O when I/O is introduced. Avoid Result, Wait, and hidden background work.
- Cancellation during Evolve can leave an existing state partially evolved. Document
  this; never return a partial state as a successful simulation result.
- Circuit builders and StateVector are mutable and not thread-safe. Do not mutate
  a circuit during execution or share a state across concurrent executions.
- Reject invalid arguments with standard argument exceptions. Surface cancellation.
  Do not swallow allocation, backend, or technical failures.
- Do not commit secrets or generated bin/obj/TestResults files. Use structured,
  nonsensitive logging if logging is later introduced.

## Build and style

Use the .NET 10 SDK selected by global.json and preserve net9.0 library assets.
The test project targets net9.0 and net10.0; install both runtimes and run both
complete suites, including hardware tests on each when validating CUDA. Nullable references and
warnings-as-errors apply to every project via Directory.Build.props. Put package
versions in Directory.Packages.props and explain any added dependency.

Use file-scoped namespaces, meaningful names, explicit dependencies, immutable
contracts where practical, and comments explaining constraints rather than restating
code. Avoid mass formatting of unrelated files.

Library packaging is shared through build/QuantumMatter.Library.props; only Core,
Application and Infrastructure are SDK packages. Keep XML docs complete and do not
suppress CS1591. PublicApiAnalyzers is build-only. Review PublicAPI inventory changes
explicitly; never automatically rewrite a shipped inventory to hide a break.
Run scripts/Test-Packages.ps1 for SDK changes. It must consume fresh local packages,
not project references or a stale global package cache. Test .NET 10 consumer
compatibility when claiming it, without silently changing the net9.0 library target.
Keep PackageValidationBaselineVersion at the reviewed published baseline and retain
the 1.x assembly binding version. Test-Packages.ps1 must also execute a consumer
compiled against that baseline with candidate DLLs, without rebuilding the app.
Keep samples/PackageConsumer limited to APIs available in the 1.0.0 baseline.
Use samples/ObservationConsumer for additive v1.2 APIs; Test-Packages.ps1 must run
it from fresh candidate packages separately from the unchanged baseline consumer.
Use samples/DynamicConsumer for v1.3 APIs, in its own sibling directory. Keep
existing public-feed validation modes usable for earlier releases.
Use samples/NoisyDynamicConsumer for additive v1.4 APIs, in another sibling directory;
public-feed checks opt in with -ValidateNoisyDynamicCircuits for compatible versions.
Use samples/VariationalConsumer for v1.5 APIs in a fifth sibling directory; public
checks opt in with -ValidateVariational while preserving older consumer sources.
Use the current docs/releases record for promotion; local package validation is not publication.
Use samples/VqeConsumer for v1.6 APIs in a sixth sibling directory; public-feed
checks opt in with -ValidateVqe while preserving all earlier consumer sources/modes.

```sh
dotnet restore QuantumMatter.sln
dotnet build QuantumMatter.sln --no-restore --verbosity minimal
dotnet test QuantumMatter.sln --no-restore --verbosity minimal
dotnet format QuantumMatter.sln --no-restore --verify-no-changes
dotnet run --project src/QuantumMatter.Demo --no-build
```
