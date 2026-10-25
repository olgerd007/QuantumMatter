namespace QuantumMatter.Core.Dynamic;

/// <summary>A finite sequence of unitary snapshots, measurements, classical conditions and single-qubit resets.</summary>
/// <remarks>Mutable and not thread-safe. Each execution starts with a zero classical register.</remarks>
public sealed class DynamicCircuit
{
    private readonly List<Step> _steps = [];

    /// <summary>Creates a circuit with 1–30 qubits and 1–30 classical bits, without allocating a state.</summary>
    public DynamicCircuit(int qubits, int classicalBits)
    {
        StateVector.RequiredBytes(qubits);
        if (classicalBits is < 1 or > 30) { throw new ArgumentOutOfRangeException(nameof(classicalBits)); }
        QubitCount = qubits;
        ClassicalBitCount = classicalBits;
    }

    /// <summary>The quantum register size shared by all unitary segments.</summary>
    public int QubitCount { get; }
    /// <summary>The classical register size; bit zero is least significant.</summary>
    public int ClassicalBitCount { get; }
    /// <summary>The number of appended instructions, counting each segment as one.</summary>
    public int StepCount => _steps.Count;

    /// <summary>Snapshots a size-compatible unitary segment for unconditional execution.</summary>
    public DynamicCircuit Append(QuantumCircuit circuit)
    {
        ValidateCircuit(circuit);
        _steps.Add(new UnitaryStep(circuit.Copy(), 0, 0));
        return this;
    }

    /// <summary>Snapshots a segment executed when (classicalRegister AND classicalMask) equals expectedValue.</summary>
    /// <remarks>The mask is nonzero and in range; expectedValue may only set bits included in the mask.</remarks>
    public DynamicCircuit If(int classicalMask, int expectedValue, QuantumCircuit circuit)
    {
        ValidateCircuit(circuit);
        int registerMask = (1 << ClassicalBitCount) - 1;
        if (classicalMask <= 0 || (classicalMask & ~registerMask) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(classicalMask));
        }

        if (expectedValue < 0 || (expectedValue & ~classicalMask) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedValue));
        }

        _steps.Add(new UnitaryStep(circuit.Copy(), classicalMask, expectedValue));
        return this;
    }

    /// <summary>Measures an ordered subset and overwrites the corresponding classical destination bits.</summary>
    /// <remarks>Selections are nonempty, equally sized, distinct within each selection, and copied now.</remarks>
    public DynamicCircuit Measure(ReadOnlySpan<int> qubits, ReadOnlySpan<int> classicalBits)
    {
        if (qubits.Length < 1 || qubits.Length != classicalBits.Length || qubits.Length > QubitCount)
        {
            throw new ArgumentException("Measurement requires equally sized nonempty selections.", nameof(qubits));
        }

        ValidateIndices(qubits, QubitCount, nameof(qubits));
        int writeMask = ValidateIndices(classicalBits, ClassicalBitCount, nameof(classicalBits));
        _steps.Add(new MeasurementStep(qubits.ToArray(), classicalBits.ToArray(), writeMask));
        return this;
    }

    /// <summary>Resets one qubit by projective measurement and a conditional X; no classical bit is written.</summary>
    /// <remarks>This is a stochastic trajectory of a nonunitary channel, not an amplitude-array overwrite.</remarks>
    public DynamicCircuit Reset(int qubit)
    {
        if ((uint)qubit >= (uint)QubitCount) { throw new ArgumentOutOfRangeException(nameof(qubit)); }
        _steps.Add(new ResetStep(qubit, new QuantumCircuit(QubitCount).X(qubit)));
        return this;
    }

    internal int Execute(StateVector state, IStateVectorBackend backend, Random random, CancellationToken cancellationToken)
    {
        int classical = 0;
        foreach (Step step in _steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (step)
            {
                case UnitaryStep unitary:
                    if ((classical & unitary.Mask) == unitary.Value) { state.Evolve(unitary.Circuit, backend, cancellationToken); }
                    break;
                case MeasurementStep measurement:
                    int outcome = state.Measure(measurement.Qubits, random, cancellationToken);
                    int written = 0;
                    for (int bit = 0; bit < measurement.ClassicalBits.Length; bit++)
                    {
                        written |= ((outcome >> bit) & 1) << measurement.ClassicalBits[bit];
                    }

                    classical = (classical & ~measurement.WriteMask) | written;
                    break;
                case ResetStep reset:
                    if (state.Measure([reset.Qubit], random, cancellationToken) != 0)
                    {
                        state.Evolve(reset.Correction, backend, cancellationToken);
                    }

                    break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return classical;
    }

    private void ValidateCircuit(QuantumCircuit circuit)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        if (circuit.QubitCount != QubitCount) { throw new ArgumentException("Circuit sizes must match.", nameof(circuit)); }
    }

    private static int ValidateIndices(ReadOnlySpan<int> indices, int count, string parameterName)
    {
        int mask = 0;
        foreach (int index in indices)
        {
            if ((uint)index >= (uint)count) { throw new ArgumentOutOfRangeException(parameterName); }
            int bit = 1 << index;
            if ((mask & bit) != 0) { throw new ArgumentException("Indices must be distinct.", parameterName); }
            mask |= bit;
        }

        return mask;
    }

    private abstract record Step;
    private sealed record UnitaryStep(QuantumCircuit Circuit, int Mask, int Value) : Step;
    private sealed record MeasurementStep(int[] Qubits, int[] ClassicalBits, int WriteMask) : Step;
    private sealed record ResetStep(int Qubit, QuantumCircuit Correction) : Step;
}
