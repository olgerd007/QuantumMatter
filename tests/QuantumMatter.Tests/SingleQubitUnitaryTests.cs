using System.Numerics;
using QuantumMatter.Core;
using Xunit;

namespace QuantumMatter.Tests;

public sealed class SingleQubitUnitaryTests
{
    [Fact]
    public void Existing_operation_construction_and_deconstruction_remain_supported()
    {
        var operation = new GateOperation(GateKind.Rx, 2, -1, 0.75);
        var (kind, target, control, angle) = operation;
        Assert.Equal(GateKind.Rx, kind);
        Assert.Equal(2, target);
        Assert.Equal(-1, control);
        Assert.Equal(0.75, angle);
        Assert.Equal(-1, operation.SecondControl);
        Assert.Null(operation.Unitary);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(2, 3)]
    [InlineData(3, 3)]
    public void Matrix_dimensions_must_be_exactly_two_by_two(int rows, int columns) =>
        Assert.Throws<ArgumentException>(() => SingleQubitUnitary.Create(new Complex[rows, columns]));

    [Fact]
    public void Null_matrix_is_rejected() =>
        Assert.Throws<ArgumentNullException>(() => SingleQubitUnitary.Create(null!));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Every_matrix_component_must_be_finite(double value)
    {
        for (int entry = 0; entry < 4; entry++)
        {
            foreach (Complex invalid in new[] { new Complex(value, 0), new Complex(0, value) })
            {
                Complex[] values = { 1, 0, 0, 1 };
                values[entry] = invalid;
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    SingleQubitUnitary.Create(values[0], values[1], values[2], values[3]));
            }
        }
    }

    [Fact]
    public void Norms_and_column_orthogonality_are_both_required()
    {
        Assert.Throws<ArgumentException>(() => SingleQubitUnitary.Create(0, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => SingleQubitUnitary.Create(2, 0, 0, 1));
        Assert.Throws<ArgumentException>(() => SingleQubitUnitary.Create(1, 1, 0, 0));
        Assert.Throws<ArgumentException>(() => SingleQubitUnitary.Create(1, Complex.ImaginaryOne, 0, 0));
        Assert.Throws<ArgumentException>(() => SingleQubitUnitary.Create(double.MaxValue, 0, 0, 1));
    }

    [Fact]
    public void Validation_tolerance_is_fixed_and_does_not_silently_normalize()
    {
        double nearUnit = Math.Sqrt(1 + 2e-13);
        Assert.Equal(new Complex(nearUnit, 0), SingleQubitUnitary.Create(nearUnit, 0, 0, 1).M00);
        Assert.Throws<ArgumentException>(() => SingleQubitUnitary.Create(Math.Sqrt(1 + 2e-10), 0, 0, 1));
    }

    [Fact]
    public void Matrix_is_copied_and_adjoint_is_conjugate_transpose()
    {
        Complex[,] entries = { { 0, Complex.ImaginaryOne }, { 1, 0 } };
        SingleQubitUnitary unitary = SingleQubitUnitary.Create(entries);
        entries[0, 1] = 0;
        Assert.Equal(Complex.ImaginaryOne, unitary.M01);
        Assert.Equal(SingleQubitUnitary.Create(0, 1, -Complex.ImaginaryOne, 0), unitary.Adjoint());
        Assert.Equal(unitary, unitary.Adjoint().Adjoint());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void Every_new_gate_rejects_invalid_operands_before_append(int qubit)
    {
        var circuit = new QuantumCircuit(3);
        SingleQubitUnitary identity = SingleQubitUnitary.Create(1, 0, 0, 1);
        foreach (GateKind kind in Enum.GetValues<GateKind>().Where(k => k is not (GateKind.CNot or GateKind.Swap or GateKind.Toffoli)))
        {
            if (kind != GateKind.Unitary)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => StateVectorBackendConformanceTests.Append(circuit, kind, qubit, 0));
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => StateVectorBackendConformanceTests.AppendControlled(circuit, kind, qubit, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => StateVectorBackendConformanceTests.AppendControlled(circuit, kind, 0, qubit, 0));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => circuit.U(qubit, identity));
        Assert.Throws<ArgumentOutOfRangeException>(() => circuit.Toffoli(qubit, 1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => circuit.Toffoli(0, qubit, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => circuit.Toffoli(0, 1, qubit));
        Assert.Empty(circuit.Operations);
    }

    [Fact]
    public void Controlled_gate_kinds_and_distinct_operands_are_validated_before_append()
    {
        var circuit = new QuantumCircuit(3);
        foreach (GateKind invalid in new[] { GateKind.CNot, GateKind.Swap, GateKind.Toffoli, GateKind.Unitary, (GateKind)(-1), (GateKind)999 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => circuit.Controlled(invalid, 0, 1));
        }

        foreach (GateKind kind in Enum.GetValues<GateKind>().Where(k => k is not (GateKind.CNot or GateKind.Swap or GateKind.Toffoli)))
        {
            Assert.Throws<ArgumentException>(() => StateVectorBackendConformanceTests.AppendControlled(circuit, kind, 0, 0, 0));
        }

        Assert.Throws<ArgumentException>(() => circuit.Toffoli(0, 0, 2));
        Assert.Throws<ArgumentException>(() => circuit.Toffoli(0, 1, 0));
        Assert.Throws<ArgumentException>(() => circuit.Toffoli(0, 1, 1));
        Assert.Throws<ArgumentNullException>(() => circuit.U(0, null!));
        Assert.Throws<ArgumentNullException>(() => circuit.ControlledU(0, 1, null!));
        Assert.Throws<ArgumentException>(() => circuit.U(0, SingleQubitUnitary.Create(1, 1, 0, 0)));
        Assert.Empty(circuit.Operations);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Phase_and_controlled_rotation_angles_must_be_finite(double angle)
    {
        var circuit = new QuantumCircuit(2);
        Assert.Throws<ArgumentOutOfRangeException>(() => circuit.Phase(0, angle));
        Assert.Throws<ArgumentOutOfRangeException>(() => circuit.CPhase(0, 1, angle));
        Assert.Throws<ArgumentOutOfRangeException>(() => circuit.CRx(0, 1, angle));
        Assert.Throws<ArgumentOutOfRangeException>(() => circuit.CRy(0, 1, angle));
        Assert.Throws<ArgumentOutOfRangeException>(() => circuit.CRz(0, 1, angle));
        Assert.Empty(circuit.Operations);
    }
}
