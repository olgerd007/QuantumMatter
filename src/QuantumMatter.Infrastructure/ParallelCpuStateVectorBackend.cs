using System.Numerics;
using QuantumMatter.Core;

namespace QuantumMatter.Infrastructure;

/// <summary>Bounded synchronous data parallelism with a barrier between circuit operations.</summary>
public sealed class ParallelCpuStateVectorBackend : IStateVectorBackend
{
    private readonly ParallelCpuOptions _options;
    private readonly CpuStateVectorBackend _scalar = new();

    /// <summary>Creates a reusable backend, validating worker and threshold options.</summary>
    public ParallelCpuStateVectorBackend(ParallelCpuOptions? options = null)
    {
        _options = options ?? new ParallelCpuOptions();
        if (_options.MaxDegreeOfParallelism is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Worker limit must be between 1 and 256.");
        }

        if (_options.MinimumParallelQubits is < 1 or > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Parallel threshold must be between 1 and 30 qubits.");
        }
    }

    /// <summary>Whether portable vectors and the Complex memory layout support the SIMD path.</summary>
    public static bool IsSimdSupported => SimdHadamardKernel.IsSupported;

    /// <inheritdoc />
    public void Execute(QuantumCircuit circuit, Memory<Complex> amplitudes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        if (amplitudes.Length != 1 << circuit.QubitCount)
        {
            throw new ArgumentException("Buffer length must equal 2^qubitCount.", nameof(amplitudes));
        }

        cancellationToken.ThrowIfCancellationRequested();
        bool parallel = circuit.QubitCount >= _options.MinimumParallelQubits && _options.MaxDegreeOfParallelism > 1;
        if (!parallel && !_options.EnableSimd)
        {
            _scalar.Execute(circuit, amplitudes, cancellationToken);
            return;
        }

        var scheduling = new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.MaxDegreeOfParallelism,
            CancellationToken = cancellationToken,
            TaskScheduler = TaskScheduler.Default
        };
        foreach (GateOperation operation in circuit.Operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PreparedStateVectorGate gate = PreparedStateVectorGate.Prepare(operation);
            if (gate.Kernel == StateVectorKernelKind.Identity)
            {
                continue;
            }

            int workItems = gate.Kernel == StateVectorKernelKind.Diagonal ? amplitudes.Length : amplitudes.Length / 2;
            int workers = parallel ? Math.Min(workItems, _options.MaxDegreeOfParallelism) : 1;
            if (workers == 1)
            {
                ApplyRange(amplitudes, gate, 0, workItems, _options.EnableSimd, cancellationToken);
            }
            else
            {
                // Every pair has exactly one owner; Parallel.For joins before the next gate starts.
                Parallel.For(0, workers, scheduling, worker =>
                {
                    int start = (int)((long)workItems * worker / workers);
                    int end = (int)((long)workItems * (worker + 1) / workers);
                    ApplyRange(amplitudes, gate, start, end, _options.EnableSimd, cancellationToken);
                });
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void ApplyRange(Memory<Complex> amplitudes, PreparedStateVectorGate gate, int start, int end,
        bool enableSimd, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Span<Complex> state = amplitudes.Span;
        int bit = 1 << gate.Target;
        if (gate.Kernel == StateVectorKernelKind.Hadamard && gate.ControlMask == 0 && enableSimd
            && SimdHadamardKernel.IsSupported && bit >= Vector<double>.Count / 2)
        {
            SimdHadamardKernel.Apply(state, gate.Target, start, end, cancellationToken);
            return;
        }

        if (gate.Kernel == StateVectorKernelKind.Diagonal)
        {
            for (int index = start; index < end; index++)
            {
                if ((index & 4095) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if ((index & gate.ControlMask) == gate.ControlMask)
                {
                    state[index] *= (index & bit) == 0 ? gate.A : gate.D;
                }
            }

            return;
        }

        int swapControl = gate.Kernel == StateVectorKernelKind.Swap ? 1 << gate.SwapControl : 0;
        for (int pair = start; pair < end; pair++)
        {
            if ((pair & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            int low = pair & (bit - 1);
            int first = ((pair - low) << 1) | low;
            if ((first & gate.ControlMask) != gate.ControlMask)
            {
                continue;
            }

            int second = first | bit;
            if (gate.Kernel == StateVectorKernelKind.Swap)
            {
                if ((first & swapControl) == 0)
                {
                    continue;
                }

                second ^= swapControl;
                (state[first], state[second]) = (state[second], state[first]);
            }
            else if (gate.Kernel == StateVectorKernelKind.Exchange)
            {
                (state[first], state[second]) = (state[second], state[first]);
            }
            else
            {
                Complex x = state[first];
                Complex y = state[second];
                state[first] = gate.A * x + gate.B * y;
                state[second] = gate.C * x + gate.D * y;
            }
        }
    }
}
