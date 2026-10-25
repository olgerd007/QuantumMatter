# Changelog

## 1.1.0-rc.1 — Unreleased

- Full xUnit coverage on .NET 9 and .NET 10 using the .NET 10 SDK; library assets
  remain net9.0 and the 211 shipped API declarations are unchanged.
- Explicit Windows/Linux Debug/Release runtime matrix and sequential dual-runtime
  NVIDIA validation; runtime guards reject unintended major-version roll-forward.
- Package validation against published 1.0.0, including parameter-name checks.
- Binary smoke checks execute an unchanged consumer compiled against published
  1.0.0 with candidate DLLs on each runtime, including optional CUDA.
- Public-feed checks accept an explicit package version to verify prior releases.

## 1.0.0 — 2026-10-25

First stable SDK, assembled from milestones v0.1–v0.7. See the
[release record](docs/releases/1.0.0.md) for validation and publication evidence.

- Three packages preserve the Core/Application/Infrastructure dependency direction.
- Validated circuits and custom unitaries support 17 gate kinds and controlled forms.
- Scalar, bounded parallel/SIMD and explicit double-precision NVIDIA CUDA execution.
- Deutsch–Jozsa, QFT/inverse QFT and single-solution Grover circuit builders.
- Terminal sampling, collapse, allocation budgets and cooperative cancellation.
- Explicit single-qubit noise channels and seeded pure-state trajectory estimates.
- Public API inventories, XML documentation, package validation and consumer checks.
- Frozen 1.0 public API baseline; future incompatible API changes fail the build.
- .NET 9 libraries with .NET 9/10 package consumer checks. .NET 9 reaches end of
  support on November 10, 2026; .NET 10 is the recommended consumer runtime.
- Windows x64 CUDA support; Linux CUDA remains experimental pending hardware validation.

### Migration from the initial checkout

- QuantumSim project/namespace names become QuantumMatter.
- StateVectorSimulator and SimulationResult live in QuantumMatter.Application;
  inject the desired backend explicitly.
- Invalid basis indices in StateVector.Amplitude/Probability and
  NoisyProbabilityResult.Estimate now throw ArgumentOutOfRangeException with
  parameter basisIndex instead of leaking array IndexOutOfRangeException.
- Existing QFT overloads, gate identifiers and operation defaults are retained.

### Known limits

Noise kernels are CPU only; hybrid CUDA trajectories transfer per unitary segment.
No mid-circuit measurement/classical feedback, correlated channels, density-matrix
backend, arbitrary multi-target unitaries or automatic hardware calibration.
Windows/Linux CPU CI passes; Linux CUDA remains experimental.
