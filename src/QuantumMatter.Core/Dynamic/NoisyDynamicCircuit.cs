using QuantumMatter.Core.Noise;

namespace QuantumMatter.Core.Dynamic;

/// <summary>A finite dynamic program with explicit single-qubit noise placements between instructions.</summary>
/// <remarks>Mutable and not thread-safe. Each trajectory starts with zero classical memory.</remarks>
public sealed class NoisyDynamicCircuit
{
    private readonly List<Step> _steps = [];
    private DynamicCircuit _pending;
    private int _stepCount;

    /// <summary>Creates a program with 1–30 qubits and 1–30 classical bits without allocating a state.</summary>
    public NoisyDynamicCircuit(int qubits, int classicalBits)
    {
        _pending = new DynamicCircuit(qubits, classicalBits);
        QubitCount = qubits;
        ClassicalBitCount = classicalBits;
    }

    /// <summary>The fixed quantum register width.</summary>
    public int QubitCount { get; }
    /// <summary>The fixed classical register width; bit zero is least significant.</summary>
    public int ClassicalBitCount { get; }
    /// <summary>The number of explicit instructions, including channel placements.</summary>
    public int StepCount => _stepCount;

    /// <summary>Snapshots a size-compatible unitary segment for unconditional execution.</summary>
    public NoisyDynamicCircuit Append(QuantumCircuit circuit)
    {
        _pending.Append(circuit);
        _stepCount++;
        return this;
    }

    /// <summary>Snapshots a unitary segment executed when (classicalRegister AND classicalMask) equals expectedValue.</summary>
    /// <remarks>The mask is nonzero and in range; expectedValue sets only masked bits.</remarks>
    public NoisyDynamicCircuit If(int classicalMask, int expectedValue, QuantumCircuit circuit)
    {
        _pending.If(classicalMask, expectedValue, circuit);
        _stepCount++;
        return this;
    }

    /// <summary>Measures an ordered qubit subset and overwrites its mapped classical destination bits.</summary>
    /// <remarks>Selections are nonempty, equally sized, distinct within each selection, and copied now.</remarks>
    public NoisyDynamicCircuit Measure(ReadOnlySpan<int> qubits, ReadOnlySpan<int> classicalBits)
    {
        _pending.Measure(qubits, classicalBits);
        _stepCount++;
        return this;
    }

    /// <summary>Resets one qubit through measurement and conditional X without writing classical bits.</summary>
    public NoisyDynamicCircuit Reset(int qubit)
    {
        _pending.Reset(qubit);
        _stepCount++;
        return this;
    }

    /// <summary>Places one validated noise channel on an in-range target at this exact program position.</summary>
    /// <remarks>Placement is unconditional and does not modify classical memory. Zero strength consumes no draw or backend call.</remarks>
    public NoisyDynamicCircuit ApplyNoise(NoiseChannel channel, int target)
    {
        // Validate before committing the pending segment so a rejected instruction changes nothing.
        var noise = new NoisyCircuit(QubitCount).ApplyNoise(channel, target);
        if (_pending.StepCount != 0)
        {
            _steps.Add(new Step(_pending, null));
            _pending = new DynamicCircuit(QubitCount, ClassicalBitCount);
        }

        _steps.Add(new Step(null, noise));
        _stepCount++;
        return this;
    }

    internal int Execute(StateVector state, IStateVectorBackend unitaryBackend, INoiseChannelBackend noiseBackend,
        Random random, CancellationToken cancellationToken)
    {
        int classical = 0;
        foreach (Step step in _steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (step.Dynamic is not null)
            {
                classical = step.Dynamic.Execute(state, unitaryBackend, random, cancellationToken, classical);
            }
            else
            {
                state.EvolveTrajectory(step.Noise!, unitaryBackend, noiseBackend, random, cancellationToken);
            }
        }

        return _pending.Execute(state, unitaryBackend, random, cancellationToken, classical);
    }

    private sealed record Step(DynamicCircuit? Dynamic, NoisyCircuit? Noise);
}
