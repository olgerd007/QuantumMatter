# SDK compatibility and support policy

## v1.0 framework/platform scope

The SDK targets net9.0 as requested by the project. The repository uses the SDK
selected by global.json. Framework lifecycle was reviewed for v1.0 on 2026-10-25:
Microsoft support for .NET 9 ends November 10, 2026. .NET 10 LTS consumers are
recommended for new applications; Microsoft supports that runtime through
November 14, 2028. See [Microsoft's support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).
Libraries retain net9.0 to preserve the requested target and run on .NET 10 through
normal package compatibility. This does not extend Microsoft's .NET 9 servicing.
Future 1.x work may add a net10.0 asset without dropping net9.0; removal of the
net9.0 asset requires a major version and migration guidance.

| Surface | Release target | Validation evidence |
| --- | --- | --- |
| Scalar/parallel CPU and reference noise | Windows x64 and Linux x64, .NET 9 | Local Windows suite and remote Windows/Linux Debug/Release CI pass |
| .NET 10 consumer of net9.0 packages | Windows/Linux x64 | Local Windows smoke including CUDA and remote Windows/Linux consumers pass; not the full suite retargeted to .NET 10 |
| SIMD | Portable Vector&lt;double&gt; Hadamard when hardware/layout permits | Local accelerated and disabled-intrinsics suites; scalar fallback |
| CUDA | Windows x64, compatible NVIDIA device/driver | Local compatible CUDA device, [device capability omitted], compatible driver; Linux CUDA is experimental and outside v1.0 support |
| Other OS/CPU architectures | Unverified | No release support claim |
| Native AOT, trimming, browser/WASM | Unverified | No release support claim; CUDA uses dynamic compilation |

GPU support is capability-based and explicit, not a promise covering every NVIDIA
model/driver. Discovery, precision, device residency and memory requirements are in
the [GPU guide](gpu-execution.md). CPU usage requires no NVIDIA driver. Infrastructure
restores ILGPU 1.5.3 even for CPU selection; Core/Application do not depend on ILGPU.

## Versioning

All three SDK packages share semantic version 1.0.0. Use matching package versions.
The release record distinguishes prepared artifacts from public publication.
The 1.x compatibility policy is:

- Patch releases fix compatible defects; minor releases add compatible capabilities.
- Breaking source/binary/API or mathematical-convention changes require a new major
  version and migration documentation.
- Existing public signatures, parameter names, enum values, defaults, nullable
  annotations and backend ownership contracts are retained within 1.x.
- Deprecations use Obsolete with a replacement and release notes. APIs remain through
  the current major and at least one documented minor transition before removal.
- Exception types documented in the API guide are supported; message strings,
  internal algorithms, CPU schedules and performance timings are not stable APIs.

PublicApiAnalyzers 3.3.4 is a pinned, build-only dependency compatible with the chosen
compiler. It tracks the three libraries' PublicAPI files and nullability; changes
require explicit inventory diffs. The reviewed 1.0.0 inventory is frozen in
PublicAPI.Shipped.txt as the release contract; this does not assert publication.
After 1.0.0 publication, configure PackageValidationBaselineVersion to 1.0.0 for
subsequent versions. SDK package
validation checks compatible assets at pack time; there is no previous stable
package baseline to compare for this first release. References:
[API analyzer](https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/PublicApiAnalyzers/PublicApiAnalyzers.Help.md),
[package validation](https://learn.microsoft.com/en-us/dotnet/fundamentals/package-validation/overview).

The two existing QFT Append overloads retain optional parameters via narrow,
documented RS0026 suppressions: a required subset parameter distinguishes them.
New ambiguous optional-parameter overloads should not be added.

## Numerical and stochastic compatibility

Double precision, little-endian indexing, column-vector order, finite radian angles,
Pauli rotation signs and global/relative phases are contractual. Published test
tolerances are evidence for those cases, not an arbitrary-depth error guarantee.
CPU/GPU instruction rounding can differ; bitwise equality is not promised.

Seeded output is reproducible for the same API, runtime, backend and circuit.
Cross-runtime/version/device random sequences and identical floating-point CDF
decisions are not guaranteed. Noise Run and EstimateProbabilities intentionally
consume different random draws. Noise errors are Monte Carlo estimates, not exact
mixed-state results or model/calibration guarantees. See [noise](noise.md).

Performance depends on circuit depth/stride, CPU, GPU, driver and runtime. Benchmark
reports identify the environment and include raw data. No automatic universal CPU/GPU
crossover is claimed. Array budgets are separate from runtime/circuit/count/device
overhead and do not guarantee available physical memory.

## Distribution and ownership

MIT applies to QuantumMatter. ILGPU and development dependencies retain their own
licenses/notices. Packages carry documentation and license files. Consumers own
injected backend lifetimes, especially IDisposable CUDA contexts. Core/Application
have no container, logging, network or persistence dependencies.

Release 1.0.0 packages and symbols are published on nuget.org under user
olgerd007, using the same OIDC Trusted Publishing approach as
XBullet.EasyTesting. The user-provided screenshot confirms QuantumMatter's own
repository/workflow policy and QuantumMatter.* scope. Its first OIDC
publish has succeeded. Fresh .NET 9/10 public-feed consumers pass, including CUDA
and tagged-source metadata checks. Evidence is recorded in the release record.
The policy scope does not itself reserve the QuantumMatter prefix; no reserved-prefix
claim is made. Changes continue through feature branches and reviewed release tags.
