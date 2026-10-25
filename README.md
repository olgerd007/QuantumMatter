# QuantumMatter

An open-source quantum circuit simulator built from scratch in C#/.NET 9 using
System.Numerics.Complex and xUnit. No external quantum simulation library is used.
Projects, assemblies, and namespaces use the QuantumMatter prefix.

## SDK v1.0

The SDK version is **1.0.0**. Three NuGet packages expose
Core, Application and Infrastructure with XML documentation and tracked public APIs.
The [v1.0.0 release](https://github.com/olgerd007/QuantumMatter/releases/tag/v1.0.0)
is published on GitHub and NuGet. Fresh .NET 9 and .NET 10 consumers pass using
only nuget.org, including CUDA execution and tagged-source metadata checks.
Release publication is configured for nuget.org account olgerd007 using the same
OIDC convention as XBullet.EasyTesting. The matching QuantumMatter trust policy is
created and has successfully authenticated the first package publication.

```sh
dotnet add package QuantumMatter.Application --version 1.0.0
dotnet add package QuantumMatter.Infrastructure --version 1.0.0
```

```powershell
./scripts/Test-Packages.ps1 -Configuration Release
./scripts/Test-Packages.ps1 -Configuration Release -SkipBuild -ConsumerFramework net10.0
./scripts/Test-PublishedPackages.ps1 -ConsumerFramework net10.0
```

PowerShell 7 and the .NET 9 SDK are required for package checks; the optional
net10.0 consumer check additionally needs a .NET 10 SDK. The scripts pack to
artifacts/packages and test a fresh package-only consumer with an isolated cache.
Library targets remain net9.0. See the [versioned API reference](docs/api/1.0.0.md),
[compatibility policy](docs/compatibility.md) and [release record](docs/releases/1.0.0.md).

## Build and run

Install a .NET 9 SDK in the 9.0.3xx feature band selected by global.json.

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

The logical range is 1–30 qubits. For the unitary simulator, the default 512 MiB
host-array budget permits up to 25 qubits for Simulate and 24 for Run; actual
available memory may be lower. The noise guide defines trajectory/estimator limits.
A 30-qubit state alone needs 16 GiB, and its sampling CDF needs another 8 GiB.
Counts, snapshots, runtime overhead, and concurrent simulations require additional
memory. Circuits and states are mutable and must not be modified concurrently.

Mid-circuit measurement, classical control, circuit reset instructions, correlated
noise, density-matrix execution, general multi-target custom unitaries and broader
SIMD kernels are future work. StateVector.Reset restores an owned host state.

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
- [Release verification and promotion](docs/releases/1.0.0.md)
- [Release notes and migration changes](CHANGELOG.md)
- [Agent and contributor development rules](AGENTS.md)

Core is the domain layer; Application owns simulation workflows; Infrastructure
implements the backend; Demo composes them. StateVectorSimulator and
SimulationResult now live in QuantumMatter.Application and require explicit backend
injection. See the audit document for migration details.

The test suite includes complex gate-column checks, independent dense-matrix
comparisons, exhaustive two-qubit truth tables, long-circuit inverse/norm tests,
and resource, validation, cancellation, and measurement regressions.
CI builds and tests Debug and Release on Windows and Linux and verifies formatting.
Use feature branches for significant changes and never push directly to main.
