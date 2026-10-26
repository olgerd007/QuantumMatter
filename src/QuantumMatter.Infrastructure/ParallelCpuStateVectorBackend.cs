using System.Numerics;
using QuantumMatter.Core;

namespace QuantumMatter.Infrastructure;

/// <summary>Bounded synchronous data parallelism with optional state-tile reuse across adjacent gates.</summary>
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

    /// <summary>Whether portable vectors and the Complex memory layout support at least the SIMD Hadamard path.</summary>
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
        if (!parallel && !_options.EnableSimd && !_options.EnableGateBlocking)
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
        var execution = parallel ? new ParallelGateExecution(amplitudes, _options.EnableSimd, _options.EnableAvx512, scheduling) : null;
        // Constant-size metadata only; no amplitude scratch or circuit-sized preparation array.
        var blockedGates = _options.EnableGateBlocking ? new PreparedStateVectorGate[32] : null;
        for (int operationIndex = 0; operationIndex < circuit.Operations.Count; operationIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PreparedStateVectorGate gate = PreparedStateVectorGate.Prepare(circuit.Operations[operationIndex]);
            if (gate.Kernel == StateVectorKernelKind.Identity)
            {
                continue;
            }

            if (blockedGates is not null && CanBlock(gate))
            {
                blockedGates[0] = gate;
                int count = 1;
                while (count < blockedGates.Length && operationIndex + count < circuit.Operations.Count)
                {
                    PreparedStateVectorGate next = PreparedStateVectorGate.Prepare(circuit.Operations[operationIndex + count]);
                    if (next.Target != gate.Target || !CanBlock(next)) { break; }
                    blockedGates[count++] = next;
                }

                if (count > 1)
                {
                    int pairs = amplitudes.Length / 2;
                    int blockWorkers = parallel ? Math.Min(pairs, _options.MaxDegreeOfParallelism) : 1;
                    if (blockWorkers == 1)
                    {
                        ApplyBlockedRange(amplitudes, blockedGates, count, 0, pairs, _options.EnableSimd, _options.EnableAvx512, cancellationToken);
                    }
                    else
                    {
                        execution!.ApplyBlocked(blockedGates, count, pairs, blockWorkers);
                    }

                    operationIndex += count - 1;
                    continue;
                }
            }

            int workItems = gate.Kernel == StateVectorKernelKind.Diagonal ? amplitudes.Length : amplitudes.Length / 2;
            int workers = parallel ? Math.Min(workItems, _options.MaxDegreeOfParallelism) : 1;
            if (workers == 1)
            {
                ApplyRange(amplitudes, gate, 0, workItems, _options.EnableSimd, _options.EnableAvx512, cancellationToken);
            }
            else
            {
                execution!.Apply(gate, workItems, workers);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static bool CanBlock(PreparedStateVectorGate gate) => gate.ControlMask == 0
        && gate.Kernel is StateVectorKernelKind.Matrix or StateVectorKernelKind.Hadamard
            or StateVectorKernelKind.Exchange or StateVectorKernelKind.Diagonal;

    private static void ApplyBlockedRange(Memory<Complex> amplitudes, PreparedStateVectorGate[] gates, int count,
        int start, int end, bool enableSimd, bool enableAvx512, CancellationToken cancellationToken)
    {
        int bit = 1 << gates[0].Target;
        // Pack distant partners into a small two-half workspace. Reuse is local to a worker;
        // the full register is never transposed and no pooled/caller buffers are retained.
        Span<Complex> scratch = bit > 512 ? stackalloc Complex[1024] : Span<Complex>.Empty;
        Span<Complex> state = amplitudes.Span;
        for (int pair = start; pair < end;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int low = pair & (bit - 1);
            int first = ((pair - low) << 1) | low;
            int run = Math.Min(512, end - pair);
            // Several complete small-target blocks form one contiguous amplitude tile.
            bool contiguous = low == 0 && bit <= run;
            if (contiguous) { run -= run % bit; }
            else { run = Math.Min(run, bit - low); }

            if (!scratch.IsEmpty)
            {
                // Power-of-two strips retain the target-bit model inside the tile. Bulk copies
                // avoid alternating distant source/destination streams for every complex value.
                int localTarget = BitOperations.Log2((uint)run);
                run = 1 << localTarget;
                int second = first | bit;
                Span<Complex> tile = scratch[..(2 * run)];
                state.Slice(first, run).CopyTo(tile);
                state.Slice(second, run).CopyTo(tile[run..]);

                for (int gateIndex = 0; gateIndex < count; gateIndex++)
                {
                    PreparedStateVectorGate local = gates[gateIndex] with { Target = localTarget };
                    int workItems = local.Kernel == StateVectorKernelKind.Diagonal ? tile.Length : run;
                    ApplyRange(tile, local, 0, workItems, enableSimd, enableAvx512, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                tile[..run].CopyTo(state[first..]);
                tile[run..].CopyTo(state[second..]);

                pair += run;
                continue;
            }

            // All gates have the same pair ownership. Preserve their original arithmetic order
            // within each independent tile; no matrix multiplication or coefficient fusion occurs.
            for (int gateIndex = 0; gateIndex < count; gateIndex++)
            {
                PreparedStateVectorGate gate = gates[gateIndex];
                if (gate.Kernel != StateVectorKernelKind.Diagonal)
                {
                    ApplyRange(amplitudes, gate, pair, pair + run, enableSimd, enableAvx512, cancellationToken);
                }
                else if (contiguous)
                {
                    ApplyRange(amplitudes, gate, first, first + 2 * run, enableSimd, enableAvx512, cancellationToken);
                }
                else
                {
                    ApplyRange(amplitudes, gate, first, first + run, enableSimd, enableAvx512, cancellationToken);
                    int second = first | bit;
                    ApplyRange(amplitudes, gate, second, second + run, enableSimd, enableAvx512, cancellationToken);
                }
            }

            pair += run;
        }
    }

    private sealed class ParallelGateExecution
    {
        private readonly Memory<Complex> _amplitudes;
        private readonly bool _enableSimd;
        private readonly bool _enableAvx512;
        private readonly ParallelOptions _scheduling;
        private readonly Action<int> _applyWorker;
        private PreparedStateVectorGate _gate;
        private int _workItems;
        private int _workers;
        private PreparedStateVectorGate[]? _blockedGates;
        private int _blockedCount;

        internal ParallelGateExecution(Memory<Complex> amplitudes, bool enableSimd, bool enableAvx512, ParallelOptions scheduling)
        {
            _amplitudes = amplitudes;
            _enableSimd = enableSimd;
            _enableAvx512 = enableAvx512;
            _scheduling = scheduling;
            _applyWorker = ApplyWorker;
        }

        internal void Apply(PreparedStateVectorGate gate, int workItems, int workers)
        {
            _blockedGates = null;
            _gate = gate;
            _workItems = workItems;
            _workers = workers;
            // The join completes all readers before the next gate updates these per-execution fields.
            Parallel.For(0, workers, _scheduling, _applyWorker);
        }

        internal void ApplyBlocked(PreparedStateVectorGate[] gates, int count, int workItems, int workers)
        {
            _blockedGates = gates;
            _blockedCount = count;
            _workItems = workItems;
            _workers = workers;
            Parallel.For(0, workers, _scheduling, _applyWorker);
        }

        private void ApplyWorker(int worker)
        {
            int start = (int)((long)_workItems * worker / _workers);
            int end = (int)((long)_workItems * (worker + 1) / _workers);
            if (_blockedGates is null)
            {
                ApplyRange(_amplitudes, _gate, start, end, _enableSimd, _enableAvx512, _scheduling.CancellationToken);
            }
            else
            {
                ApplyBlockedRange(_amplitudes, _blockedGates, _blockedCount, start, end, _enableSimd, _enableAvx512, _scheduling.CancellationToken);
            }
        }
    }

    private static void ApplyRange(Memory<Complex> amplitudes, PreparedStateVectorGate gate, int start, int end,
        bool enableSimd, bool enableAvx512, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplyRange(amplitudes.Span, gate, start, end, enableSimd, enableAvx512, cancellationToken);
    }

    private static void ApplyRange(Span<Complex> state, PreparedStateVectorGate gate, int start, int end,
        bool enableSimd, bool enableAvx512, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int bit = 1 << gate.Target;
        if (enableSimd && enableAvx512 && gate.ControlMask == 0 && bit >= 4 && Avx512StateVectorKernel.IsSupported
            && gate.Kernel is StateVectorKernelKind.Matrix or StateVectorKernelKind.Hadamard
                or StateVectorKernelKind.Exchange or StateVectorKernelKind.Diagonal)
        {
            Avx512StateVectorKernel.Apply(state, gate, start, end, cancellationToken);
            return;
        }

        if (enableSimd && gate.ControlMask == 0 && AvxStateVectorKernel.IsSupported
            && gate.Kernel is StateVectorKernelKind.Matrix or StateVectorKernelKind.Exchange or StateVectorKernelKind.Diagonal
                or StateVectorKernelKind.Hadamard)
        {
            // Retain the portable high-stride H kernel, which avoids complex coefficient work.
            if (gate.Kernel != StateVectorKernelKind.Hadamard || bit == 1)
            {
                AvxStateVectorKernel.Apply(state, gate, start, end, cancellationToken);
                return;
            }
        }

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
