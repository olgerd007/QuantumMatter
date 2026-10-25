# Initial repository audit and implementation plan

Audited source: commit 292a985 on main, before the foundation changes.
Working branch: codex/state-vector-foundation.

## Baseline evidence

The repository contained Core, Demo, and one xUnit project, with no root solution,
AGENTS.md, shared build configuration, architecture documentation, or CI workflow.
The code targeted .NET 9 and used System.Numerics.Complex. All eight original tests
passed after NuGet restore. Gate matrices were consistent with little-endian indexing
and the negative-sign Pauli rotation convention. No external quantum library was used.

## Findings

| Finding | Consequence | Foundation change |
| --- | --- | --- |
| Negative control or first SWAP qubit bypassed validation | Negative C# shift counts were masked, producing unintended operations | Validate both operands by gate kind |
| Full-register measurement used unit mass and defaulted to the last basis state | Floating-point drift could select a zero-mass outcome | Scale by actual mass and fall back only to supported outcomes |
| No memory preflight | Valid register sizes could request up to 16 GiB unexpectedly | Configurable state/CDF host-array budget |
| No cancellation | Long gates and sampling could not be interrupted | Tokens through workflows, backends, and measurement |
| Simulation orchestration and CPU kernels shared Core | Hardware implementation could not be replaced cleanly | Separate Application and Infrastructure with a domain backend port |
| Eight mostly probability-based tests | Phase, ordering, high-bit, invalid-input, and sampling regressions were weakly protected | Matrix-column tests, dense oracle, truth tables, inverses, and execution tests |
| SDK and package policy were per-project or absent | Root build and repeatable validation were inconvenient | Solution, SDK selection, shared properties, centralized versions, CI |
| Counts were only nominally read-only | Consumers could cast the dictionary and mutate results | Read-only dictionary wrapper |

## Incremental implementation plan

1. Establish baseline execution and create a feature branch.
2. Retain Core as the domain project; introduce Application and Infrastructure.
   Keep the fluent circuit API and state queries; explicitly compose the CPU backend.
3. Correct input/resource handling and measurement, preserve existing gate semantics,
   and implement cancellable scalar kernels with no dense production matrices.
4. Add independent mathematical conformance tests and regression tests for each defect.
5. Establish repository rules, architecture/math documentation, and CI.
6. Run restore, Debug and Release builds/tests, formatting verification, and the demo.

No database, API, migrations, external service, or GPU package is needed for this
increment. SIMD and GPU implementation remain follow-up work behind the backend
boundary, with conformance and benchmarking requirements documented separately.

## Compatibility decisions

All project folders, project files, assemblies, and namespaces use the QuantumMatter
prefix. StateVectorSimulator and SimulationResult live in QuantumMatter.Application;
update consuming project references and using directives to the new names.
Construct the simulator with an IStateVectorBackend, for
example new StateVectorSimulator(new CpuStateVectorBackend()). Demo and tests
demonstrate migration. No parameterless compatibility constructor is kept because
it would require Application to depend on Infrastructure or retain duplicate kernels.

A default 512 MiB host-array budget intentionally replaces unlimited allocation.
Callers can opt into a larger budget. Gate names, qubit order, rotation signs,
terminal sampling, StateVector queries, and full-register measurement semantics remain.

## Completed local validation

On Windows x64 with project-selected SDK and .NET runtime 9:

- Restore succeeded for the complete solution.
- Debug and Release builds succeeded with zero warnings and zero errors.
- All 56 xUnit cases passed in both configurations (the original eight plus 48 new cases).
- Formatting verification and git diff --check passed.
- The Bell-state demo reported probabilities 0.5 at 00 and 11, zero elsewhere,
  and seeded counts 502 and 498 across 1,000 shots.

The Windows/Linux CI matrix is configured but has not run remotely. No throughput
benchmark or hardware acceleration claim is made. Changes remain local on the
feature branch; nothing was pushed.
