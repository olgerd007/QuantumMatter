using System.Buffers;
using System.Numerics;
using QuantumMatter.Application;
using QuantumMatter.Application.Algorithms;
using QuantumMatter.Core;
using QuantumMatter.Infrastructure;
using Xunit;

namespace QuantumMatter.Tests;

public sealed class ParallelCpuBackendTests
{
    [Theory]
    [InlineData(10, 3)]
    [InlineData(12, 7)]
    public void Packed_tiles_match_analytic_H_then_Rz_tensor_columns(int target, int workers)
    {
        const int qubits = 13;
        const double angle = 0.713;
        double h = Math.Sqrt(0.5);
        Complex phase0 = Complex.FromPolarCoordinates(1, -angle / 2);
        Complex phase1 = Complex.FromPolarCoordinates(1, angle / 2);
        Complex[] initial = InitialState(1 << qubits);
        Complex[] actual = (Complex[])initial.Clone();
        Create(workers, simd: true, blocking: true).Execute(new QuantumCircuit(qubits).H(target).Rz(target, angle), actual);
        int bit = 1 << target;
        for (int first = 0; first < initial.Length; first++)
        {
            if ((first & bit) != 0) { continue; }
            int second = first | bit;
            // Independent 2x2 product Rz*H, tensored with identity on every spectator.
            Complex expected0 = (phase0 * h) * initial[first] + (phase0 * h) * initial[second];
            Complex expected1 = (phase1 * h) * initial[first] - (phase1 * h) * initial[second];
            Assert.True(Complex.Abs(expected0 - actual[first]) < 1e-12);
            Assert.True(Complex.Abs(expected1 - actual[second]) < 1e-12);
        }
    }

