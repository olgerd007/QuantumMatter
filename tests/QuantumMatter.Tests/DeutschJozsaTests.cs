using System.Collections;
using System.Numerics;
using QuantumMatter.Application;
using QuantumMatter.Application.Algorithms;
using QuantumMatter.Core;
using QuantumMatter.Infrastructure;
using Xunit;

namespace QuantumMatter.Tests;

public sealed class DeutschJozsaTests
{
    private readonly StateVectorSimulator _simulator = new(new CpuStateVectorBackend());

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Every_small_promised_function_matches_analytic_walsh_amplitudes(int inputQubits)
    {
        int length = 1 << inputQubits;
        for (int truthBits = 0; truthBits < 1 << length; truthBits++)
        {
            int ones = BitOperations.PopCount((uint)truthBits);
            if (ones != 0 && ones != length && ones != length / 2)
            {
                continue;
            }

            bool[] table = Enumerable.Range(0, length).Select(x => (truthBits & (1 << x)) != 0).ToArray();
            Verify(table);
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void General_balanced_tables_uncompute_all_scratch_qubits(int inputQubits)
    {
        int[] order = Enumerable.Range(0, 1 << inputQubits).ToArray();
        new Random(913 + inputQubits).Shuffle(order);
        var table = new bool[order.Length];
        foreach (int input in order.Take(order.Length / 2))
        {
            table[input] = true;
        }

        Verify(table);
    }

    [Fact]
    public void Sampling_reads_low_input_bits_and_preserves_the_final_state()
    {
        bool[] parity = Enumerable.Range(0, 8).Select(x => (BitOperations.PopCount((uint)(x & 5)) & 1) != 0).ToArray();
        SimulationResult balanced = _simulator.Run(DeutschJozsa.CreateCircuit(parity), 128, 42);
        Assert.Equal(128, balanced.Counts["0101"]);
        Assert.InRange(balanced.FinalState.Probability(5), 1 - 1e-12, 1 + 1e-12);
        SimulationResult constant = _simulator.Run(DeutschJozsa.CreateCircuit(Enumerable.Repeat(true, 8).ToArray()), 128, 42);
        Assert.Equal(128, constant.Counts["0000"]);
    }

    [Fact]
    public void Invalid_tables_and_excessive_oracle_size_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => DeutschJozsa.CreateCircuit(null!));
        foreach (int length in new[] { 0, 1, 3, 6, 1 << 17 })
        {
            Assert.Throws<ArgumentException>(() => DeutschJozsa.CreateCircuit(new bool[length]));
        }

        Assert.Throws<ArgumentException>(() => DeutschJozsa.CreateCircuit(new[] { false, false, false, true }));
        Assert.Throws<ArgumentException>(() => DeutschJozsa.CreateCircuit(new[] { true, true, true, false }));
        bool[] largeBalanced = Enumerable.Range(0, 1 << 16).Select(x => x < 1 << 15).ToArray();
        Assert.Throws<ArgumentException>(() => DeutschJozsa.CreateCircuit(largeBalanced));
        QuantumCircuit constant = DeutschJozsa.CreateCircuit(Enumerable.Repeat(true, 1 << 16).ToArray());
        Assert.Equal(30, constant.QubitCount);
        Assert.True(constant.Operations.Count < 100);
    }

    [Fact]
    public void Cancellation_is_checked_before_and_during_table_capture()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => DeutschJozsa.CreateCircuit(new[] { false, true }, source.Token));
        using var duringRead = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => DeutschJozsa.CreateCircuit(new CancelingTruthTable(duringRead), duringRead.Token));
    }

    private void Verify(bool[] table)
    {
        StateVector state = _simulator.Simulate(DeutschJozsa.CreateCircuit(table));
        for (int output = 0; output < state.Length; output++)
        {
            double expected = 0;
            if (output < table.Length)
            {
                for (int input = 0; input < table.Length; input++)
                {
                    int parity = BitOperations.PopCount((uint)(input & output)) & 1;
                    expected += (table[input] ? -1 : 1) * (parity == 0 ? 1 : -1) / (double)table.Length;
                }
            }

            Assert.True(Complex.Abs(state.Amplitude(output) - expected) < 1e-11);
        }

        bool constant = table.All(x => x == table[0]);
        Assert.InRange(state.Probability(0), constant ? 1 - 1e-11 : 0, constant ? 1 + 1e-11 : 1e-22);
    }

    private sealed class CancelingTruthTable(CancellationTokenSource source) : IReadOnlyList<bool>
    {
        public int Count => 8;
        public bool this[int index]
        {
            get
            {
                if (index == 2)
                {
                    source.Cancel();
                }

                return false;
            }
        }

        public IEnumerator<bool> GetEnumerator()
        {
            for (int index = 0; index < Count; index++)
            {
                yield return this[index];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
