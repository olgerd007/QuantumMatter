using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace QuantumMatter.Infrastructure;

/// <summary>In-place double-precision pair kernels; each range keeps exclusive amplitude ownership.</summary>
internal static class AvxStateVectorKernel
{
    internal static bool IsSupported => Avx.IsSupported && SimdHadamardKernel.IsSupported;

    internal static void Apply(Span<Complex> state, PreparedStateVectorGate gate, int start, int end,
        CancellationToken cancellationToken)
    {
        int bit = 1 << gate.Target;
        if (gate.Kernel == StateVectorKernelKind.Diagonal)
        {
            ApplyDiagonal(state, gate, start, end, cancellationToken);
            return;
        }

        var a = new Coefficient(gate.A);
        var b = new Coefficient(gate.B);
        var c = new Coefficient(gate.C);
        var d = new Coefficient(gate.D);
        // For q0, one vector holds both amplitudes. Coefficients select the two output rows.
        var ac = new Coefficient(gate.A, gate.C);
        var bd = new Coefficient(gate.B, gate.D);
        int pair = start;
        while (pair < end)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int low = pair & (bit - 1);
            int first = ((pair - low) << 1) | low;
            int second = first | bit;
            if (bit == 1)
            {
                int count = Math.Min(4096, end - pair);
                Span<double> components = MemoryMarshal.Cast<Complex, double>(state.Slice(first, 2 * count));
                for (int offset = 0; offset < components.Length; offset += 4)
                {
                    Vector256<double> input = Vector256.LoadUnsafe(ref components[offset]);
                    Vector256<double> output;
                    if (gate.Kernel == StateVectorKernelKind.Exchange)
                    {
                        output = Avx.Permute2x128(input, input, 0x01);
                    }
                    else
                    {
                        Vector256<double> x = Avx.Permute2x128(input, input, 0x00);
                        Vector256<double> y = Avx.Permute2x128(input, input, 0x11);
                        output = ac.Multiply(x) + bd.Multiply(y);
                    }

                    output.StoreUnsafe(ref components[offset]);
                }

                pair += count;
                continue;
            }

            int run = Math.Min(Math.Min(bit - low, end - pair), 4096);
            Span<double> zero = MemoryMarshal.Cast<Complex, double>(state.Slice(first, run));
            Span<double> one = MemoryMarshal.Cast<Complex, double>(state.Slice(second, run));
            int lane = 0;
            for (; lane + 4 <= zero.Length; lane += 4)
            {
                Vector256<double> x = Vector256.LoadUnsafe(ref zero[lane]);
                Vector256<double> y = Vector256.LoadUnsafe(ref one[lane]);
                if (gate.Kernel == StateVectorKernelKind.Exchange)
                {
                    y.StoreUnsafe(ref zero[lane]);
                    x.StoreUnsafe(ref one[lane]);
                }
                else
                {
                    (a.Multiply(x) + b.Multiply(y)).StoreUnsafe(ref zero[lane]);
                    (c.Multiply(x) + d.Multiply(y)).StoreUnsafe(ref one[lane]);
                }
            }

            if (lane < zero.Length)
            {
                int index = lane / 2;
                Complex x = state[first + index];
                Complex y = state[second + index];
                if (gate.Kernel == StateVectorKernelKind.Exchange)
                {
                    state[first + index] = y;
                    state[second + index] = x;
                }
                else
                {
                    state[first + index] = gate.A * x + gate.B * y;
                    state[second + index] = gate.C * x + gate.D * y;
                }
            }

            pair += run;
        }
    }

    private static void ApplyDiagonal(Span<Complex> state, PreparedStateVectorGate gate, int start, int end,
        CancellationToken cancellationToken)
    {
        int bit = 1 << gate.Target;
        int index = start;
        while (index < end)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // An odd range boundary is handled before loading two contiguous complex values.
            if ((index & 1) != 0 || index + 1 == end)
            {
                state[index] *= (index & bit) == 0 ? gate.A : gate.D;
                index++;
                continue;
            }

            Complex phase = (index & bit) == 0 ? gate.A : gate.D;
            int run = Math.Min(Math.Min(bit == 1 ? end - index : bit - (index & (bit - 1)), end - index), 4096);
            run &= ~1;
            // Identity halves of Z/S/T/Phase need neither loads nor stores.
            if (bit != 1 && phase == Complex.One)
            {
                index += run;
                continue;
            }

            var coefficient = bit == 1 ? new Coefficient(gate.A, gate.D) : new Coefficient(phase);
            Span<double> components = MemoryMarshal.Cast<Complex, double>(state.Slice(index, run));
            for (int lane = 0; lane < components.Length; lane += 4)
            {
                coefficient.Multiply(Vector256.LoadUnsafe(ref components[lane])).StoreUnsafe(ref components[lane]);
            }

            index += run;
        }
    }

    private readonly struct Coefficient
    {
        private readonly Vector256<double> _real;
        private readonly Vector256<double> _imaginary;
        private readonly bool _isReal;

        internal Coefficient(Complex value) : this(value, value) { }

        internal Coefficient(Complex first, Complex second)
        {
            _real = Vector256.Create(first.Real, first.Real, second.Real, second.Real);
            _imaginary = Vector256.Create(-first.Imaginary, first.Imaginary, -second.Imaginary, second.Imaginary);
            _isReal = first.Imaginary == 0 && second.Imaginary == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Vector256<double> Multiply(Vector256<double> value) => _isReal
            ? value * _real
            : value * _real + Avx.Permute(value, 0x05) * _imaginary;
    }
}
