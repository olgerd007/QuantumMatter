using System.Numerics;
using QuantumMatter.Application;
using QuantumMatter.Application.Algorithms;
using QuantumMatter.Core;
using QuantumMatter.Infrastructure;
using Xunit;

namespace QuantumMatter.Tests;

public sealed class QuantumFourierTransformTests
{
    private readonly StateVectorSimulator _simulator = new(new CpuStateVectorBackend());

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    public void Every_basis_column_matches_the_analytic_fourier_matrix(int qubits, bool inverse)
    {
        int dimension = 1 << qubits;
        for (int input = 0; input < dimension; input++)
        {
            var circuit = new QuantumCircuit(qubits);
            for (int q = 0; q < qubits; q++)
            {
                if ((input & (1 << q)) != 0)
                {
                    circuit.X(q);
                }
            }

            QuantumFourierTransform.Append(circuit, inverse);
            StateVector actual = _simulator.Simulate(circuit);
            for (int output = 0; output < dimension; output++)
            {
                double phase = (inverse ? -1 : 1) * Math.Tau * input * output / dimension;
                Complex expected = new(Math.Cos(phase) / Math.Sqrt(dimension), Math.Sin(phase) / Math.Sqrt(dimension));
                Near(expected, actual.Amplitude(output));
            }
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Complex_superposition_matches_direct_dft_and_inverse_restores_amplitudes(int qubits)
    {
        var preparation = new QuantumCircuit(qubits);
        for (int q = 0; q < qubits; q++)
        {
            preparation.Ry(q, 0.37 * (q + 1)).Rz(q, -0.713 * q);
        }

        preparation.CNot(0, qubits - 1);
        StateVector state = _simulator.Simulate(preparation);
        Complex[] original = state.Snapshot();
        var backend = new CpuStateVectorBackend();
        state.Evolve(QuantumFourierTransform.CreateCircuit(qubits), backend);
        for (int output = 0; output < state.Length; output++)
        {
            Complex expected = 0;
            for (int input = 0; input < original.Length; input++)
            {
                double phase = Math.Tau * input * output / state.Length;
                expected += original[input] * new Complex(Math.Cos(phase), Math.Sin(phase)) / Math.Sqrt(state.Length);
            }

            Near(expected, state.Amplitude(output));
        }

        state.Evolve(QuantumFourierTransform.CreateCircuit(qubits, inverse: true), backend);
        for (int index = 0; index < state.Length; index++)
        {
            Near(original[index], state.Amplitude(index));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ordered_noncontiguous_subset_preserves_spectators_in_an_entangled_state(bool inverse)
    {
        int[] register = { 3, 0, 2 };
        int mask = register.Aggregate(0, (bits, q) => bits | (1 << q));
        StateVector state = _simulator.Simulate(new QuantumCircuit(4).H(0).Ry(1, 0.81).Rx(2, -1.31).H(3).CNot(3, 1).Rz(0, 0.67));
        Complex[] original = state.Snapshot();
        var transform = new QuantumCircuit(4);
        QuantumFourierTransform.Append(transform, register, inverse);
        state.Evolve(transform, new CpuStateVectorBackend());

        for (int output = 0; output < state.Length; output++)
        {
            Complex expected = 0;
            for (int input = 0; input < original.Length; input++)
            {
                if ((input & ~mask) != (output & ~mask))
                {
                    continue;
                }

                double phase = (inverse ? -1 : 1) * Math.Tau * Extract(input, register) * Extract(output, register) / 8;
                expected += original[input] * new Complex(Math.Cos(phase), Math.Sin(phase)) / Math.Sqrt(8);
            }

            Near(expected, state.Amplitude(output));
        }
    }

    [Fact]
    public void Invalid_subsets_and_cancellation_leave_existing_circuit_unchanged()
    {
        var circuit = new QuantumCircuit(3).X(1);
        foreach (int[] invalid in new[] { Array.Empty<int>(), new[] { -1 }, new[] { 3 }, new[] { 0, 0 }, new[] { 0, 1, 2, 3 } })
        {
            Assert.Throws<ArgumentException>(() => QuantumFourierTransform.Append(circuit, invalid));
        }

        Assert.Throws<ArgumentNullException>(() => QuantumFourierTransform.Append(null!));
        Assert.Throws<ArgumentNullException>(() => QuantumFourierTransform.Append(circuit, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => QuantumFourierTransform.CreateCircuit(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => QuantumFourierTransform.CreateCircuit(31));
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => QuantumFourierTransform.Append(circuit, cancellationToken: source.Token));
        Assert.Single(circuit.Operations);
    }

    private static int Extract(int basis, int[] register)
    {
        int local = 0;
        for (int position = 0; position < register.Length; position++)
        {
            local |= ((basis >> register[position]) & 1) << position;
        }

        return local;
    }

    private static void Near(Complex expected, Complex actual) => Assert.True(Complex.Abs(expected - actual) < 3e-12,
        $"Expected {expected}; actual {actual}.");
}
