using System.Numerics;

namespace QuantumMatter.Core.Noise;

/// <summary>Ordered snapshots of unitary segments and explicit single-qubit noise placements.</summary>
public sealed class NoisyCircuit
{
    private readonly List<Step> _steps = [];

    /// <summary>Creates an empty noisy circuit for 1 through 30 qubits without allocating a state.</summary>
    public NoisyCircuit(int qubits)
    {
        StateVector.RequiredBytes(qubits);
        QubitCount = qubits;
    }

    /// <summary>The fixed register size shared by all segments.</summary>
    public int QubitCount { get; }
    /// <summary>The number of appended segments and channel placements.</summary>
    public int StepCount => _steps.Count;

    /// <summary>Copies this segment now; subsequent changes to the source circuit have no effect.</summary>
    public NoisyCircuit Append(QuantumCircuit circuit)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        if (circuit.QubitCount != QubitCount)
        {
            throw new ArgumentException("Circuit sizes must match.", nameof(circuit));
        }

        _steps.Add(new Step(circuit.Copy(), null, -1));
        return this;
    }

    /// <summary>Places one validated channel on an in-range target after the preceding steps.</summary>
    public NoisyCircuit ApplyNoise(NoiseChannel channel, int target)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if ((uint)target >= (uint)QubitCount)
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }

        _steps.Add(new Step(null, channel, target));
        return this;
    }

    internal void Execute(Memory<Complex> amplitudes, IStateVectorBackend unitaryBackend,
        INoiseChannelBackend noiseBackend, Random random, CancellationToken cancellationToken)
    {
        foreach (Step step in _steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (step.Circuit is not null)
            {
                unitaryBackend.Execute(step.Circuit, amplitudes, cancellationToken);
            }
            else if (step.Channel!.Probability != 0)
            {
                noiseBackend.Apply(step.Channel, step.Target, amplitudes, random, cancellationToken);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private sealed record Step(QuantumCircuit? Circuit, NoiseChannel? Channel, int Target);
}
