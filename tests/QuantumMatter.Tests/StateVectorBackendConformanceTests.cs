using System.Numerics;
using QuantumMatter.Application;
using QuantumMatter.Core;
using QuantumMatter.Infrastructure;
using Xunit;

namespace QuantumMatter.Tests;

public abstract partial class StateVectorBackendConformanceTests
{
    protected abstract IStateVectorBackend CreateBackend();
    private StateVectorSimulator Simulator => new(CreateBackend());

    public static TheoryData<GateKind, int, Complex, Complex> KnownColumns => new()
    {
        { GateKind.H, 0, Math.Sqrt(0.5), Math.Sqrt(0.5) },
        { GateKind.H, 1, Math.Sqrt(0.5), -Math.Sqrt(0.5) },
        { GateKind.X, 0, 0, 1 },
        { GateKind.X, 1, 1, 0 },
        { GateKind.Y, 0, 0, Complex.ImaginaryOne },
        { GateKind.Y, 1, -Complex.ImaginaryOne, 0 },
        { GateKind.Z, 0, 1, 0 },
        { GateKind.Z, 1, 0, -1 },
        { GateKind.S, 0, 1, 0 },
        { GateKind.S, 1, 0, Complex.ImaginaryOne },
        { GateKind.T, 0, 1, 0 },
        { GateKind.T, 1, 0, new Complex(Math.Sqrt(0.5), Math.Sqrt(0.5)) },
        { GateKind.Rx, 0, 0, -Complex.ImaginaryOne },
        { GateKind.Rx, 1, -Complex.ImaginaryOne, 0 },
        { GateKind.Ry, 0, 0, 1 },
        { GateKind.Ry, 1, -1, 0 },
        { GateKind.Rz, 0, -Complex.ImaginaryOne, 0 },
        { GateKind.Rz, 1, 0, Complex.ImaginaryOne },
        { GateKind.I, 0, 1, 0 },
        { GateKind.I, 1, 0, 1 },
        { GateKind.Sdg, 0, 1, 0 },
        { GateKind.Sdg, 1, 0, -Complex.ImaginaryOne },
        { GateKind.Tdg, 0, 1, 0 },
        { GateKind.Tdg, 1, 0, new Complex(Math.Sqrt(0.5), -Math.Sqrt(0.5)) },
        { GateKind.Phase, 0, 1, 0 },
        { GateKind.Phase, 1, 0, -1 }
    };

    [Theory]
    [MemberData(nameof(KnownColumns))]
    public void Gates_have_correct_complex_matrix_columns_on_every_qubit(
        GateKind kind, int input, Complex zero, Complex one)
    {
        for (int target = 0; target < 4; target++)
        {
            var circuit = new QuantumCircuit(4);
            // Spectator qubits set to one expose addressing errors in high and low target positions.
            int spectators = 15 ^ (1 << target);
            for (int qubit = 0; qubit < 4; qubit++)
            {
                if (qubit != target || input == 1)
                {
                    circuit.X(qubit);
                }
            }

            Append(circuit, kind, target, Math.PI);
            StateVector state = Simulator.Simulate(circuit);
            for (int index = 0; index < state.Length; index++)
            {
                Complex expected = index == spectators ? zero : index == 15 ? one : Complex.Zero;
                Near(expected, state.Amplitude(index));
            }
        }
    }

    [Fact]
    public void Controlled_not_and_swap_match_truth_tables_for_all_basis_states_and_orderings()
    {
        for (int first = 0; first < 4; first++)
        {
            for (int second = 0; second < 4; second++)
            {
                if (first == second)
                {
                    continue;
                }

                for (int basis = 0; basis < 16; basis++)
                {
                    var cnot = BasisCircuit(4, basis).CNot(first, second);
                    int expectedCnot = (basis & (1 << first)) == 0 ? basis : basis ^ (1 << second);
                    AssertBasis(Simulator.Simulate(cnot), expectedCnot);

                    var swap = BasisCircuit(4, basis).Swap(first, second);
                    int expectedSwap = ((basis >> first) & 1) == ((basis >> second) & 1)
                        ? basis : basis ^ (1 << first) ^ (1 << second);
                    AssertBasis(Simulator.Simulate(swap), expectedSwap);
                }
            }
        }
    }

