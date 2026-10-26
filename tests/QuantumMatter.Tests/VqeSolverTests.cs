using System.Numerics;
using QuantumMatter.Application;
using QuantumMatter.Application.Variational;
using QuantumMatter.Core;
using QuantumMatter.Core.Observables;
using QuantumMatter.Core.Variational;
using QuantumMatter.Infrastructure;
using Xunit;

namespace QuantumMatter.Tests;

public sealed class VqeSolverTests
{
    private static ParameterizedCircuit Single() => new ParameterizedCircuit(1, 1).Ry(0, 0);
    private static PauliHamiltonian Z(double coefficient = 1) => new(1, [new(coefficient, new PauliString("Z"))]);

    [Theory]
    [InlineData("scalar")]
    [InlineData("parallel")]
    [InlineData("simd")]
#if CUDA_TESTS
    [InlineData("cuda")]
#endif
    public void One_qubit_search_matches_analytic_eigenvalue_and_independent_returned_parameter_energy(string name)
    {
        IStateVectorBackend backend = NoisyDynamicCircuitTests.Backend(name);
        using IDisposable? owned = backend as IDisposable;
        var h = new PauliHamiltonian(1, [new(-0.2, new PauliString("I")), new(0.7, new PauliString("Z")), new(0.3, new PauliString("X"))]);
        var result = new VqeSolver(backend).Solve(Single(), h, [0.73]);
        Near(-0.2 - Math.Sqrt(0.58), result.Energy, 2e-11);
        double theta = result.Parameters[0];
        Near(-0.2 + 0.7 * Math.Cos(theta) + 0.3 * Math.Sin(theta), result.Energy);
        Assert.InRange(Math.Abs(-0.7 * Math.Sin(theta) + 0.3 * Math.Cos(theta)), 0, 1.1e-6);
        Assert.Equal(VqeTerminationReason.GradientToleranceReached, result.TerminationReason);
        Accounting(result, 1);
    }

    [Theory]
    [InlineData("scalar")]
    [InlineData("parallel")]
    [InlineData("simd")]
#if CUDA_TESTS
    [InlineData("cuda")]
#endif
    public void Entangled_two_qubit_search_matches_independent_dense_Heisenberg_eigenpair(string name)
    {
        IStateVectorBackend backend = NoisyDynamicCircuitTests.Backend(name);
        using IDisposable? owned = backend as IDisposable;
        var h = new PauliHamiltonian(2, [new(1, new PauliString("XX")), new(1, new PauliString("YY")), new(1, new PauliString("ZZ"))]);
        var template = new ParameterizedCircuit(2, 1).Append(new QuantumCircuit(2).X(0))
            .Ry(1, 0).Append(new QuantumCircuit(2).CNot(1, 0));
        var result = new VqeSolver(backend).Solve(template, h, [0.2]);
        // Independent 4x4 Hamiltonian: the |01>,|10> block has eigenvalues -3 and 1.
        double[,] matrix = { { 1, 0, 0, 0 }, { 0, -1, 2, 0 }, { 0, 2, -1, 0 }, { 0, 0, 0, 1 } };
        double theta = result.Parameters[0];
        double[] state = [0, Math.Cos(theta / 2), Math.Sin(theta / 2), 0];
        double energy = 0, residual = 0;
        for (int row = 0; row < 4; row++)
        {
            double product = 0;
            for (int column = 0; column < 4; column++) { product += matrix[row, column] * state[column]; }
            energy += state[row] * product;
            residual += Math.Pow(product + 3 * state[row], 2);
        }
        Near(-3, result.Energy, 2e-11);
        Near(energy, result.Energy);
        Assert.InRange(Math.Sqrt(residual), 0, 1.1e-6);
        Assert.Equal(VqeTerminationReason.GradientToleranceReached, result.TerminationReason);
        Accounting(result, 1);
    }

