using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace QuantumMatter.Infrastructure;

/// <summary>Opt-in double-precision kernels for contiguous groups of four complex amplitudes.</summary>
internal static class Avx512StateVectorKernel
{
    internal static bool IsSupported => Avx512F.IsSupported && Vector512.IsHardwareAccelerated
        && AvxStateVectorKernel.IsSupported;

    internal static void Apply(Span<Complex> state, PreparedStateVectorGate gate, int start, int end,
        CancellationToken cancellationToken)
    {
        if (gate.Kernel == StateVectorKernelKind.Diagonal)
        {
            ApplyDiagonal(state, gate, start, end, cancellationToken);
            return;
        }

        int bit = 1 << gate.Target;
        var a = new Coefficient(gate.A);
        var b = new Coefficient(gate.B);
        var c = new Coefficient(gate.C);
        var d = new Coefficient(gate.D);
        var scale = Vector512.Create(gate.A.Real);
        for (int pair = start; pair < end;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int low = pair & (bit - 1);
            int first = ((pair - low) << 1) | low;
            int second = first | bit;
            int run = Math.Min(Math.Min(bit - low, end - pair), 4096);
            Span<double> zero = MemoryMarshal.Cast<Complex, double>(state.Slice(first, run));
            Span<double> one = MemoryMarshal.Cast<Complex, double>(state.Slice(second, run));
            int lane = 0;
            for (; lane + 8 <= zero.Length; lane += 8)
            {
                Vector512<double> x = Vector512.LoadUnsafe(ref zero[lane]);
                Vector512<double> y = Vector512.LoadUnsafe(ref one[lane]);
                if (gate.Kernel == StateVectorKernelKind.Exchange)
                {
                    y.StoreUnsafe(ref zero[lane]);
                    x.StoreUnsafe(ref one[lane]);
                }
                else if (gate.Kernel == StateVectorKernelKind.Hadamard)
                {
                    Vector512<double> scaledX = x * scale;
                    Vector512<double> scaledY = y * scale;
                    (scaledX + scaledY).StoreUnsafe(ref zero[lane]);
                    (scaledX - scaledY).StoreUnsafe(ref one[lane]);
                }
                else
                {
                    (a.Multiply(x) + b.Multiply(y)).StoreUnsafe(ref zero[lane]);
                    (c.Multiply(x) + d.Multiply(y)).StoreUnsafe(ref one[lane]);
                }
            }

            for (int offset = lane / 2; offset < run; offset++)
            {
                Complex x = state[first + offset];
                Complex y = state[second + offset];
                if (gate.Kernel == StateVectorKernelKind.Exchange)
                {
                    state[first + offset] = y;
                    state[second + offset] = x;
                }
                else
                {
                    state[first + offset] = gate.A * x + gate.B * y;
                    state[second + offset] = gate.C * x + gate.D * y;
                }
            }

            pair += run;
        }
    }

    private static void ApplyDiagonal(Span<Complex> state, PreparedStateVectorGate gate, int start, int end,
        CancellationToken cancellationToken)
    {
        int bit = 1 << gate.Target;
        for (int index = start; index < end;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int run = Math.Min(Math.Min(bit - (index & (bit - 1)), end - index), 4096);
            Complex phase = (index & bit) == 0 ? gate.A : gate.D;
            if (phase != Complex.One)
            {
                var coefficient = new Coefficient(phase);
                Span<double> components = MemoryMarshal.Cast<Complex, double>(state.Slice(index, run));
                int lane = 0;
                for (; lane + 8 <= components.Length; lane += 8)
                {
                    coefficient.Multiply(Vector512.LoadUnsafe(ref components[lane])).StoreUnsafe(ref components[lane]);
                }

                for (int offset = lane / 2; offset < run; offset++) { state[index + offset] *= phase; }
            }

            index += run;
        }
    }

    private readonly struct Coefficient
    {
        private readonly Vector512<double> _real;
        private readonly Vector512<double> _imaginary;
        private readonly bool _isReal;

        internal Coefficient(Complex value)
        {
            _real = Vector512.Create(value.Real);
            double imaginary = value.Imaginary;
            _imaginary = Vector512.Create(-imaginary, imaginary, -imaginary, imaginary,
                -imaginary, imaginary, -imaginary, imaginary);
            _isReal = imaginary == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Vector512<double> Multiply(Vector512<double> value) => _isReal
            ? value * _real
            : value * _real + Avx512F.Permute2x64(value, 0x55) * _imaginary;
    }
}
