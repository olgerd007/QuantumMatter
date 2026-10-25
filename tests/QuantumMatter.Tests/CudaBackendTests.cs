using System.Buffers;
using System.Numerics;
using QuantumMatter.Application;
using QuantumMatter.Application.Algorithms;
using QuantumMatter.Application.Noise;
using QuantumMatter.Core;
using QuantumMatter.Core.Noise;
using QuantumMatter.Infrastructure;
using Xunit;

namespace QuantumMatter.Tests;

public sealed class CudaConfigurationTests
{
    [Theory]
    [InlineData(-1, 32, 32)]
    [InlineData(0, -1, 32)]
    [InlineData(0, 31, 32)]
    [InlineData(0, 32, 0)]
    [InlineData(0, 32, 31)]
    public void Invalid_options_are_rejected_before_device_access(int device, long deviceBudget, long stagingBudget) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CudaStateVectorBackend(new CudaBackendOptions
        {
            DeviceIndex = device,
            DeviceMemoryBudgetBytes = deviceBudget,
            StagingMemoryBudgetBytes = stagingBudget
        }));

    [Fact]
    public void Discovery_never_falls_back_to_cpu()
    {
        IReadOnlyList<CudaDeviceInfo> devices = CudaStateVectorBackend.GetDevices();
        if (devices.Count == 0)
        {
            Assert.Throws<PlatformNotSupportedException>(() => new CudaStateVectorBackend());
        }
        else
        {
            Assert.All(devices, device => Assert.True(device.MemoryBytes > 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CudaStateVectorBackend(
                new CudaBackendOptions { DeviceIndex = devices.Count }));
        }
    }
}

#if CUDA_TESTS
// Opt-in is deliberate: these tests fail, rather than skip or emulate, when CUDA is unavailable.
public sealed class CudaBackendFixture : IDisposable
{
    public CudaStateVectorBackend Backend { get; } = new(new CudaBackendOptions { EnableProfiling = true });
    public void Dispose() => Backend.Dispose();
}

public sealed class CudaBackendConformanceTests(CudaBackendFixture fixture) :
    StateVectorBackendConformanceTests, IClassFixture<CudaBackendFixture>
{
    protected override IStateVectorBackend CreateBackend() => fixture.Backend;
}