    [Fact]
    public void Interference_distinguishes_relative_phase_and_gate_order()
    {
        AssertBasis(Simulator.Simulate(new QuantumCircuit(1).H(0).Z(0).H(0)), 1);
        Near(Complex.ImaginaryOne, Simulator.Simulate(new QuantumCircuit(1).X(0).S(0)).Amplitude(1));
        Near(Complex.One, Simulator.Simulate(new QuantumCircuit(1).S(0).X(0)).Amplitude(1));
        var ghz = Simulator.Simulate(new QuantumCircuit(4).H(0).CNot(0, 2).CNot(2, 3).CNot(3, 1));
        Near(Math.Sqrt(0.5), ghz.Amplitude(0));
        Near(Math.Sqrt(0.5), ghz.Amplitude(15));
        Assert.All(Enumerable.Range(1, 14), index => Near(0, ghz.Amplitude(index)));
    }

    [Fact]
    public void Long_circuit_preserves_norm_and_inverse_recovers_original_state()
    {
        var circuit = new QuantumCircuit(5);
        for (int round = 0; round < 200; round++)
        {
            circuit.H(round % 5).Rx((round + 1) % 5, 0.371)
                .Ry((round + 2) % 5, -1.28).Rz((round + 3) % 5, 2.4)
                .CNot(round % 5, (round + 1) % 5).Swap((round + 2) % 5, (round + 4) % 5);
        }

        StateVector state = Simulator.Simulate(circuit);
        Assert.InRange(Enumerable.Range(0, state.Length).Sum(state.Probability), 1 - 1e-11, 1 + 1e-11);

        GateOperation[] forward = circuit.Operations.ToArray();
        foreach (GateOperation operation in forward.Reverse())
        {
            if (operation.Kind == GateKind.CNot)
            {
                circuit.CNot(operation.Control, operation.Target);
            }
            else if (operation.Kind == GateKind.Swap)
            {
                circuit.Swap(operation.Control, operation.Target);
            }
            else
            {
                Append(circuit, operation.Kind, operation.Target, -operation.Angle);
            }
        }

        AssertBasis(Simulator.Simulate(circuit), 0, 1e-11);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(613)]
    public void Random_circuits_match_independent_dense_matrix_oracle(int seed)
    {
        var random = new Random(seed);
        for (int qubits = 1; qubits <= 5; qubits++)
        {
            var circuit = new QuantumCircuit(qubits);
            var expected = new Complex[1 << qubits];
            expected[0] = 1;
            for (int step = 0; step < 80; step++)
            {
                GateKind kind = (GateKind)random.Next(qubits == 1 ? 9 : 11);
                int target = random.Next(qubits);
                int other = qubits == 1 ? 0 : (target + random.Next(1, qubits)) % qubits;
                if (kind == GateKind.CNot)
                {
                    circuit.CNot(other, target);
                }
                else if (kind == GateKind.Swap)
                {
                    circuit.Swap(other, target);
                }
                else
                {
                    Append(circuit, kind, target, random.NextDouble() * 8 - 4);
                }

                expected = DenseMultiply(expected, circuit.Operations[^1], qubits);
            }

            StateVector actual = Simulator.Simulate(circuit);
            for (int index = 0; index < expected.Length; index++)
            {
                Near(expected[index], actual.Amplitude(index), 2e-12);
            }
        }
    }

    // Deliberately materializes the full operator and uses row/column multiplication.
    // This oracle does not use the production pair iteration or in-place permutations.
    private static Complex[] DenseMultiply(Complex[] vector, GateOperation op, int qubits)
    {
        var matrix = new Complex[vector.Length, vector.Length];
        Complex[,] local = LocalMatrix(op);
        for (int row = 0; row < vector.Length; row++)
        {
            for (int column = 0; column < vector.Length; column++)
            {
                if (op.Kind is GateKind.CNot or GateKind.Toffoli)
                {
                    bool controlSet = ((column >> op.Control) & 1) == 1;
                    if (op.Kind == GateKind.Toffoli)
                    {
                        controlSet &= ((column >> op.SecondControl) & 1) == 1;
                    }
                    int destination = controlSet ? column ^ (1 << op.Target) : column;
                    matrix[row, column] = row == destination ? 1 : 0;
                }
                else if (op.Kind == GateKind.Swap)
                {
                    int first = (column >> op.Control) & 1;
                    int second = (column >> op.Target) & 1;
                    int destination = column - (first << op.Control) - (second << op.Target)
                        + (first << op.Target) + (second << op.Control);
                    matrix[row, column] = row == destination ? 1 : 0;
                }
                else
                {
                    if (op.Control >= 0 && ((column >> op.Control) & 1) == 0)
                    {
                        matrix[row, column] = row == column ? 1 : 0;
                        continue;
                    }

                    Complex product = 1;
                    for (int q = qubits - 1; q >= 0; q--)
                    {
                        int r = (row >> q) & 1;
                        int c = (column >> q) & 1;
                        product *= q == op.Target ? local[r, c] : r == c ? 1 : 0;
                    }

                    matrix[row, column] = product;
                }
            }
        }

        var result = new Complex[vector.Length];
        for (int row = 0; row < vector.Length; row++)
        {
            for (int column = 0; column < vector.Length; column++)
            {
                result[row] += matrix[row, column] * vector[column];
            }
        }

        return result;
    }

