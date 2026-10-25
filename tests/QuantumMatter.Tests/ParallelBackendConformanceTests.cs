using QuantumMatter.Core;
using QuantumMatter.Infrastructure;

namespace QuantumMatter.Tests;

public sealed class ParallelOneWorkerConformanceTests : StateVectorBackendConformanceTests
{
    protected override IStateVectorBackend CreateBackend() => new ParallelCpuStateVectorBackend(
        new ParallelCpuOptions { MaxDegreeOfParallelism = 1, MinimumParallelQubits = 1 });
}

public sealed class ParallelTwoWorkerConformanceTests : StateVectorBackendConformanceTests
{
    protected override IStateVectorBackend CreateBackend() => new ParallelCpuStateVectorBackend(
        new ParallelCpuOptions { MaxDegreeOfParallelism = 2, MinimumParallelQubits = 1 });
}

public sealed class ParallelThreeWorkerConformanceTests : StateVectorBackendConformanceTests
{
    protected override IStateVectorBackend CreateBackend() => new ParallelCpuStateVectorBackend(
        new ParallelCpuOptions { MaxDegreeOfParallelism = 3, MinimumParallelQubits = 1 });
}

public sealed class SimdOneWorkerConformanceTests : StateVectorBackendConformanceTests
{
    protected override IStateVectorBackend CreateBackend() => new ParallelCpuStateVectorBackend(
        new ParallelCpuOptions { MaxDegreeOfParallelism = 1, MinimumParallelQubits = 1, EnableSimd = true });
}

public sealed class ParallelSimdThreeWorkerConformanceTests : StateVectorBackendConformanceTests
{
    protected override IStateVectorBackend CreateBackend() => new ParallelCpuStateVectorBackend(
        new ParallelCpuOptions { MaxDegreeOfParallelism = 3, MinimumParallelQubits = 1, EnableSimd = true });
}