    [Fact]
    public void Backtracking_rejects_insufficient_decrease_and_counts_every_actual_execution()
    {
        var backend = new RecordingBackend();
        var options = new VqeOptions { MaximumIterations = 1, InitialStepSize = 20, SufficientDecrease = 0.1 };
        var result = new VqeSolver(backend, options).Solve(Single(), Z(), [1]);
        double gradient = -Math.Sin(1);
        double firstTrial = Math.FusedMultiplyAdd(-20, gradient, 1);
        double acceptedTrial = Math.FusedMultiplyAdd(-10, gradient, 1);
        Assert.True(Math.Cos(firstTrial) > Math.Cos(1) - 0.1 * 20 * gradient * gradient);
        Assert.True(Math.Cos(acceptedTrial) <= Math.Cos(1) - 0.1 * 10 * gradient * gradient);
        Near(acceptedTrial, result.Parameters[0]);
        Near(Math.Cos(acceptedTrial), result.Energy);
        Assert.Equal(1, result.IterationCount);
        Assert.Equal(3, result.EnergyEvaluationCount);
        Assert.Equal(1, result.GradientEvaluationCount);
        Assert.Equal(6, result.CircuitEvaluationCount);
        Assert.Equal(result.CircuitEvaluationCount, backend.Calls);
        Assert.Equal(VqeTerminationReason.IterationLimitReached, result.TerminationReason);
    }

    [Theory]
    [InlineData(1, 1, 0, 0)]
    [InlineData(3, 1, 0, 0)]
    [InlineData(4, 4, 1, 0)]
    [InlineData(5, 5, 1, 1)]
    [InlineData(7, 5, 1, 1)]
    [InlineData(8, 8, 2, 1)]
    public void Execution_limit_reserves_complete_shift_gradients_without_partial_evaluations(long budget, long calls, int gradients, int iterations)
    {
        var backend = new RecordingBackend();
        var result = new VqeSolver(backend, new VqeOptions { MaximumCircuitEvaluations = budget }).Solve(Single(), Z(), [0.7]);
        Assert.Equal(VqeTerminationReason.EvaluationLimitReached, result.TerminationReason);
        Assert.Equal(calls, backend.Calls);
        Assert.Equal(calls, result.CircuitEvaluationCount);
        Assert.Equal(gradients, result.GradientEvaluationCount);
        Assert.Equal(iterations, result.IterationCount);
        Near(Math.Cos(result.Parameters[0]), result.Energy);
        Accounting(result, 1);
    }

    [Fact]
    public void Iteration_limits_include_zero_and_do_not_claim_an_unchecked_final_gradient()
    {
        var backend = new RecordingBackend();
        var result = new VqeSolver(backend, new VqeOptions { MaximumIterations = 0, MaximumCircuitEvaluations = long.MaxValue })
            .Solve(Single(), Z(), [0.7]);
        Assert.Equal(VqeTerminationReason.IterationLimitReached, result.TerminationReason);
        Assert.Equal(0, result.IterationCount);
        Assert.Equal(0, result.GradientEvaluationCount);
        Assert.Equal(1, backend.Calls);
        Near(Math.Cos(0.7), result.Energy);
        result = new VqeSolver(new CpuStateVectorBackend(), new VqeOptions { MaximumIterations = 2 }).Solve(Single(), Z(), [0.7]);
        Assert.Equal(2, result.IterationCount);
        Assert.Equal(VqeTerminationReason.IterationLimitReached, result.TerminationReason);
        Assert.Equal(9, result.CircuitEvaluationCount);
    }

    [Fact]
    public void Stationarity_includes_maxima_constant_circuits_and_zero_operators_without_a_ground_state_claim()
    {
        var solver = new VqeSolver(new CpuStateVectorBackend());
        var maximum = solver.Solve(Single(), Z(), [0]);
        Assert.Equal(VqeTerminationReason.GradientToleranceReached, maximum.TerminationReason);
        Assert.Equal(1, maximum.Energy); // A stationary maximum, not the ground energy -1.
        Assert.Equal(0, maximum.IterationCount);
        var constant = solver.Solve(new ParameterizedCircuit(1, 0).Append(new QuantumCircuit(1).X(0)), Z(), []);
        Assert.Equal(-1, constant.Energy);
        Assert.Empty(constant.Parameters);
        Assert.Equal(2, constant.CircuitEvaluationCount);
        Assert.Equal(VqeTerminationReason.GradientToleranceReached, constant.TerminationReason);
        var zero = solver.Solve(Single(), new PauliHamiltonian(1, []), [0.7]);
        Assert.Equal(0, zero.Energy);
        Assert.Equal(VqeTerminationReason.GradientToleranceReached, zero.TerminationReason);
    }

