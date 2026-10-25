using System.Reflection;
using System.Runtime.Versioning;
using QuantumMatter.Application;
using QuantumMatter.Application.Noise;
using QuantumMatter.Core;
using QuantumMatter.Core.Noise;
using QuantumMatter.Infrastructure;
using Xunit;

namespace QuantumMatter.Tests;

public sealed class SdkContractTests
{
    [Fact]
    public void Tests_execute_on_the_declared_runtime_without_major_version_roll_forward()
    {
        // VSTest's host reports its own TFM; the test assembly declares the runtime under test.
        var target = new FrameworkName(typeof(SdkContractTests).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName);
        Assert.Equal(target.Version.Major, Environment.Version.Major);
    }

    [Fact]
    public void Both_runtime_suites_use_the_net9_library_assets()
    {
        foreach (Assembly assembly in new[] { typeof(StateVector).Assembly, typeof(StateVectorSimulator).Assembly, typeof(CpuStateVectorBackend).Assembly })
        {
            Assert.Equal(".NETCoreApp,Version=v9.0", assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void Public_basis_index_access_reports_argument_errors_without_mutating_state(int index)
    {
        var state = new StateVector(1);
        ArgumentOutOfRangeException amplitude = Assert.Throws<ArgumentOutOfRangeException>(() => state.Amplitude(index));
        ArgumentOutOfRangeException probability = Assert.Throws<ArgumentOutOfRangeException>(() => state.Probability(index));
        var simulator = new NoisyStateVectorSimulator(new CpuStateVectorBackend(), new CpuNoiseChannelBackend());
        NoisyProbabilityResult estimate = simulator.EstimateProbabilities(new NoisyCircuit(1), 2);
        ArgumentOutOfRangeException result = Assert.Throws<ArgumentOutOfRangeException>(() => estimate.Estimate(index));
        Assert.All(new[] { amplitude, probability, result }, error => Assert.Equal("basisIndex", error.ParamName));
        Assert.Equal(1, state.Probability(0));
    }

    [Fact]
    public void Domain_and_application_assemblies_keep_hardware_dependencies_out()
    {
        var coreReferences = typeof(StateVector).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(coreReferences, reference => reference.Name!.StartsWith("QuantumMatter.", StringComparison.Ordinal));
        Assert.DoesNotContain(coreReferences, reference => reference.Name == "ILGPU");
        var applicationReferences = typeof(StateVectorSimulator).Assembly.GetReferencedAssemblies();
        Assert.Equal(new[] { "QuantumMatter.Core" }, applicationReferences
            .Where(reference => reference.Name!.StartsWith("QuantumMatter.", StringComparison.Ordinal)).Select(reference => reference.Name).ToArray());
        Assert.DoesNotContain(applicationReferences, reference => reference.Name == "ILGPU");
    }
}