public sealed class CudaBackendTests(CudaBackendFixture fixture) : IClassFixture<CudaBackendFixture>
{
    [Fact]
    public void Noisy_trajectories_reuse_CUDA_unitary_segments_with_CPU_channels()
    {
        var cpu = new NoisyStateVectorSimulator(new CpuStateVectorBackend(), new CpuNoiseChannelBackend());
        var gpu = new NoisyStateVectorSimulator(fixture.Backend, new CpuNoiseChannelBackend());
        foreach (NoiseKind kind in Enum.GetValues<NoiseKind>())
        {
            var circuit = new NoisyCircuit(3).Append(new QuantumCircuit(3).H(0).CNot(0, 2).Ry(1, 0.41))
                .ApplyNoise(NoiseChannelTests.Channel(kind, 0.35), 2)
                .Append(new QuantumCircuit(3).CNot(0, 2).H(0));
            NoisyProbabilityResult expected = cpu.EstimateProbabilities(circuit, 64, seed: 415);
            NoisyProbabilityResult actual = gpu.EstimateProbabilities(circuit, 64, seed: 415);
            for (int index = 0; index < actual.Length; index++)
            {
                Assert.InRange(Math.Abs(expected.Estimate(index).Probability - actual.Estimate(index).Probability), 0, 3e-12);
                Assert.InRange(Math.Abs(expected.Estimate(index).StandardError - actual.Estimate(index).StandardError), 0, 3e-12);
            }

            Assert.Equal(cpu.Run(circuit, 32, seed: 962).Counts.ToArray(), gpu.Run(circuit, 32, seed: 962).Counts.ToArray());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cancellation_and_host_failure_after_device_work_release_resources(bool cancel)
    {
        using var source = new CancellationTokenSource();
        using var manager = new CommitFailureMemory(() =>
        {
            if (cancel)
            {
                source.Cancel();
            }
            else
            {
                throw new InvalidOperationException("Injected host commit failure.");
            }
        });
        Memory<Complex> memory = manager.Memory;
        manager.Arm();
        var circuit = new QuantumCircuit(2).H(0).CNot(0, 1);
        if (cancel)
        {
            OperationCanceledException exception = Assert.Throws<OperationCanceledException>(
                () => fixture.Backend.Execute(circuit, memory, source.Token));
            Assert.Equal(source.Token, exception.CancellationToken);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => fixture.Backend.Execute(circuit, memory));
        }

        Assert.Equal(new Complex[] { 1, 0, 0, 0 }, manager.Storage);
        manager.Reset();
        fixture.Backend.Execute(circuit, memory);
        Assert.InRange(manager.Storage[3].Magnitude, Math.Sqrt(0.5) - 1e-12, Math.Sqrt(0.5) + 1e-12);
    }

    [Fact]
    public void Algorithms_and_seeded_terminal_sampling_work_through_application_port()
    {
        var gpu = new StateVectorSimulator(fixture.Backend);
        var scalar = new StateVectorSimulator(new CpuStateVectorBackend());
        var circuits = new[]
        {
            new QuantumCircuit(2).H(0).CNot(0, 1),
            QuantumFourierTransform.CreateCircuit(5),
            DeutschJozsa.CreateCircuit(new[] { false, true, true, false }),
            GroverSearch.CreateCircuit(3, 5)
        };
        foreach (QuantumCircuit circuit in circuits)
        {
            SimulationResult expected = scalar.Run(circuit, 1000, seed: 73);
            SimulationResult actual = gpu.Run(circuit, 1000, seed: 73);
            Assert.Equal(expected.Counts.ToArray(), actual.Counts.ToArray());
            for (int index = 0; index < actual.FinalState.Length; index++)
            {
                Assert.True(Complex.Abs(expected.FinalState.Amplitude(index) - actual.FinalState.Amplitude(index)) < 1e-12);
            }
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(9)]
    [InlineData(17)]
    public void Arbitrary_complex_states_and_offset_buffers_match_scalar(int qubits)
    {
        var circuit = new QuantumCircuit(qubits);
        for (int target = 0; target < qubits; target++)
        {
            circuit.H(target).Rx(target, 0.17).Ry(target, -0.83).Rz(target, 1.31).Phase(target, -0.4);
            if (target != 0)
            {
                circuit.CNot(0, target).Swap(target, 0).CRx(target, 0, -0.6);
            }
        }

        if (qubits >= 3)
        {
            circuit.Toffoli(qubits - 1, 0, 1);
        }

        var initial = new Complex[1 << qubits];
        double scale = 1 / Math.Sqrt(initial.Length);
        for (int index = 0; index < initial.Length; index++)
        {
            initial[index] = Complex.FromPolarCoordinates(scale, index * 0.37);
        }

        var storage = new Complex[initial.Length + 4];
        Array.Fill(storage, new Complex(7, -2));
        initial.CopyTo(storage, 2);
        new CpuStateVectorBackend().Execute(circuit, initial);
        fixture.Backend.Execute(circuit, storage.AsMemory(2, initial.Length));
        for (int index = 0; index < initial.Length; index++)
        {
            Assert.True(Complex.Abs(initial[index] - storage[index + 2]) < 3e-12);
        }

        Assert.All(storage.Take(2).Concat(storage.TakeLast(2)), value => Assert.Equal(new Complex(7, -2), value));
    }

    [Theory]
    [InlineData(32, 64)]
    [InlineData(64, 32)]
    public void Resource_rejection_leaves_input_unchanged_and_backend_reusable(long deviceBudget, long stagingBudget)
    {
        using var backend = new CudaStateVectorBackend(new CudaBackendOptions
        {
            DeviceMemoryBudgetBytes = deviceBudget,
            StagingMemoryBudgetBytes = stagingBudget
        });
        Complex[] state = [1, 0, 0, 0];
        Assert.Throws<InvalidOperationException>(() => backend.Execute(new QuantumCircuit(2).H(0), state));
        Assert.Equal(new Complex[] { 1, 0, 0, 0 }, state);
        Complex[] small = [1, 0];
        backend.Execute(new QuantumCircuit(1).X(0), small);
        Assert.Equal(new Complex[] { 0, 1 }, small);
    }

    [Fact]
    public void Precancellation_and_bad_buffers_leave_backend_reusable()
    {
        Complex[] state = [1, 0, 0, 0];
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => fixture.Backend.Execute(new QuantumCircuit(2).H(0), state, source.Token));
        Assert.Equal(new Complex[] { 1, 0, 0, 0 }, state);
        Assert.Throws<ArgumentNullException>(() => fixture.Backend.Execute(null!, state));
        Assert.Throws<ArgumentException>(() => fixture.Backend.Execute(new QuantumCircuit(1), state));
        fixture.Backend.Execute(new QuantumCircuit(2).X(1), state);
        Assert.Equal(Complex.One, state[2]);
    }

    [Fact]
    public void Profiling_counts_launches_and_reports_device_time()
    {
        Complex[] state = [1, 0, 0, 0];
        CudaExecutionProfile profile = fixture.Backend.ExecuteProfiled(new QuantumCircuit(2).I(0).H(0).CNot(0, 1), state);
        Assert.Equal(64, profile.StateBytes);
        Assert.Equal(2, profile.KernelLaunches);
        Assert.True(profile.DeviceEvolutionTime >= TimeSpan.Zero);
        using var plain = new CudaStateVectorBackend();
        Assert.Throws<InvalidOperationException>(() => plain.ExecuteProfiled(new QuantumCircuit(2), state));
    }

    [Fact]
    public void Disposed_backend_rejects_execution_and_disposal_is_idempotent()
    {
        var backend = new CudaStateVectorBackend();
        backend.Dispose();
        backend.Dispose();
        Assert.Throws<ObjectDisposedException>(() => backend.Execute(new QuantumCircuit(1), new Complex[2]));
    }

    [Fact]
    public void Concurrent_calls_use_independent_states()
    {
        var circuit = new QuantumCircuit(8).H(7).CNot(7, 0).CRy(0, 3, 0.2);
        Complex[] expected = new Complex[256];
        expected[0] = 1;
        new CpuStateVectorBackend().Execute(circuit, expected);
        Parallel.For(0, 8, _ =>
        {
            Complex[] state = new Complex[256];
            state[0] = 1;
            fixture.Backend.Execute(circuit, state);
            for (int index = 0; index < state.Length; index++)
            {
                Assert.True(Complex.Abs(expected[index] - state[index]) < 1e-12);
            }
        });
    }

    private sealed class CommitFailureMemory(Action onCommit) : MemoryManager<Complex>
    {
        private int _reads;
        private bool _armed;
        internal Complex[] Storage { get; } = [1, 0, 0, 0];
        internal void Arm() { _reads = 0; _armed = true; }
        internal void Reset() => _armed = false;
        public override Span<Complex> GetSpan()
        {
            if (_armed && ++_reads == 2)
            {
                onCommit();
            }

            return Storage;
        }

        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();
        public override void Unpin() { }
        protected override void Dispose(bool disposing) { }
    }
}
#else
public sealed class CudaHardwareTestsDisabled
{
    [Fact(Skip = "NVIDIA integration suite is opt-in: dotnet test -p:EnableCudaTests=true (requires real CUDA hardware).")]
    public void Enable_hardware_tests_to_validate_CUDA() { }
}
#endif