    [Fact]
    public void Shared_parameters_unused_indices_and_owned_results_preserve_existing_variational_contracts()
    {
        var backend = new RecordingBackend();
        var solver = new VqeSolver(backend, new VqeOptions { InitialStepSize = 0.25 });
        var template = new ParameterizedCircuit(1, 2).Ry(0, 0).Ry(0, 0);
        double[] initial = [0.31, 17];
        var result = solver.Solve(template, Z(), initial);
        Assert.Equal(new[] { 0.31, 17 }, initial);
        Assert.Equal(17, result.Parameters[1]);
        Near(-1, result.Energy, 2e-11);
        Near(Math.Cos(2 * result.Parameters[0]), result.Energy);
        Assert.Equal(VqeTerminationReason.GradientToleranceReached, result.TerminationReason);
        Accounting(result, 2);
        Assert.Equal(result.CircuitEvaluationCount, backend.Calls);
        Assert.Throws<NotSupportedException>(() => ((IList<double>)result.Parameters)[0] = 9);
        double saved = result.Parameters[0];
        initial[0] = -8;
        solver.Solve(template, Z(), initial);
        Assert.Equal(saved, result.Parameters[0]);
        Assert.Equal(2, template.OperationCount);
    }

    [Fact]
    public void A_rejected_lower_energy_trial_does_not_replace_the_accepted_iterate()
    {
        var result = new VqeSolver(new CpuStateVectorBackend(), new VqeOptions
        { MaximumLineSearchIterations = 1, InitialStepSize = 5, SufficientDecrease = 0.9 }).Solve(Single(), Z(), [1]);
        Assert.True(Math.Cos(1 + 5 * Math.Sin(1)) < Math.Cos(1));
        Assert.Equal(VqeTerminationReason.LineSearchStalled, result.TerminationReason);
        Assert.Equal(0, result.IterationCount);
        Assert.Equal(1, result.Parameters[0]);
        Near(Math.Cos(1), result.Energy);
        Assert.Equal(5, result.CircuitEvaluationCount);
    }

    [Fact]
    public void Minimum_step_and_unrepresentable_parameters_or_energies_stop_as_stalled()
    {
        var minimum = new VqeSolver(new CpuStateVectorBackend(), new VqeOptions
        { InitialStepSize = 20, MinimumStepSize = 15, SufficientDecrease = 0.1 }).Solve(Single(), Z(), [1]);
        Assert.Equal(VqeTerminationReason.LineSearchStalled, minimum.TerminationReason);
        Assert.Equal(5, minimum.CircuitEvaluationCount);
        var huge = new VqeSolver(new CpuStateVectorBackend(), new VqeOptions { GradientTolerance = 0 }).Solve(Single(), Z(), [1e100]);
        Assert.Equal(VqeTerminationReason.LineSearchStalled, huge.TerminationReason);
        Assert.Equal(4, huge.CircuitEvaluationCount); // No backend call for an unchanged parameter vector.
        var offset = new PauliHamiltonian(1, [new(1e16, new PauliString("I")), new(1, new PauliString("Z"))]);
        var result = new VqeSolver(new CpuStateVectorBackend()).Solve(Single(), offset, [0.7]);
        Assert.Equal(VqeTerminationReason.LineSearchStalled, result.TerminationReason);
        Assert.Equal(0, result.IterationCount);
    }

    [Theory]
    [InlineData(1e308, 1e-308, 1e-310)]
    [InlineData(1e-200, 1e200, 1e-12)]
    public void Sufficient_decrease_handles_gradient_square_overflow_and_underflow(double weight, double step, double minimum)
    {
        var result = new VqeSolver(new CpuStateVectorBackend(), new VqeOptions
        { MaximumIterations = 1, InitialStepSize = step, MinimumStepSize = minimum, GradientTolerance = 0 })
            .Solve(Single(), Z(weight), [Math.PI / 2]);
        Assert.Equal(1, result.IterationCount);
        Assert.Equal(VqeTerminationReason.IterationLimitReached, result.TerminationReason);
        Assert.True(result.Energy / weight < -0.8);
        Near(Math.Cos(result.Parameters[0]), result.Energy / weight);
    }

