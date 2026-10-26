# QuantumMatter SDK

QuantumMatter is an MIT-licensed quantum simulator built from scratch in C# using
double-precision System.Numerics.Complex. Libraries target .NET 9; package-only
consumer checks also run on .NET 10. Starting with v1.1, the full xUnit suite runs
on both runtimes using the same net9.0 library assets.

The v1.7 candidate adds repository-only educational cryptography experiments and
a separate .NET 10 PQC provider lab. No cryptography API is added to these SDK
packages. See [the experiment guide](docs/cryptography.md) and
[candidate record](docs/releases/1.7.0.md); local validation is not publication.

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

ParallelCpuOptions.EnableAvx512 and EnableGateBlocking are optional CPU tuning
controls, disabled by default. AVX-512 requires EnableSimd and falls back on
unsupported hardware; blocking reuses compatible gate tiles with bounded workspace.
See the [measured AVX-512/CUDA comparison](docs/avx512-cuda-performance.md) before
choosing a worker count or assuming that GPU execution is faster.

Version 1.4 adds a separate NoisyDynamicCircuit and runner combining those
channels with measurement, classical feedback and reset. See
[the noisy dynamic guide](docs/noisy-dynamic-circuits.md) and
[release validation](docs/releases/1.4.0.md) and [API additions](docs/api/1.4.0.md).
Existing circuit contracts are retained.

Version 1.5 adds Pauli Hamiltonians, parameterized Rx/Ry/Rz templates and
pure-state energies/gradients. Shared parameters use occurrence-wise shifts;
reductions remain on the host after unitary backend execution. See the
[variational guide](docs/variational.md), [API additions](docs/api/1.5.0.md) and
[release validation](docs/releases/1.5.0.md).

Version 1.2 adds partial-register measurement, normalized marginals and
Pauli-string expectations on host states; see docs/observations.md. These preserve
the existing unitary circuit and terminal sampling contracts.

Version 1.3 adds a separate dynamic-circuit workflow with mid-circuit
measurements, classical conditions and reset; see docs/dynamic-circuits.md.

The package contains XML IntelliSense documentation, declared public API files,
LICENSE, CHANGELOG.md, third-party notices, and the docs directory. Start with
docs/api/1.3.0.md, docs/compatibility.md, docs/noise.md and docs/gpu-execution.md.
See the repository for anonymized validation evidence, raw benchmarks, known limitations and
the [release checklist and repository](https://github.com/olgerd007/QuantumMatter).