    private static Complex[,] LocalMatrix(GateOperation op)
    {
        Complex i = Complex.ImaginaryOne;
        Complex[,] x = { { 0, 1 }, { 1, 0 } };
        Complex[,] y = { { 0, -i }, { i, 0 } };
        Complex[,] z = { { 1, 0 }, { 0, -1 } };
        if (op.Kind is GateKind.Rx or GateKind.Ry or GateKind.Rz)
        {
            Complex[,] pauli = op.Kind == GateKind.Rx ? x : op.Kind == GateKind.Ry ? y : z;
            var rotation = new Complex[2, 2];
            for (int row = 0; row < 2; row++)
            {
                for (int column = 0; column < 2; column++)
                {
                    rotation[row, column] = (row == column ? Math.Cos(op.Angle / 2) : 0)
                        - i * Math.Sin(op.Angle / 2) * pauli[row, column];
                }
            }

            return rotation;
        }

        return op.Kind switch
        {
            GateKind.H => new Complex[,] { { 1 / Math.Sqrt(2), 1 / Math.Sqrt(2) }, { 1 / Math.Sqrt(2), -1 / Math.Sqrt(2) } },
            GateKind.X => x,
            GateKind.Y => y,
            GateKind.Z => z,
            GateKind.S => new Complex[,] { { 1, 0 }, { 0, i } },
            GateKind.T => new Complex[,] { { 1, 0 }, { 0, new Complex(Math.Cos(Math.PI / 4), Math.Sin(Math.PI / 4)) } },
            GateKind.I => new Complex[,] { { 1, 0 }, { 0, 1 } },
            GateKind.Sdg => new Complex[,] { { 1, 0 }, { 0, -i } },
            GateKind.Tdg => new Complex[,] { { 1, 0 }, { 0, new Complex(Math.Cos(-Math.PI / 4), Math.Sin(-Math.PI / 4)) } },
            GateKind.Phase => new Complex[,] { { 1, 0 }, { 0, new Complex(Math.Cos(op.Angle), Math.Sin(op.Angle)) } },
            GateKind.Unitary when op.Unitary is { } u => new Complex[,] { { u.M00, u.M01 }, { u.M10, u.M11 } },
            GateKind.CNot or GateKind.Swap or GateKind.Toffoli => new Complex[2, 2],
            _ => throw new ArgumentOutOfRangeException(nameof(op), "The oracle requires an independent matrix for every gate.")
        };
    }

    internal static QuantumCircuit Append(QuantumCircuit circuit, GateKind kind, int target, double angle) => kind switch
    {
        GateKind.H => circuit.H(target),
        GateKind.X => circuit.X(target),
        GateKind.Y => circuit.Y(target),
        GateKind.Z => circuit.Z(target),
        GateKind.S => circuit.S(target),
        GateKind.T => circuit.T(target),
        GateKind.Rx => circuit.Rx(target, angle),
        GateKind.Ry => circuit.Ry(target, angle),
        GateKind.Rz => circuit.Rz(target, angle),
        GateKind.I => circuit.I(target),
        GateKind.Sdg => circuit.Sdg(target),
        GateKind.Tdg => circuit.Tdg(target),
        GateKind.Phase => circuit.Phase(target, angle),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static QuantumCircuit BasisCircuit(int qubits, int basis)
    {
        var circuit = new QuantumCircuit(qubits);
        for (int q = 0; q < qubits; q++)
        {
            if ((basis & (1 << q)) != 0)
            {
                circuit.X(q);
            }
        }

        return circuit;
    }

    private static void AssertBasis(StateVector state, int basis, double tolerance = 1e-12)
    {
        for (int index = 0; index < state.Length; index++)
        {
            Near(index == basis ? Complex.One : Complex.Zero, state.Amplitude(index), tolerance);
        }
    }

    private static void Near(Complex expected, Complex actual, double tolerance = 1e-12) =>
        Assert.True(Complex.Abs(expected - actual) <= tolerance, $"Expected {expected}; actual {actual}.");
}

public sealed class CpuBackendConformanceTests : StateVectorBackendConformanceTests
{
    protected override IStateVectorBackend CreateBackend() => new CpuStateVectorBackend();
}