    [Fact]
    public void Unrepresentable_candidates_exhaust_search_attempts_without_backend_calls()
    {
        var backend = new RecordingBackend();
        var result = new VqeSolver(backend, new VqeOptions { InitialStepSize = 1e308, MaximumLineSearchIterations = 3 })
            .Solve(Single(), Z(1e308), [0.7]);
        Assert.Equal(VqeTerminationReason.LineSearchStalled, result.TerminationReason);
        Assert.Equal(4, backend.Calls);
        Assert.Equal(1, result.EnergyEvaluationCount);
        Assert.Equal(0.7, result.Parameters[0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Subnormal_steps_stop_when_reduction_underflows_or_rounds_back_to_the_same_step(bool roundsBack)
    {
        var x = new PauliHamiltonian(1, [new(1, new PauliString("X"))]);
        var result = new VqeSolver(new CpuStateVectorBackend(), new VqeOptions
        {
            InitialStepSize = double.Epsilon,
            MinimumStepSize = double.Epsilon,
            GradientTolerance = 0,
            BacktrackingFactor = roundsBack ? Math.BitDecrement(1) : 0.5
        }).Solve(Single(), x, [0]);
        Assert.Equal(VqeTerminationReason.LineSearchStalled, result.TerminationReason);
        Assert.Equal(5, result.CircuitEvaluationCount);
        Assert.Equal(0, result.Parameters[0]);
    }

    [Fact]
    public void Log_comparison_accepts_a_representable_energy_drop_whose_direct_subtraction_overflows()
    {
        var result = new VqeSolver(new CpuStateVectorBackend(), new VqeOptions
        { InitialStepSize = 7e-308, MinimumStepSize = 1e-310, MaximumIterations = 1, GradientTolerance = 0 })
            .Solve(Single(), Z(1e308), [0.4]);
        Assert.Equal(1, result.IterationCount);
        Assert.True(double.IsPositiveInfinity(1e308 * Math.Cos(0.4) - result.Energy));
        Near(Math.Cos(result.Parameters[0]), result.Energy / 1e308);
    }

    [Fact]
    public void Host_budget_accounts_for_overlapping_solver_and_evaluator_arrays_before_execution()
    {
        var backend = new RecordingBackend();
        var options = new VqeOptions { MaximumIterations = 1 };
        var solver = new VqeSolver(backend, options, new SimulationOptions { MemoryBudgetBytes = 88 });
        Assert.Equal(1, solver.Solve(Single(), Z(), [0.7]).IterationCount); // 16*2 + 48*1 + 8*1.
        long calls = backend.Calls;
        Assert.Throws<ArgumentOutOfRangeException>(() => new VqeSolver(backend, options, new SimulationOptions { MemoryBudgetBytes = 87 }).Solve(Single(), Z(), [0.7]));
        Assert.Throws<ArgumentOutOfRangeException>(() => solver.Solve(new ParameterizedCircuit(30, 0), new PauliHamiltonian(30, []), []));
        Assert.Equal(calls, backend.Calls);
        var empty = new VqeSolver(backend, simulationOptions: new SimulationOptions { MemoryBudgetBytes = 32 })
            .Solve(new ParameterizedCircuit(1, 0), new PauliHamiltonian(1, []), []);
        Assert.Equal(VqeTerminationReason.GradientToleranceReached, empty.TerminationReason);
    }

    [Fact]
    public void Options_and_inputs_fail_before_calling_the_backend()
    {
        var backend = new RecordingBackend();
        Assert.Throws<ArgumentNullException>(() => new VqeSolver(null!));
        var defaults = new VqeOptions();
        foreach (var invalid in new[] { defaults with { MaximumIterations = -1 }, defaults with { MaximumCircuitEvaluations = 0 },
            defaults with { MaximumLineSearchIterations = 0 }, defaults with { MinimumStepSize = 2 },
            defaults with { BacktrackingFactor = 0 }, defaults with { BacktrackingFactor = 1 },
            defaults with { SufficientDecrease = 0 }, defaults with { SufficientDecrease = 1 }, defaults with { GradientTolerance = -1 } })
        { Assert.Throws<ArgumentOutOfRangeException>(() => new VqeSolver(backend, invalid)); }
        foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1.0 })
        {
            foreach (var invalid in new[] { defaults with { GradientTolerance = value }, defaults with { InitialStepSize = value },
                defaults with { MinimumStepSize = value }, defaults with { BacktrackingFactor = value }, defaults with { SufficientDecrease = value } })
            { Assert.Throws<ArgumentOutOfRangeException>(() => new VqeSolver(backend, invalid)); }
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => new VqeSolver(backend, defaults with { InitialStepSize = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VqeSolver(backend, defaults with { MinimumStepSize = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VqeSolver(backend, simulationOptions: new SimulationOptions { MemoryBudgetBytes = 0 }));
        var solver = new VqeSolver(backend);
        Assert.Throws<ArgumentNullException>(() => solver.Solve(null!, Z(), []));
        Assert.Throws<ArgumentNullException>(() => solver.Solve(Single(), null!, [1]));
        Assert.Throws<ArgumentException>(() => solver.Solve(Single(), new PauliHamiltonian(2, []), [1]));
        Assert.Throws<ArgumentException>(() => solver.Solve(Single(), Z(), []));
        foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        { Assert.Throws<ArgumentOutOfRangeException>(() => solver.Solve(Single(), Z(), [value])); }
        Assert.Equal(0, backend.Calls);
    }

    [Fact]
    public void Cancellation_and_backend_or_numeric_failures_never_return_partial_results()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var backend = new RecordingBackend();
        Assert.Throws<OperationCanceledException>(() => new VqeSolver(backend).Solve(Single(), Z(), [0.7], canceled.Token));
        Assert.Equal(0, backend.Calls);
        foreach (int call in new[] { 1, 2, 5 })
        {
            using var during = new CancellationTokenSource();
            var cancelBackend = new RecordingBackend((index, _) => { if (index == call) { during.Cancel(); } });
            Assert.Throws<OperationCanceledException>(() => new VqeSolver(cancelBackend).Solve(Single(), Z(), [0.7], during.Token));
            var failing = new RecordingBackend((index, _) => { if (index == call) { throw new InvalidOperationException("Backend failure"); } });
            Assert.Throws<InvalidOperationException>(() => new VqeSolver(failing).Solve(Single(), Z(), [0.7]));
            var invalid = new RecordingBackend((index, buffer) => { if (index == call) { buffer.Span.Clear(); } });
            Assert.Throws<InvalidOperationException>(() => new VqeSolver(invalid).Solve(Single(), Z(), [0.7]));
        }
        var overflow = new PauliHamiltonian(1, [new(1e308, new PauliString("I")), new(1e308, new PauliString("I"))]);
        Assert.Throws<InvalidOperationException>(() => new VqeSolver(backend).Solve(Single(), overflow, [0.7]));
    }

    private static void Near(double expected, double actual, double tolerance = 8e-12) => Assert.InRange(Math.Abs(expected - actual), 0, tolerance);
    private static void Accounting(VqeResult result, int rotations) =>
        Assert.Equal(result.EnergyEvaluationCount + (1 + 2L * rotations) * result.GradientEvaluationCount, result.CircuitEvaluationCount);

    private sealed class RecordingBackend(Action<int, Memory<Complex>>? afterExecution = null) : IStateVectorBackend
    {
        public int Calls { get; private set; }
        public void Execute(QuantumCircuit circuit, Memory<Complex> amplitudes, CancellationToken cancellationToken = default)
        {
            Assert.Equal(Complex.One, amplitudes.Span[0]);
            for (int index = 1; index < amplitudes.Length; index++) { Assert.Equal(Complex.Zero, amplitudes.Span[index]); }
            Calls++;
            new CpuStateVectorBackend().Execute(circuit, amplitudes, cancellationToken);
            afterExecution?.Invoke(Calls, amplitudes);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
