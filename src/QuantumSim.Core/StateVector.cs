using System.Numerics;

namespace QuantumSim.Core;

public sealed class StateVector
{
    private readonly Complex[] _amplitudes;
    public int QubitCount { get; }
    public int Length => _amplitudes.Length;

    public StateVector(int qubits)
    {
        if (qubits is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(qubits));
        QubitCount = qubits;
        _amplitudes = new Complex[1 << qubits];
        _amplitudes[0] = Complex.One;
    }

    public Complex Amplitude(int basisIndex) => _amplitudes[basisIndex];
    public double Probability(int basisIndex)
    {
        var a = Amplitude(basisIndex);
        return a.Real * a.Real + a.Imaginary * a.Imaginary;
    }

    public Complex[] Snapshot() => (Complex[])_amplitudes.Clone();

    internal void ApplyMatrix(int target, Complex a, Complex b, Complex c, Complex d)
    {
        int bit = 1 << target;
        int span = bit << 1;
        for (int start = 0; start < _amplitudes.Length; start += span)
        {
            for (int offset = 0; offset < bit; offset++)
            {
                int i = start + offset;
                int j = i + bit;
                Complex x = _amplitudes[i], y = _amplitudes[j];
                _amplitudes[i] = a * x + b * y;
                _amplitudes[j] = c * x + d * y;
            }
        }
    }

    internal void ApplyCNot(int control, int target)
    {
        int c = 1 << control, t = 1 << target;
        for (int i = 0; i < _amplitudes.Length; i++)
            if ((i & c) != 0 && (i & t) == 0)
                (_amplitudes[i], _amplitudes[i | t]) = (_amplitudes[i | t], _amplitudes[i]);
    }

    internal void ApplySwap(int first, int second)
    {
        int a = 1 << first, b = 1 << second;
        for (int i = 0; i < _amplitudes.Length; i++)
            if ((i & a) == 0 && (i & b) != 0)
            {
                int j = i ^ a ^ b;
                (_amplitudes[i], _amplitudes[j]) = (_amplitudes[j], _amplitudes[i]);
            }
    }

    public int MeasureAll(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        double draw = random.NextDouble(), cumulative = 0;
        int outcome = _amplitudes.Length - 1;
        for (int i = 0; i < _amplitudes.Length; i++)
        {
            cumulative += Probability(i);
            if (draw < cumulative) { outcome = i; break; }
        }
        Array.Clear(_amplitudes);
        _amplitudes[outcome] = Complex.One;
        return outcome;
    }
}
