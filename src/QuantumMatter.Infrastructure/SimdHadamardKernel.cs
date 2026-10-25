using System.Numerics;
using System.Runtime.InteropServices;

namespace QuantumMatter.Infrastructure;

internal static class SimdHadamardKernel
{
    private static readonly bool CompatibleLayout = CheckLayout();

    internal static bool IsSupported => Vector.IsHardwareAccelerated && CompatibleLayout && Vector<double>.Count % 2 == 0;

    internal static void Apply(Span<Complex> state, int target, int start, int end, CancellationToken cancellationToken)
    {
        int bit = 1 << target;
        int complexLanes = Vector<double>.Count / 2;
        double h = Math.Sqrt(0.5);
        var scale = new Vector<double>(h);
        int untilCancellation = 0;
        int pair = start;
        while (pair < end)
        {
            int low = pair & (bit - 1);
            int first = ((pair - low) << 1) | low;
            int second = first | bit;
            int run = Math.Min(bit - low, end - pair);
            // Cast bounded contiguous blocks; even a 30-qubit buffer cannot overflow a double-span length.
            Span<double> zero = MemoryMarshal.Cast<Complex, double>(state.Slice(first, run));
            Span<double> one = MemoryMarshal.Cast<Complex, double>(state.Slice(second, run));
            int offset = 0;
            while (offset < run)
            {
                if (untilCancellation <= 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    untilCancellation = 4096;
                }

                if (offset + complexLanes <= run)
                {
                    int lane = 2 * offset;
                    var x = new Vector<double>(zero.Slice(lane));
                    var y = new Vector<double>(one.Slice(lane));
                    Vector<double> scaledX = x * scale;
                    Vector<double> scaledY = y * scale;
                    (scaledX + scaledY).CopyTo(zero.Slice(lane));
                    (scaledX - scaledY).CopyTo(one.Slice(lane));
                    offset += complexLanes;
                    untilCancellation -= complexLanes;
                }
                else
                {
                    Complex x = state[first + offset];
                    Complex y = state[second + offset];
                    state[first + offset] = h * x + h * y;
                    state[second + offset] = h * x - h * y;
                    offset++;
                    untilCancellation--;
                }
            }

            pair += run;
        }
    }

    private static bool CheckLayout()
    {
        Span<Complex> sample = stackalloc Complex[1];
        sample[0] = new Complex(1.25, -2.5);
        Span<double> components = MemoryMarshal.Cast<Complex, double>(sample);
        return components.Length == 2 && components[0] == 1.25 && components[1] == -2.5;
    }
}
