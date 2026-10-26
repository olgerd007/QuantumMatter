using QuantumMatter.Core;
using QuantumMatter.Core.Observables;
using QuantumMatter.Core.Variational;

namespace QuantumMatter.Application.Variational;

/// <summary>Deterministic pure-state VQE with steepest descent and bounded Armijo backtracking.</summary>
public sealed class VqeSolver
{
    private readonly IStateVectorBackend _backend;
    private readonly VqeOptions _options;
    private readonly SimulationOptions _simulationOptions;

    /// <summary>Injects a caller-owned backend and immutable optimization/allocation policies.</summary>
    public VqeSolver(IStateVectorBackend backend, VqeOptions? options = null, SimulationOptions? simulationOptions = null)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
        _options = options ?? new VqeOptions();
        _options.Validate();
        _simulationOptions = simulationOptions ?? new SimulationOptions();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_simulationOptions.MemoryBudgetBytes);
    }

    /// <summary>Searches from an exact-width finite parameter snapshot, returning the last accepted iterate.</summary>
    /// <remarks>Stationarity is not a global-ground-state guarantee. Cancellation and backend/numerical failures propagate without a partial result.</remarks>
    public VqeResult Solve(ParameterizedCircuit circuit, PauliHamiltonian hamiltonian, ReadOnlySpan<double> initialParameters,
        CancellationToken cancellationToken = default)
    {
        Validate(circuit, hamiltonian, initialParameters, cancellationToken);
        // Two solver arrays and the previous immutable gradient may overlap the next evaluator call.
        long reservedBytes = 24L * circuit.ParameterCount;
        var evaluator = new VariationalEnergyEvaluator(_backend,
            _simulationOptions with { MemoryBudgetBytes = _simulationOptions.MemoryBudgetBytes - reservedBytes });
        double[] parameters = initialParameters.ToArray();
        var trial = new double[parameters.Length];
        double energy = evaluator.Evaluate(circuit, hamiltonian, parameters, cancellationToken);
        long energies = 1, circuits = 1;
        int iterations = 0, gradients = 0;
        long gradientCost = 1 + 2L * circuit.RotationParameterIndices.Count;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (iterations == _options.MaximumIterations) { return Finish(VqeTerminationReason.IterationLimitReached); }
            if (_options.MaximumCircuitEvaluations - circuits < gradientCost) { return Finish(VqeTerminationReason.EvaluationLimitReached); }
            VariationalEnergyResult evaluation = evaluator.EvaluateWithGradient(circuit, hamiltonian, parameters, cancellationToken);
            gradients++;
            circuits += gradientCost;
            energy = evaluation.Energy;
            double norm = 0;
            for (int index = 0; index < parameters.Length; index++)
            {
                if ((index & 4095) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
                norm = Math.Max(norm, Math.Abs(evaluation.Gradient[index]));
            }
            if (norm <= _options.GradientTolerance) { return Finish(VqeTerminationReason.GradientToleranceReached); }

            // Scaling before squaring retains finite gradients whose squares overflow or underflow.
            double scaledSquares = 0;
            for (int index = 0; index < parameters.Length; index++)
            {
                if ((index & 4095) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
                double scaled = evaluation.Gradient[index] / norm;
                scaledSquares += scaled * scaled;
            }
            double step = _options.InitialStepSize;
            bool accepted = false;
            for (int search = 0; search < _options.MaximumLineSearchIterations; search++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (circuits == _options.MaximumCircuitEvaluations) { return Finish(VqeTerminationReason.EvaluationLimitReached); }
                bool finite = true, changed = false;
                for (int index = 0; index < parameters.Length; index++)
                {
                    if ((index & 4095) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
                    trial[index] = Math.FusedMultiplyAdd(-step, evaluation.Gradient[index], parameters[index]);
                    finite &= double.IsFinite(trial[index]);
                    changed |= trial[index] != parameters[index];
                }
                if (!changed) { return Finish(VqeTerminationReason.LineSearchStalled); }
                if (finite)
                {
                    double candidateEnergy = evaluator.Evaluate(circuit, hamiltonian, trial, cancellationToken);
                    energies++;
                    circuits++;
                    if (SufficientDecrease(energy, candidateEnergy, step, norm, scaledSquares))
                    {
                        (parameters, trial) = (trial, parameters);
                        energy = candidateEnergy;
                        iterations++;
                        accepted = true;
                        break;
                    }
                }
                double smaller = step * _options.BacktrackingFactor;
                if (smaller < _options.MinimumStepSize || smaller >= step) { break; }
                step = smaller;
            }
            if (!accepted) { return Finish(VqeTerminationReason.LineSearchStalled); }
        }

        VqeResult Finish(VqeTerminationReason reason)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new VqeResult(parameters, energy, iterations, energies, gradients, circuits, reason);
        }
    }

    private bool SufficientDecrease(double energy, double candidate, double step, double norm, double scaledSquares)
    {
        if (candidate >= energy) { return false; }
        double scale = Math.Max(Math.Abs(energy), Math.Abs(candidate));
        // Compare E-Etrial >= c*step*||g||^2 in log space to avoid overflowing either side.
        double logDecrease = Math.Log(scale) + Math.Log(energy / scale - candidate / scale);
        double logRequired = Math.Log(_options.SufficientDecrease) + Math.Log(step) + 2 * Math.Log(norm) + Math.Log(scaledSquares);
        return logDecrease >= logRequired;
    }

    private void Validate(ParameterizedCircuit circuit, PauliHamiltonian hamiltonian, ReadOnlySpan<double> initialParameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(hamiltonian);
        if (circuit.QubitCount != hamiltonian.QubitCount) { throw new ArgumentException("Circuit and Hamiltonian sizes must match.", nameof(hamiltonian)); }
        if (initialParameters.Length != circuit.ParameterCount) { throw new ArgumentException("Parameter count must match the circuit.", nameof(initialParameters)); }
        cancellationToken.ThrowIfCancellationRequested();
        long required = StateVector.RequiredBytes(circuit.QubitCount) + 48L * circuit.ParameterCount + 8L * hamiltonian.Terms.Count;
        if (required > _simulationOptions.MemoryBudgetBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(circuit), $"VQE requires {required} host-array bytes; the budget is {_simulationOptions.MemoryBudgetBytes} bytes.");
        }
        for (int index = 0; index < initialParameters.Length; index++)
        {
            if ((index & 4095) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
            if (!double.IsFinite(initialParameters[index])) { throw new ArgumentOutOfRangeException(nameof(initialParameters), "Parameters must be finite."); }
        }
    }
}
