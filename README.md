# QuantumMatter

An open-source quantum circuit simulator built from scratch in C#/.NET 9 using
System.Numerics.Complex and xUnit. No external quantum simulation library is used.
Projects, assemblies, and namespaces use the QuantumMatter prefix.

## SDK v1.6

Version **1.6.0** adds deterministic pure-state VQE with bounded backtracking,
explicit termination reasons, immutable results and circuit-execution budgets.
See the [VQE guide](docs/vqe.md) and [validation record](docs/releases/1.6.0.md).
See the [release notes](docs/releases/1.6.0-notes.md) and [API additions](docs/api/1.6.0.md).
Stable publication is being prepared; v1.5.0 remains available.

## SDK v1.5

Version **1.5.0** adds Pauli Hamiltonians, parameterized rotations and
occurrence-wise energy gradients. See the [variational guide](docs/variational.md)
and [validation record](docs/releases/1.5.0.md).
The 28 additive declarations preserve all 255 earlier APIs; library assets remain
net9.0 for .NET 9/10 consumers. See the [release notes](docs/releases/1.5.0-notes.md)
and [API additions](docs/api/1.5.0.md).

The [v1.5.0 release](https://github.com/olgerd007/QuantumMatter/releases/tag/v1.5.0)
is published on GitHub and NuGet with matching symbols. All five fresh public-feed
consumers pass on .NET 9/10, including CUDA, with exact tagged-source metadata verified:
[Core](https://www.nuget.org/packages/QuantumMatter.Core/1.5.0),
[Application](https://www.nuget.org/packages/QuantumMatter.Application/1.5.0) and
[Infrastructure](https://www.nuget.org/packages/QuantumMatter.Infrastructure/1.5.0).

```sh
dotnet add package QuantumMatter.Application --version 1.5.0
dotnet add package QuantumMatter.Infrastructure --version 1.5.0
```

## SDK v1.4

Version **1.4.0** adds explicit noise channels to dynamic circuits with measurement,
classical feedback and reset. It preserves existing 1.x APIs and retains net9.0
library assets for .NET 9/10 consumers. See the [noisy dynamic guide](docs/noisy-dynamic-circuits.md),
[API additions](docs/api/1.4.0.md) and [validation record](docs/releases/1.4.0.md).

The [v1.4.0 release](https://github.com/olgerd007/QuantumMatter/releases/tag/v1.4.0)
is published on GitHub and NuGet with matching symbols. Fresh public-feed baseline,
observation, dynamic and noisy dynamic consumers pass on .NET 9/10, including CUDA,
with exact tagged-source metadata verified:
[Core](https://www.nuget.org/packages/QuantumMatter.Core/1.4.0),
[Application](https://www.nuget.org/packages/QuantumMatter.Application/1.4.0) and
[Infrastructure](https://www.nuget.org/packages/QuantumMatter.Infrastructure/1.4.0).

The [v1.0.0](https://github.com/olgerd007/QuantumMatter/releases/tag/v1.0.0),
[v1.1.0](https://github.com/olgerd007/QuantumMatter/releases/tag/v1.1.0),
[v1.2.0](https://github.com/olgerd007/QuantumMatter/releases/tag/v1.2.0) and
[v1.3.0](https://github.com/olgerd007/QuantumMatter/releases/tag/v1.3.0) releases
remain available. Publication uses nuget.org account olgerd007 and the
QuantumMatter-specific OIDC Trusted Publishing policy.

```powershell
./scripts/Test-Packages.ps1 -Configuration Release
./scripts/Test-Packages.ps1 -Configuration Release -SkipBuild -ConsumerFramework net10.0
./scripts/Test-PublishedPackages.ps1 -PackageVersion 1.5.0 -ValidateObservations -ValidateDynamicCircuits -ValidateNoisyDynamicCircuits -ValidateVariational
./scripts/Test-PublishedPackages.ps1 -PackageVersion 1.5.0 -ConsumerFramework net10.0 -ValidateObservations -ValidateDynamicCircuits -ValidateNoisyDynamicCircuits -ValidateVariational
```

PowerShell 7, the .NET 10 SDK and .NET 9/10 runtimes are required for development checks.
The scripts pack to
artifacts/packages and test a fresh package-only consumer with an isolated cache.
Library targets remain net9.0. Packages validate against published 1.0.0 and run
unchanged consumers compiled against that baseline. See the
[versioned API reference](docs/api/1.5.0.md), [compatibility policy](docs/compatibility.md)
and [release record](docs/releases/1.5.0.md).

## Build and run

Install the .NET 10 SDK selected by global.json and both .NET 9/10 runtimes.

```sh
dotnet restore QuantumMatter.sln
dotnet build QuantumMatter.sln --no-restore --verbosity minimal
dotnet test QuantumMatter.sln --no-restore --verbosity minimal
dotnet format QuantumMatter.sln --no-restore --verify-no-changes
dotnet run --project src/QuantumMatter.Demo --no-build
```

Run all v0.4 algorithm examples:

```sh
dotnet run --project src/QuantumMatter.Demo -- all
```

Individual examples: deutsch-jozsa, qft, and grover. See the
[algorithm guide](docs/algorithms.md) for circuit builders, register layout, and costs.

Select parallel CPU execution, or opt into the measured SIMD Hadamard path:

```sh
dotnet run --project src/QuantumMatter.Demo -- all parallel
dotnet run --project src/QuantumMatter.Demo -- all parallel-simd
```

See [CPU options and measured performance](docs/cpu-performance.md) for thresholds,
worker limits, hardware fallback, and reproducible benchmark commands.

Select NVIDIA CUDA execution explicitly (requires a compatible GPU and driver):

```sh
dotnet run --project src/QuantumMatter.Demo -- all cuda
dotnet test QuantumMatter.sln -p:EnableCudaTests=true
```

The hardware test mode fails if CUDA is unavailable. Default test runs include an
explicit skipped GPU-suite marker. See the [GPU guide](docs/gpu-execution.md) for
device discovery, disposal, memory budgets, requirements, and actual benchmarks.

Run stochastic noise trajectories with seeded sampling and probability estimates:

```sh
dotnet run --project src/QuantumMatter.Demo -- noise
```

See the [noise guide](docs/noise.md) for bit/phase flips, depolarization, amplitude
damping, explicit placement, ensemble results and statistical uncertainty.

## Example

```csharp
using QuantumMatter.Application;
using QuantumMatter.Core;
using QuantumMatter.Infrastructure;

var circuit = new QuantumCircuit(2).H(0).CNot(control: 0, target: 1);
var simulator = new StateVectorSimulator(
    new CpuStateVectorBackend(),
    new SimulationOptions { MemoryBudgetBytes = 512L * 1024 * 1024 });

var result = simulator.Run(circuit, shots: 1000, seed: 42);
foreach (var (bits, count) in result.Counts)
{
    Console.WriteLine($"|{bits}> = {count}");
}
```

## Supported behavior

- Pure states initialized to |0...0>, double-precision amplitudes.
- H, X, Y, Z, S, T, Rx, Ry, Rz, CNOT, and SWAP.
- Identity, S/T adjoints, phase gates, controlled single-qubit gates/rotations,
  Toffoli, and validated custom 2x2 unitaries. See the [gate reference](docs/gates.md).
- Portable scalar CPU kernels, operating in place without dense gate matrices.
- Selectable bounded parallel CPU execution and opt-in SIMD Hadamard updates,
  with scalar fallback and configurable register-size thresholds.
- NVIDIA CUDA kernels for the full gate set in double precision, with one upload
  and download per circuit, explicit resource limits, disposal, and event profiling.
- Little-endian qubits: qubit 0 is the rightmost printed bit.
- Full-register measurement with collapse; terminal shots leave FinalState uncollapsed.
- Configurable host-array memory budgets and cooperative cancellation.
- A whole-circuit backend boundary shared by scalar CPU, parallel CPU, and CUDA.
- Deutsch–Jozsa, QFT/inverse QFT (including ordered subsets), and single-solution
  Grover circuit builders, independent of the selected backend.
- Stochastic noise trajectories with four single-qubit channels, fresh-trajectory
  shots, probability estimates and standard errors through a separate noise API.
- Ordered partial-register measurement, normalized marginal probabilities and
  Pauli-string expectation values. See the [observation guide](docs/observations.md).
- Finite dynamic circuits with mid-circuit measurement, classical mask/value
  conditions and qubit reset. Fresh trajectories count explicit classical memory;
  see the [dynamic guide](docs/dynamic-circuits.md).
- Explicit noise channels combined with dynamic instructions through a separate
  [noisy dynamic workflow](docs/noisy-dynamic-circuits.md), preserving classical history.

The logical range is 1–30 qubits. For the unitary simulator, the default 512 MiB
host-array budget permits up to 25 qubits for Simulate and 24 for Run; actual
available memory may be lower. The noise guide defines trajectory/estimator limits.
A 30-qubit state alone needs 16 GiB, and its sampling CDF needs another 8 GiB.
Counts, snapshots, runtime overhead, and concurrent simulations require additional
memory. Circuits and states are mutable and must not be modified concurrently.

Correlated noise, density-matrix execution, general multi-target custom unitaries,
broader SIMD kernels and dynamic loops are future work. StateVector.Reset restores
an owned host state; DynamicCircuit.Reset is a
stochastic single-qubit instruction.

## Repository milestones

| Version | Goal |
| --- | --- |
| v0.1 | Basic state-vector simulator |
| v0.2 | Quantum circuit engine and measurement |
| v0.3 | Complete quantum gates and validation |
| v0.4 | Quantum algorithms |
| v0.5 | Parallel CPU execution |
| v0.6 | NVIDIA GPU acceleration |
| v0.7 | Quantum noise simulation |
| v1.0 | Stable quantum computing SDK |

See the [roadmap](docs/roadmap.md) for acceptance criteria and current implementation
coverage. These are proposed milestones; implementation coverage does not indicate
a published release.

## Design and contribution

- [Initial audit and implementation plan](docs/audit.md)
- [Architecture, memory policy, and acceleration design](docs/architecture.md)
- [Mathematical conventions and numerical tolerances](docs/mathematics.md)
- [v0.3 gates, custom matrices, and backend conformance](docs/gates.md)
- [v0.4 algorithms and runnable examples](docs/algorithms.md)
- [v0.5 parallel CPU execution and benchmark results](docs/cpu-performance.md)
- [v0.6 NVIDIA execution and benchmark results](docs/gpu-execution.md)
- [v0.7 noise channels, trajectories and uncertainty](docs/noise.md)
- [v1.0 API reference and contracts](docs/api/1.0.0.md)
- [Compatibility, framework and deprecation policy](docs/compatibility.md)
- [v1.1 release verification and promotion](docs/releases/1.1.0.md)
- [v1.0 release record](docs/releases/1.0.0.md)
- [Release notes and migration changes](CHANGELOG.md)
- [Agent and contributor development rules](AGENTS.md)

Core is the domain layer; Application owns simulation workflows; Infrastructure
implements the backend; Demo composes them. StateVectorSimulator and
SimulationResult now live in QuantumMatter.Application and require explicit backend
injection. See the audit document for migration details.

The test suite includes complex gate-column checks, independent dense-matrix
comparisons, exhaustive two-qubit truth tables, long-circuit inverse/norm tests,
and resource, validation, cancellation, and measurement regressions.
CI builds and tests Debug and Release on Windows and Linux under both runtimes,
verifies formatting and validates source/binary compatibility with published 1.0.0.
Use feature branches for significant changes and never push directly to main.
