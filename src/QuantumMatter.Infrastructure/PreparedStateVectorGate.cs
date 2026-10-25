using System.Numerics;
using QuantumMatter.Core;

namespace QuantumMatter.Infrastructure;

internal enum StateVectorKernelKind { Identity, Matrix, Hadamard, Exchange, Diagonal, Swap }

/// <summary>Coefficients and control masks evaluated once per gate, outside worker loops.</summary>
internal readonly record struct PreparedStateVectorGate(
    StateVectorKernelKind Kernel, int Target, int ControlMask,
    Complex A, Complex B, Complex C, Complex D, int SwapControl = -1)
{
    internal static PreparedStateVectorGate Prepare(GateOperation operation)
    {
        int mask = operation.Control >= 0 ? 1 << operation.Control : 0;
        if (operation.SecondControl >= 0)
        {
            mask |= 1 << operation.SecondControl;
        }

        PreparedStateVectorGate Matrix(Complex a, Complex b, Complex c, Complex d) =>
            new(StateVectorKernelKind.Matrix, operation.Target, mask, a, b, c, d);
        PreparedStateVectorGate Diagonal(Complex zero, Complex one) =>
            new(StateVectorKernelKind.Diagonal, operation.Target, mask, zero, 0, 0, one);

        switch (operation.Kind)
        {
            case GateKind.I:
                return new(StateVectorKernelKind.Identity, operation.Target, mask, 0, 0, 0, 0);
            case GateKind.X:
            case GateKind.CNot:
            case GateKind.Toffoli:
                return new(StateVectorKernelKind.Exchange, operation.Target, mask, 0, 0, 0, 0);
            case GateKind.Swap:
                return new(StateVectorKernelKind.Swap, operation.Target, 0, 0, 0, 0, 0, operation.Control);
            case GateKind.H:
                double h = Math.Sqrt(0.5);
                return Matrix(h, h, h, -h) with { Kernel = StateVectorKernelKind.Hadamard };
            case GateKind.Y:
                return Matrix(0, -Complex.ImaginaryOne, Complex.ImaginaryOne, 0);
            case GateKind.Z:
                return Diagonal(1, -1);
            case GateKind.S:
                return Diagonal(1, Complex.ImaginaryOne);
            case GateKind.Sdg:
                return Diagonal(1, -Complex.ImaginaryOne);
            case GateKind.T:
                return Diagonal(1, Complex.FromPolarCoordinates(1, Math.PI / 4));
            case GateKind.Tdg:
                return Diagonal(1, Complex.FromPolarCoordinates(1, -Math.PI / 4));
            case GateKind.Phase:
                return Diagonal(1, Complex.FromPolarCoordinates(1, operation.Angle));
            case GateKind.Rx:
                double xCos = Math.Cos(operation.Angle / 2);
                Complex xSin = -Complex.ImaginaryOne * Math.Sin(operation.Angle / 2);
                return Matrix(xCos, xSin, xSin, xCos);
            case GateKind.Ry:
                double yCos = Math.Cos(operation.Angle / 2);
                double ySin = Math.Sin(operation.Angle / 2);
                return Matrix(yCos, -ySin, ySin, yCos);
            case GateKind.Rz:
                return Diagonal(Complex.FromPolarCoordinates(1, -operation.Angle / 2),
                    Complex.FromPolarCoordinates(1, operation.Angle / 2));
            case GateKind.Unitary:
                SingleQubitUnitary unitary = operation.Unitary!;
                return Matrix(unitary.M00, unitary.M01, unitary.M10, unitary.M11);
            default:
                throw new NotSupportedException($"Unsupported gate: {operation.Kind}");
        }
    }
}
