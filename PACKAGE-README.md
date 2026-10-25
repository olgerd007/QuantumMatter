# QuantumMatter SDK

QuantumMatter is an MIT-licensed quantum simulator built from scratch in C# using
double-precision System.Numerics.Complex. Libraries target .NET 9; package-only
consumer checks also run on .NET 10. Starting with v1.1, the full xUnit suite runs
on both runtimes using the same net9.0 library assets.

| Package | Contents | Runtime dependencies |
| --- | --- | --- |
| QuantumMatter.Core | Circuits, pure states, channel descriptions, backend ports | .NET runtime |
| QuantumMatter.Application | Simulation, terminal sampling, algorithms, noisy trajectories | Core |
| QuantumMatter.Infrastructure | Scalar, parallel/SIMD, CUDA, reference noise kernels | Core, ILGPU 1.5.3 |

Install Application and Infrastructure at the same version; Core is transitive.

```sh
dotnet add package QuantumMatter.Application
dotnet add package QuantumMatter.Infrastructure
```

These commands select the latest stable version. For a local/prerelease candidate,
specify its version and feed. See the repository release record for publication
status and validation evidence.

```csharp
using QuantumMatter.Application;
using QuantumMatter.Core;
using QuantumMatter.Infrastructure;

var circuit = new QuantumCircuit(2).H(0).CNot(0, 1);
var simulator = new StateVectorSimulator(new CpuStateVectorBackend());
var result = simulator.Run(circuit, shots: 1000, seed: 42);
foreach (var (bits, count) in result.Counts)
    Console.WriteLine($"{bits}: {count}");
```

Qubit zero is the least significant bit; printed kets show the highest bit first.
Angles are radians, gates act on column vectors in insertion order, and phases are
retained. Memory budgets apply before allocating state/estimator arrays.

CPU execution needs no GPU driver. CUDA is explicitly selected with
CudaStateVectorBackend and requires a compatible NVIDIA driver/device; dispose the
backend after use. v1.0 CUDA support is validated on Windows x64; Linux CUDA is
experimental. SIMD is opt-in. Noise uses a separate trajectory API and CPU
channel kernels; it is not represented by one final ensemble ket.

Version 1.2 adds partial-register measurement, normalized marginals and
Pauli-string expectations on host states; see docs/observations.md. These preserve
the existing unitary circuit and terminal sampling contracts.

The package contains XML IntelliSense documentation, declared public API files,
LICENSE, CHANGELOG.md, third-party notices, and the docs directory. Start with
docs/api/1.2.0.md, docs/compatibility.md, docs/noise.md and docs/gpu-execution.md.
See the repository for validated hardware, raw benchmarks, known limitations and
the [release checklist and repository](https://github.com/olgerd007/QuantumMatter).
