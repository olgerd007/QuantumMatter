using System.Numerics;
using QuantumMatter.Application;
using QuantumMatter.Core;
using QuantumMatter.Demo.Cryptography;
using QuantumMatter.Infrastructure;
using Xunit;

namespace QuantumMatter.Tests;

public abstract class CryptographyBackendConformanceTests
{
    protected abstract IStateVectorBackend CreateBackend();

    [Fact]
    public void Synthesized_oracle_and_modular_arithmetic_preserve_scalar_complex_amplitudes()
    {
        IStateVectorBackend backend = CreateBackend();
        foreach (QuantumCircuit circuit in new[] { ToyKeySearch.CreateSearch(3, 3), ShorExperiment.CreateCircuit(7, 2, 4) })
        {
            StateVector expected = new StateVectorSimulator(new CpuStateVectorBackend()).Simulate(circuit);
            StateVector actual = new StateVectorSimulator(backend).Simulate(circuit);
            for (int index = 0; index < actual.Length; index++)
            {
                Assert.InRange(Complex.Abs(expected.Amplitude(index) - actual.Amplitude(index)), 0, 3e-12);
            }
        }
    }

    [Fact]
    public void Interleaved_measurement_noise_and_basis_changes_preserve_conditional_histories()
    {
        IStateVectorBackend backend = CreateBackend();
        for (int seed = 0; seed < 16; seed++)
        {
            var circuit = Bb84Experiment.CreateSignal(seed & 1, (seed & 2) != 0, (seed & 4) != 0,
                (seed & 8) != 0, 0.2);
            var expected = new StateVector(1);
            var actual = new StateVector(1);
            int expectedMemory = expected.EvolveNoisyDynamic(circuit, new CpuStateVectorBackend(),
                new CpuNoiseChannelBackend(), new Random(seed + 731));
            int actualMemory = actual.EvolveNoisyDynamic(circuit, backend, new CpuNoiseChannelBackend(), new Random(seed + 731));
            Assert.Equal(expectedMemory, actualMemory);
            for (int index = 0; index < actual.Length; index++)
            {
                Assert.InRange(Complex.Abs(expected.Amplitude(index) - actual.Amplitude(index)), 0, 3e-12);
            }
        }
    }
}

public sealed class ParallelCryptographyConformanceTests : CryptographyBackendConformanceTests
{
    protected override IStateVectorBackend CreateBackend() => new ParallelCpuStateVectorBackend(
        new ParallelCpuOptions { MaxDegreeOfParallelism = 3, MinimumParallelQubits = 1, EnableSimd = true });
}

#if CUDA_TESTS
public sealed class CudaCryptographyConformanceTests(CudaBackendFixture fixture) :
    CryptographyBackendConformanceTests, IClassFixture<CudaBackendFixture>
{
    protected override IStateVectorBackend CreateBackend() => fixture.Backend;
}
#endif