    [Theory]
    [InlineData(1, false, false)]
    [InlineData(1, true, false)]
    [InlineData(3, false, false)]
    [InlineData(3, true, false)]
    [InlineData(7, true, false)]
    [InlineData(1, true, true)]
    [InlineData(3, true, true)]
    [InlineData(7, true, true)]
    public void Gate_blocking_preserves_phases_batch_boundaries_and_all_pair_strides(int workers, bool simd, bool avx512)
    {
        var backend = Create(workers, simd, blocking: true, avx512);
        var random = new Random(1793);
        const int qubits = 11;
        foreach (int target in Enumerable.Range(0, qubits))
        {
            var circuit = new QuantumCircuit(qubits);
            for (int gate = 0; gate < 73; gate++)
            {
                switch (gate % 7)
                {
                    case 0: circuit.H(target); break;
                    case 1: circuit.Rx(target, random.NextDouble()); break;
                    case 2: circuit.Ry(target, -random.NextDouble()); break;
                    case 3: circuit.Rz(target, random.NextDouble()); break;
                    case 4: circuit.X(target); break;
                    case 5: circuit.Y(target); break;
                    case 6: circuit.Phase(target, -random.NextDouble()); break;
                }
            }

            int control = (target + 1) % qubits;
            circuit.CNot(control, target).H(target).Rz(target, Math.Tau)
                .Swap(target, control).Ry(control, -0.3).Phase(control, 0.7).I(control).H(control);
            Complex[] expected = InitialState(1 << qubits);
            var storage = new Complex[expected.Length + 2];
            storage[0] = storage[^1] = new Complex(2, -3);
            expected.CopyTo(storage, 1);
            new CpuStateVectorBackend().Execute(circuit, expected);
            backend.Execute(circuit, storage.AsMemory(1, expected.Length));
            for (int index = 0; index < expected.Length; index++)
            {
                Assert.True(Complex.Abs(expected[index] - storage[index + 1]) < 1e-12);
            }

            Assert.Equal(new Complex(2, -3), storage[0]);
            Assert.Equal(new Complex(2, -3), storage[^1]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cancellation_in_a_blocked_worker_propagates_and_backend_can_be_reused(bool avx512)
    {
        var backend = Create(3, simd: true, blocking: true, avx512);
        using var source = new CancellationTokenSource();
        using var manager = new InterruptingMemoryManager(1 << 14, () => source.Cancel());
        Memory<Complex> memory = manager.Memory;
        manager.Arm();
        var circuit = new QuantumCircuit(14).H(13).Ry(13, 0.4).Rz(13, -0.7);
        OperationCanceledException error = Assert.Throws<OperationCanceledException>(() => backend.Execute(circuit, memory, source.Token));
        Assert.Equal(source.Token, error.CancellationToken);
        manager.Reset();
        memory.Span.Clear();
        memory.Span[0] = 1;
        backend.Execute(new QuantumCircuit(14).X(0).X(0), memory);
        Assert.Equal(Complex.One, memory.Span[0]);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(3, false)]
    [InlineData(7, false)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(7, true)]
    public void Individual_simd_gates_preserve_complex_amplitudes_at_every_stride_and_partition_tail(int workers, bool avx512)
    {
        const int qubits = 9;
        var backend = Create(workers, simd: true, avx512: avx512);
        // Dense complex coefficients exercise both real/imaginary products, unlike a permutation matrix.
        double h = Math.Sqrt(0.5);
        var custom = SingleQubitUnitary.Create(h, Complex.ImaginaryOne * h, Complex.ImaginaryOne * h, h);
        foreach (int target in Enumerable.Range(0, qubits))
        {
            QuantumCircuit[] circuits =
            {
                new QuantumCircuit(qubits).H(target), new QuantumCircuit(qubits).X(target),
                new QuantumCircuit(qubits).Y(target), new QuantumCircuit(qubits).Rx(target, 0.31),
                new QuantumCircuit(qubits).Ry(target, -0.74), new QuantumCircuit(qubits).Rz(target, 1.23),
                new QuantumCircuit(qubits).Phase(target, 0.71), new QuantumCircuit(qubits).U(target, custom)
            };
            foreach (QuantumCircuit circuit in circuits)
            {
                Complex[] expected = InitialState(1 << qubits);
                var storage = new Complex[expected.Length + 2];
                storage[0] = storage[^1] = new Complex(2, -3);
                expected.CopyTo(storage, 1);
                new CpuStateVectorBackend().Execute(circuit, expected);
                backend.Execute(circuit, storage.AsMemory(1, expected.Length));
                for (int index = 0; index < expected.Length; index++)
                {
                    Assert.True(Complex.Abs(expected[index] - storage[index + 1]) < 1e-12);
                }

                Assert.Equal(new Complex(2, -3), storage[0]);
                Assert.Equal(new Complex(2, -3), storage[^1]);
            }
        }
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(7, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(7, true)]
    public void Mixed_gates_match_scalar_on_offset_buffers_and_uneven_partitions(int workers, bool simd)
    {
        var backend = Create(workers, simd);
        foreach (int qubits in new[] { 5, 13, 17 })
        {
            QuantumCircuit circuit = MixedCircuit(qubits);
            Complex[] expected = InitialState(1 << qubits);
            var storage = new Complex[expected.Length + 6];
            Array.Fill(storage, new Complex(2, -3));
            expected.CopyTo(storage, 3);
            new CpuStateVectorBackend().Execute(circuit, expected);
            backend.Execute(circuit, storage.AsMemory(3, expected.Length));
            for (int index = 0; index < expected.Length; index++)
            {
                Assert.True(Complex.Abs(expected[index] - storage[index + 3]) < 1e-12);
            }

            Assert.All(storage.Take(3).Concat(storage.TakeLast(3)), value => Assert.Equal(new Complex(2, -3), value));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void One_backend_is_reusable_by_concurrent_simulations(bool blocking, bool avx512)
    {
        var backend = Create(3, simd: true, blocking, avx512);
        QuantumCircuit circuit = MixedCircuit(11);
        Complex[] initial = InitialState(1 << 11);
        Complex[] expected = (Complex[])initial.Clone();
        new CpuStateVectorBackend().Execute(circuit, expected);
        Parallel.For(0, 8, _ =>
        {
            Complex[] state = (Complex[])initial.Clone();
            backend.Execute(circuit, state);
            for (int index = 0; index < expected.Length; index++)
            {
                Assert.True(Complex.Abs(expected[index] - state[index]) < 1e-12);
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cancellation_from_a_running_worker_propagates_and_does_not_poison_the_backend(bool simd)
    {
        var backend = Create(3, simd);
        using var source = new CancellationTokenSource();
        using var manager = new InterruptingMemoryManager(1 << 14, () => source.Cancel());
        Memory<Complex> memory = manager.Memory;
        manager.Arm();
        var circuit = new QuantumCircuit(14).H(7).CNot(7, 0);
        OperationCanceledException error = Assert.Throws<OperationCanceledException>(() => backend.Execute(circuit, memory, source.Token));
        Assert.Equal(source.Token, error.CancellationToken);
        manager.Reset();
        memory.Span.Clear();
        memory.Span[0] = 1;
        backend.Execute(circuit, memory);
        Assert.InRange(memory.Span[0].Magnitude, Math.Sqrt(0.5) - 1e-12, Math.Sqrt(0.5) + 1e-12);
        Assert.InRange(memory.Span[129].Magnitude, Math.Sqrt(0.5) - 1e-12, Math.Sqrt(0.5) + 1e-12);
    }

    [Fact]
    public void Worker_failures_are_observable_and_workers_finish_before_returning()
    {
        var backend = Create(3, simd: false);
        using var manager = new InterruptingMemoryManager(32, () => throw new InvalidOperationException("Injected worker failure."));
        Memory<Complex> memory = manager.Memory;
        manager.Arm();
        AggregateException error = Assert.Throws<AggregateException>(() => backend.Execute(new QuantumCircuit(5).H(2), memory));
        Assert.Contains(error.Flatten().InnerExceptions, item => item is InvalidOperationException);
        manager.Reset();
        memory.Span.Clear();
        memory.Span[0] = 1;
        backend.Execute(new QuantumCircuit(5).X(4), memory);
        Assert.Equal(Complex.One, memory.Span[16]);
    }

    [Fact]
    public void Options_and_buffer_dimensions_are_validated()
    {
        foreach (int workers in new[] { -1, 0, 257 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Create(workers, false));
        }

        foreach (int threshold in new[] { 0, 31 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ParallelCpuStateVectorBackend(
                new ParallelCpuOptions { MinimumParallelQubits = threshold }));
        }

        var backend = Create(3, false);
        Assert.Throws<ArgumentNullException>(() => backend.Execute(null!, new Complex[4]));
        Assert.Throws<ArgumentException>(() => backend.Execute(new QuantumCircuit(2), new Complex[2]));
        Assert.Throws<ArgumentException>(() => backend.Execute(new QuantumCircuit(2), new Complex[8]));
    }

    [Fact]
    public void Below_threshold_fallback_and_terminal_sampling_preserve_results()
    {
        var circuit = new QuantumCircuit(5).H(0).CNot(0, 4).Ry(2, 0.4).Rz(4, -1.2);
        var reference = new StateVectorSimulator(new CpuStateVectorBackend());
        var fallback = new StateVectorSimulator(new ParallelCpuStateVectorBackend(
            new ParallelCpuOptions { MinimumParallelQubits = 30 }));
        Assert.Equal(reference.Simulate(circuit).Snapshot(), fallback.Simulate(circuit).Snapshot());

        var parallel = new StateVectorSimulator(Create(3, simd: true));
        var bell = new QuantumCircuit(2).H(0).CNot(0, 1);
        SimulationResult shots = parallel.Run(bell, 2000, 42);
        Assert.Equal(reference.Run(bell, 2000, 42).Counts.ToArray(), shots.Counts.ToArray());
        Assert.InRange(shots.FinalState.Probability(0), 0.5 - 1e-12, 0.5 + 1e-12);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void Algorithm_circuits_match_scalar_with_forced_parallel_execution(int workers, bool simd)
    {
        var reference = new StateVectorSimulator(new CpuStateVectorBackend());
        var parallel = new StateVectorSimulator(Create(workers, simd));
        bool[] truthTable = { true, false, false, true, false, true, true, false };
        var fourier = new QuantumCircuit(5).X(0).X(3);
        QuantumFourierTransform.Append(fourier);
        foreach (QuantumCircuit circuit in new[] { DeutschJozsa.CreateCircuit(truthTable), fourier, GroverSearch.CreateCircuit(4, 9) })
        {
            Complex[] expected = reference.Simulate(circuit).Snapshot();
            StateVector actual = parallel.Simulate(circuit);
            for (int index = 0; index < expected.Length; index++)
            {
                Assert.True(Complex.Abs(expected[index] - actual.Amplitude(index)) < 1e-11);
            }
        }
    }

    private static ParallelCpuStateVectorBackend Create(int workers, bool simd, bool blocking = false, bool avx512 = false) => new(
        new ParallelCpuOptions
        {
            MaxDegreeOfParallelism = workers,
            MinimumParallelQubits = 1,
            EnableSimd = simd,
            EnableGateBlocking = blocking,
            EnableAvx512 = avx512
        });

    private static QuantumCircuit MixedCircuit(int qubits)
    {
        var circuit = new QuantumCircuit(qubits);
        var custom = SingleQubitUnitary.Create(0, Complex.ImaginaryOne, 1, 0);
        foreach (int target in new[] { 0, 1, qubits / 2, qubits - 1 }.Distinct())
        {
            int control = (target + 1) % qubits;
            int second = (target + 2) % qubits;
            circuit.H(target).X(target).Y(target).Z(target).S(target).T(target)
                .Sdg(target).Tdg(target).Phase(target, 0.71).Rx(target, 0.31).Ry(target, -1.12).Rz(target, 2.13)
                .U(target, custom).CH(control, target).CY(control, target).CZ(control, target)
                .CS(control, target).CT(control, target).CSdg(control, target).CTdg(control, target)
                .CRx(control, target, 0.55).CRy(control, target, -0.74).CRz(control, target, 1.51)
                .CPhase(control, target, -0.67).ControlledU(control, target, custom)
                .CNot(control, target).Swap(control, target).Toffoli(control, second, target);
        }

        return circuit;
    }

    private static Complex[] InitialState(int length)
    {
        var values = new Complex[length];
        double norm = 0;
        for (int index = 0; index < length; index++)
        {
            values[index] = new Complex(Math.Sin(index * 0.013), Math.Cos(index * 0.007));
            norm += values[index].Magnitude * values[index].Magnitude;
        }

        double scale = 1 / Math.Sqrt(norm);
        for (int index = 0; index < length; index++)
        {
            values[index] *= scale;
        }

        return values;
    }

    private sealed class InterruptingMemoryManager(int length, Action interrupt) : MemoryManager<Complex>
    {
        private readonly Complex[] _values = new Complex[length];
        private int _armed;
        public void Arm() => Volatile.Write(ref _armed, 1);
        public void Reset() => Volatile.Write(ref _armed, 0);

        public override Span<Complex> GetSpan()
        {
            if (Interlocked.Exchange(ref _armed, 0) == 1)
            {
                interrupt();
            }

            return _values;
        }

        public override MemoryHandle Pin(int elementIndex = 0) => _values.AsMemory(elementIndex).Pin();
        public override void Unpin() { }
        protected override void Dispose(bool disposing) { }
    }
}
