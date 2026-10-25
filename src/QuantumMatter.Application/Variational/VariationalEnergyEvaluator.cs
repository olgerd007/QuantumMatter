using QuantumMatter.Core;
using QuantumMatter.Core.Observables;
using QuantumMatter.Core.Variational;

namespace QuantumMatter.Application.Variational;

/// <summary>Evaluates pure-state Hamiltonian energies and occurrence-wise parameter-shift gradients.</summary>
public sealed class VariationalEnergyEvaluator
{
    private readonly IStateVectorBackend _backend;
    private readonly SimulationOptions _options;

    /// <summary>Injects a caller-owned unitary backend and a positive host-array budget.</summary>
    public VariationalEnergyEvaluator(IStateVectorBackend backend, SimulationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
        _options = options ?? new SimulationOptions();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_options.MemoryBudgetBytes);
    }

    /// <summary>Evaluates one bound circuit from the all-zero state, without sampling or modifying its inputs.</summary>
    public double Evaluate(ParameterizedCircuit circuit, PauliHamiltonian hamiltonian, ReadOnlySpan<double> parameters,
        CancellationToken cancellationToken = default)
    {
        Validate(circuit, hamiltonian, gradients: false, cancellationToken);
        QuantumCircuit bound = circuit.Bind(parameters, cancellationToken);
        var state = new StateVector(circuit.QubitCount, _options.MemoryBudgetBytes);
        return EvaluateBound(bound, state, hamiltonian, cancellationToken);
    }

    /// <summary>Returns energy and gradients using two shifted evaluations per symbolic rotation occurrence.</summary>
    /// <remarks>Shared-parameter contributions are summed termwise; one state, three parameter-sized arrays and one term-sized array are budgeted.</remarks>
    public VariationalEnergyResult EvaluateWithGradient(ParameterizedCircuit circuit, PauliHamiltonian hamiltonian,
        ReadOnlySpan<double> parameters, CancellationToken cancellationToken = default)
    {
        Validate(circuit, hamiltonian, gradients: true, cancellationToken);
        QuantumCircuit bound = circuit.Bind(parameters, cancellationToken);
        double[] values = parameters.ToArray();
        var gradient = new double[circuit.ParameterCount];
        var corrections = new double[circuit.ParameterCount];
        var plusExpectations = new double[hamiltonian.Terms.Count];
        cancellationToken.ThrowIfCancellationRequested();
        var state = new StateVector(circuit.QubitCount, _options.MemoryBudgetBytes);
        double energy = EvaluateBound(bound, state, hamiltonian, cancellationToken);
        for (int rotation = 0; rotation < circuit.RotationParameterIndices.Count; rotation++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int parameter = circuit.RotationParameterIndices[rotation];
            PrepareBound(circuit.BindWithRotationShift(values, rotation, Math.PI / 2, cancellationToken), state, cancellationToken);
            if (hamiltonian.Terms.Count == 0) { state.ExpectationEnergy(hamiltonian, cancellationToken); }
            for (int term = 0; term < plusExpectations.Length; term++)
            {
                plusExpectations[term] = state.ExpectationValue(hamiltonian.Terms[term].Observable, cancellationToken);
            }

            PrepareBound(circuit.BindWithRotationShift(values, rotation, -Math.PI / 2, cancellationToken), state, cancellationToken);
            if (hamiltonian.Terms.Count == 0) { state.ExpectationEnergy(hamiltonian, cancellationToken); }
            for (int term = 0; term < plusExpectations.Length; term++)
            {
                PauliTerm observable = hamiltonian.Terms[term];
                double minus = state.ExpectationValue(observable.Observable, cancellationToken);
                // Difference each observable before weighting: a large identity offset must not erase its gradient.
                double contribution = observable.Coefficient * (0.5 * plusExpectations[term] - 0.5 * minus);
                double previous = gradient[parameter];
                double next = previous + contribution;
                corrections[parameter] += Math.Abs(previous) >= Math.Abs(contribution)
                    ? (previous - next) + contribution : (contribution - next) + previous;
                gradient[parameter] = next;
                if (!double.IsFinite(next) || !double.IsFinite(corrections[parameter]))
                {
                    throw new InvalidOperationException("The gradient accumulation is nonfinite.");
                }
            }
        }

        for (int parameter = 0; parameter < gradient.Length; parameter++)
        {
            if ((parameter & 4095) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
            gradient[parameter] += corrections[parameter];
            if (!double.IsFinite(gradient[parameter])) { throw new InvalidOperationException("The gradient is nonfinite."); }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new VariationalEnergyResult(energy, gradient);
    }

    private double EvaluateBound(QuantumCircuit circuit, StateVector state, PauliHamiltonian hamiltonian, CancellationToken cancellationToken)
    {
        PrepareBound(circuit, state, cancellationToken);
        return state.ExpectationEnergy(hamiltonian, cancellationToken);
    }

    private void PrepareBound(QuantumCircuit circuit, StateVector state, CancellationToken cancellationToken)
    {
        state.Reset(cancellationToken);
        state.Evolve(circuit, _backend, cancellationToken);
    }

    private void Validate(ParameterizedCircuit circuit, PauliHamiltonian hamiltonian, bool gradients, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(hamiltonian);
        if (circuit.QubitCount != hamiltonian.QubitCount) { throw new ArgumentException("Circuit and Hamiltonian sizes must match.", nameof(hamiltonian)); }
        cancellationToken.ThrowIfCancellationRequested();
        long required = StateVector.RequiredBytes(circuit.QubitCount)
            + (gradients ? 24L * circuit.ParameterCount + 8L * hamiltonian.Terms.Count : 0);
        if (required > _options.MemoryBudgetBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(circuit), $"Execution requires {required} bytes; the budget is {_options.MemoryBudgetBytes} bytes.");
        }
    }
}
