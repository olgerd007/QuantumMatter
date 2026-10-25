using System.Numerics;
using ILGPU;
using ILGPU.Runtime;

namespace QuantumMatter.Infrastructure;

// Explicit conversion avoids depending on the private binary layout of System.Numerics.Complex.
internal readonly struct DeviceComplex(double real, double imaginary)
{
    public readonly double Real = real;
    public readonly double Imaginary = imaginary;

    internal static DeviceComplex FromComplex(Complex value) => new(value.Real, value.Imaginary);
    internal Complex ToComplex() => new(Real, Imaginary);

    public static DeviceComplex operator +(DeviceComplex left, DeviceComplex right) =>
        new(left.Real + right.Real, left.Imaginary + right.Imaginary);

    public static DeviceComplex operator *(DeviceComplex left, DeviceComplex right) =>
        new(left.Real * right.Real - left.Imaginary * right.Imaginary,
            left.Real * right.Imaginary + left.Imaginary * right.Real);
}

internal readonly struct DeviceGate(PreparedStateVectorGate gate)
{
    public readonly StateVectorKernelKind Kind = gate.Kernel;
    public readonly int TargetBit = 1 << gate.Target;
    public readonly int ControlMask = gate.ControlMask;
    public readonly int SwapBit = gate.SwapControl >= 0 ? 1 << gate.SwapControl : 0;
    public readonly DeviceComplex A = DeviceComplex.FromComplex(gate.A);
    public readonly DeviceComplex B = DeviceComplex.FromComplex(gate.B);
    public readonly DeviceComplex C = DeviceComplex.FromComplex(gate.C);
    public readonly DeviceComplex D = DeviceComplex.FromComplex(gate.D);
}

internal static class CudaStateVectorKernel
{
    internal static void Apply(Index1D index, ArrayView1D<DeviceComplex, Stride1D.Dense> state, DeviceGate gate)
    {
        // Insert a zero target bit. Every CUDA thread owns one disjoint amplitude pair.
        int pair = index;
        int low = pair & (gate.TargetBit - 1);
        int first = ((pair - low) << 1) | low;
        int second = first | gate.TargetBit;
        if ((first & gate.ControlMask) != gate.ControlMask)
        {
            return;
        }

        if (gate.Kind == StateVectorKernelKind.Swap)
        {
            if ((first & gate.SwapBit) == 0)
            {
                return;
            }

            second ^= gate.SwapBit;
        }

        DeviceComplex zero = state[first];
        DeviceComplex one = state[second];
        if (gate.Kind == StateVectorKernelKind.Exchange || gate.Kind == StateVectorKernelKind.Swap)
        {
            state[first] = one;
            state[second] = zero;
        }
        else if (gate.Kind == StateVectorKernelKind.Diagonal)
        {
            state[first] = gate.A * zero;
            state[second] = gate.D * one;
        }
        else
        {
            state[first] = gate.A * zero + gate.B * one;
            state[second] = gate.C * zero + gate.D * one;
        }
    }
}
