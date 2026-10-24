# QuantumSim — .NET 9 state-vector quantum circuit simulator

A learning-oriented, from-scratch quantum simulator implemented in C# with `System.Numerics.Complex`.

## Requirements
- .NET 9 SDK
- Internet access for NuGet packages when first running tests

## Run
```sh
dotnet run --project src/QuantumSim.Demo
```

## Test
```sh
dotnet test tests/QuantumSim.Tests
```

## Implemented
- State vector initialized to `|0...0>`
- Gates H, X, Y, Z, S, T, Rx, Ry, Rz, CNOT and SWAP
- Full-register state-vector measurement (with collapse) and terminal shot sampling
- Circuit validation and tests

## Conventions
- Little-endian qubit positions: qubit 0 maps to bit 0 (rightmost printed bit).
- Complex amplitudes use double precision.
- Operations are applied in insertion order.
- `Run` samples terminal measurements from an uncollapsed state. The circuit API does not yet support mid-circuit measurement, classical control, reset, or noise.
- `FinalState` is the uncollapsed final pure state; `MeasureAll` collapses an individual StateVector in place.
- Maximum configured qubit count is 30, but available RAM is the actual limiting factor. A 30-qubit state vector requires 16 GiB excluding overhead.

## Roadmap
1. Separate execution backend, memory budgets, cancellation, robust validation of matrix unitarity.
2. Efficient controlled-U and generalized multi-qubit gates.
3. Separate measurement operations, classical registers, reset, mid-circuit measurement and conditional execution.
4. SIMD and parallel CPU kernels, benchmarks.
5. GPU kernels; compare single/double precision.
6. Algorithms: Deutsch-Jozsa, QFT, Grover, VQE/QAOA and error/noise models.

## Validation
The provided source was generated in an environment without the .NET SDK, so compilation and xUnit execution could not be performed here. Run the commands above to validate on your machine.
