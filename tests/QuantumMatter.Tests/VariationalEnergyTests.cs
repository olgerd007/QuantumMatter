using System.Numerics;
using QuantumMatter.Application;
using QuantumMatter.Application.Variational;
using QuantumMatter.Core;
using QuantumMatter.Core.Observables;
using QuantumMatter.Core.Variational;
using QuantumMatter.Infrastructure;
using Xunit;

namespace QuantumMatter.Tests;

public sealed class VariationalEnergyTests
{
    private static PauliHamiltonian Z(double coefficient = 1) => new(1, [new PauliTerm(coefficient, new PauliString("Z"))]);
    private static VariationalEnergyEvaluator Create(SimulationOptions? options = null) => new(new CpuStateVectorBackend(), options);

    [Theory]
    [InlineData("scalar")]
    [InlineData("parallel")]
    [InlineData("simd")]
#if CUDA_TESTS
    [InlineData("cuda")]
#endif
    public void Shared_parameter_gradients_use_the_product_rule_and_preserve_large_angle_shifts(string backendName)
    {
        IStateVectorBackend backend = NoisyDynamicCircuitTests.Backend(backendName);
        using IDisposable? owned = backend as IDisposable;
        var evaluator = new VariationalEnergyEvaluator(backend);
        var shared = new ParameterizedCircuit(1, 2).Ry(0, 0).Ry(0, 0);
        const double angle = 0.37;
        var result = evaluator.EvaluateWithGradient(shared, Z(), [angle, 17]);
        Near(Math.Cos(2 * angle), result.Energy);
        Near(-2 * Math.Sin(2 * angle), result.Gradient[0]);
        Assert.Equal(0, result.Gradient[1]);
        var single = new ParameterizedCircuit(1, 1).Ry(0, 0);
        const double huge = 1e100;
        Assert.Equal(huge, huge + Math.PI / 2);
        result = evaluator.EvaluateWithGradient(single, Z(), [huge]);
        Near(Math.Cos(huge), result.Energy);
        Near(-Math.Sin(huge), result.Gradient[0]);
        var offset = new PauliHamiltonian(1, [new PauliTerm(1e16, new PauliString("I")), new PauliTerm(1, new PauliString("Z"))]);
        Near(-Math.Sin(angle), evaluator.EvaluateWithGradient(single, offset, [angle]).Gradient[0]);
        var large = evaluator.EvaluateWithGradient(single, Z(1e308), [Math.PI / 2]);
        Assert.InRange(Math.Abs(large.Gradient[0] / 1e308 + 1), 0, 4e-12);
    }

    [Theory]
    [InlineData("scalar")]
    [InlineData("parallel")]
    [InlineData("simd")]
#if CUDA_TESTS
    [InlineData("cuda")]
#endif
    public void Complex_entangled_energies_and_gradients_match_independent_dense_product_derivatives(string backendName)
    {
        IStateVectorBackend backend = NoisyDynamicCircuitTests.Backend(backendName);
        using IDisposable? owned = backend as IDisposable;
        var evaluator = new VariationalEnergyEvaluator(backend);
        var template = new ParameterizedCircuit(2, 4).Append(new QuantumCircuit(2).H(0).S(0).H(1))
            .Rx(0, 0).Append(new QuantumCircuit(2).CNot(0, 1)).Rz(1, 1).Ry(0, 0)
            .Append(new QuantumCircuit(2).Sdg(1)).Rx(1, 2);
        PauliTerm[] terms = [new(0.7, new PauliString("YZ")), new(-0.4, new PauliString("XX")),
            new(0.31, new PauliString("ZI")), new(-0.22, new PauliString("IY")), new(0.13, new PauliString("II"))];
        var hamiltonian = new PauliHamiltonian(2, terms);
        var h = new Complex[4, 4];
        foreach (PauliTerm term in terms)
        {
            Complex[,] matrix = PauliHamiltonianTests.DensePauli(term.Observable.Operators);
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++) { h[row, column] += term.Coefficient * matrix[row, column]; }
            }
        }

        foreach (double[] parameters in new[] { new[] { 0.71, -0.23, 0.52, 9 }, new[] { -1.2, 0.89, -0.41, -8 } })
        {
            Complex[] state = [1, 0, 0, 0];
            Complex[][] derivatives = Enumerable.Range(0, 4).Select(_ => new Complex[4]).ToArray();
            double s = Math.Sqrt(0.5);
            Complex[,] hadamard = { { s, s }, { s, -s } };
            Complex[,] phase = { { 1, 0 }, { 0, Complex.ImaginaryOne } };
            Complex[,] adjoint = { { 1, 0 }, { 0, -Complex.ImaginaryOne } };
            Complex[,] cnot = { { 1, 0, 0, 0 }, { 0, 0, 0, 1 }, { 0, 0, 1, 0 }, { 0, 1, 0, 0 } };
            Push(Local(hadamard, 0));
            Push(Local(phase, 0));
            Push(Local(hadamard, 1));
            Rotate('X', 0, 0);
            Push(cnot);
            Rotate('Z', 1, 1);
            Rotate('Y', 0, 0);
            Push(Local(adjoint, 1));
            Rotate('X', 1, 2);
            Complex[] hState = PauliHamiltonianTests.Apply(h, state);
            double mass = Inner(state, state).Real;
            double expected = Inner(state, hState).Real / mass;
            var actual = evaluator.EvaluateWithGradient(template, hamiltonian, parameters);
            Near(expected, actual.Energy);
            Near(expected, evaluator.Evaluate(template, hamiltonian, parameters));
            for (int p = 0; p < 4; p++)
            {
                double expectedGradient = (2 * Inner(derivatives[p], hState).Real - expected * 2 * Inner(derivatives[p], state).Real) / mass;
                Near(expectedGradient, actual.Gradient[p]);
            }

            // Independent central differences supplement, rather than define, the analytic dense oracle.
            double[] shifted = (double[])parameters.Clone();
            shifted[0] += 1e-5;
            double plus = evaluator.Evaluate(template, hamiltonian, shifted);
            shifted[0] -= 2e-5;
            double minus = evaluator.Evaluate(template, hamiltonian, shifted);
            Assert.InRange(Math.Abs(actual.Gradient[0] - (plus - minus) / 2e-5), 0, 3e-9);

            void Push(Complex[,] matrix, Complex[,]? derivative = null, int parameter = -1)
            {
                Complex[] previous = state;
                state = PauliHamiltonianTests.Apply(matrix, previous);
                for (int p = 0; p < 4; p++)
                {
                    derivatives[p] = PauliHamiltonianTests.Apply(matrix, derivatives[p]);
                    if (p == parameter)
                    {
                        Complex[] extra = PauliHamiltonianTests.Apply(derivative!, previous);
                        for (int i = 0; i < 4; i++) { derivatives[p][i] += extra[i]; }
                    }
                }
            }

            void Rotate(char axis, int target, int parameter)
            {
                double c = Math.Cos(parameters[parameter] / 2), sine = Math.Sin(parameters[parameter] / 2);
                Complex[,] pauli = PauliHamiltonianTests.DensePauli(axis.ToString());
                var rotation = new Complex[2, 2];
                var derivative = new Complex[2, 2];
                for (int row = 0; row < 2; row++)
                {
                    for (int column = 0; column < 2; column++)
                    {
                        double identity = row == column ? 1 : 0;
                        rotation[row, column] = c * identity - Complex.ImaginaryOne * sine * pauli[row, column];
                        derivative[row, column] = -0.5 * sine * identity - Complex.ImaginaryOne * 0.5 * c * pauli[row, column];
                    }
                }

                Push(Local(rotation, target), Local(derivative, target), parameter);
            }
        }
    }

    [Fact]
    public void Binding_snapshots_constant_gates_and_values_and_shifts_only_one_shared_occurrence()
    {
        var constant = new QuantumCircuit(1).H(0).Rz(0, 0.1);
        var template = new ParameterizedCircuit(1, 2).Append(constant).Ry(0, 0).Rx(0, 0);
        constant.X(0);
        double[] values = [0.7, 2];
        QuantumCircuit bound = template.Bind(values);
        values[0] = -8;
        Assert.Equal(4, bound.Operations.Count);
        Assert.Equal(0.7, bound.Operations[2].Angle);
        Assert.Equal(0.7, bound.Operations[3].Angle);
        Assert.Equal(new[] { 0, 0 }, template.RotationParameterIndices);
        QuantumCircuit shifted = template.BindWithRotationShift([0.7, 2], 1, Math.PI / 2);
        Assert.Equal(5, shifted.Operations.Count);
        Assert.Equal(0.7, shifted.Operations[2].Angle);
        Assert.Equal(0.7, shifted.Operations[3].Angle);
        Assert.Equal(Math.PI / 2, shifted.Operations[4].Angle);
        bound.X(0);
        Assert.Equal(4, template.OperationCount);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)template.RotationParameterIndices)[0] = 1);
    }

    [Fact]
    public void Invalid_templates_bindings_and_shifts_are_rejected_without_changing_the_builder()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParameterizedCircuit(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParameterizedCircuit(31, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParameterizedCircuit(1, -1));
        var template = new ParameterizedCircuit(1, 1);
        Assert.Throws<ArgumentNullException>(() => template.Append(null!));
        Assert.Throws<ArgumentException>(() => template.Append(new QuantumCircuit(2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => template.Rx(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => template.Ry(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => template.Rz(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => template.Rx(0, -1));
        Assert.Equal(0, template.OperationCount);
        Assert.Empty(template.RotationParameterIndices);
        template.Ry(0, 0);
        Assert.Throws<ArgumentException>(() => template.Bind([]));
        Assert.Throws<ArgumentException>(() => template.Bind([1, 2]));
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => template.Bind([invalid]));
            Assert.Throws<ArgumentOutOfRangeException>(() => template.BindWithRotationShift([0], 0, invalid));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => template.BindWithRotationShift([0], -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => template.BindWithRotationShift([0], 1, 1));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => template.Bind([0], canceled.Token));
        Assert.Throws<OperationCanceledException>(() => template.BindWithRotationShift([0], 0, 1, canceled.Token));
        Assert.Equal(1, template.OperationCount);
    }

    [Fact]
    public void All_shifts_start_from_zero_and_results_and_inputs_are_independently_owned()
    {
        var backend = new ResetCheckingBackend();
        var evaluator = new VariationalEnergyEvaluator(backend);
        var template = new ParameterizedCircuit(1, 2).Rx(0, 0).Ry(0, 0).Rz(0, 1);
        double[] input = [0.3, 0.4];
        var result = evaluator.EvaluateWithGradient(template, Z(), input);
        Assert.Equal(7, backend.Calls);
        Assert.Equal(new[] { 0.3, 0.4 }, input);
        double original = result.Gradient[0];
        input[0] = 0.9;
        evaluator.EvaluateWithGradient(template, Z(), input);
        Assert.Equal(original, result.Gradient[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<double>)result.Gradient)[0] = 12);
    }

    [Fact]
    public void Empty_unused_and_zero_parameter_circuits_and_exact_array_budgets_are_supported()
    {
        var fixedCircuit = new ParameterizedCircuit(1, 0).Append(new QuantumCircuit(1).X(0));
        var result = Create(new SimulationOptions { MemoryBudgetBytes = 40 }).EvaluateWithGradient(fixedCircuit, Z(), []);
        Assert.Equal(-1, result.Energy);
        Assert.Empty(result.Gradient);
        var unused = new ParameterizedCircuit(1, 1);
        Assert.Equal(0, Create(new SimulationOptions { MemoryBudgetBytes = 64 }).EvaluateWithGradient(unused, Z(), [7]).Gradient[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(new SimulationOptions { MemoryBudgetBytes = 63 }).EvaluateWithGradient(unused, Z(), [7]));
        Assert.Equal(1, Create(new SimulationOptions { MemoryBudgetBytes = 32 }).Evaluate(unused, Z(), [7]));
        var zero = new PauliHamiltonian(1, []);
        Assert.Equal(0, Create(new SimulationOptions { MemoryBudgetBytes = 56 }).EvaluateWithGradient(new ParameterizedCircuit(1, 1).Ry(0, 0), zero, [0.3]).Gradient[0]);
    }

    [Fact]
    public void Workflow_inputs_cancellation_backend_failures_and_invalid_states_never_return_partial_success()
    {
        var template = new ParameterizedCircuit(1, 1).Ry(0, 0);
        var bad = new FailingBackend();
        var evaluator = new VariationalEnergyEvaluator(bad);
        Assert.Throws<ArgumentNullException>(() => new VariationalEnergyEvaluator(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(new SimulationOptions { MemoryBudgetBytes = 0 }));
        Assert.Throws<ArgumentNullException>(() => evaluator.Evaluate(null!, Z(), [0]));
        Assert.Throws<ArgumentNullException>(() => evaluator.Evaluate(template, null!, [0]));
        Assert.Throws<ArgumentException>(() => evaluator.Evaluate(template, new PauliHamiltonian(2, []), [0]));
        Assert.Throws<ArgumentException>(() => evaluator.EvaluateWithGradient(template, Z(), []));
        Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.Evaluate(template, Z(), [double.NaN]));
        Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.EvaluateWithGradient(new ParameterizedCircuit(30, 1), new PauliHamiltonian(30, []), [0]));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => evaluator.Evaluate(template, Z(), [0], canceled.Token));
        Assert.Throws<OperationCanceledException>(() => evaluator.EvaluateWithGradient(template, Z(), [0], canceled.Token));
        Assert.Equal(0, bad.Calls);
        Assert.Throws<InvalidOperationException>(() => evaluator.EvaluateWithGradient(template, Z(), [0]));
        using var duringShift = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => new VariationalEnergyEvaluator(new CancelBackend(duringShift))
            .EvaluateWithGradient(template, Z(), [0], duringShift.Token));
        Assert.Throws<InvalidOperationException>(() => new VariationalEnergyEvaluator(new InvalidStateBackend())
            .EvaluateWithGradient(template, new PauliHamiltonian(1, []), [0]));
    }

    private static Complex[,] Local(Complex[,] local, int target)
    {
        var matrix = new Complex[4, 4];
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                if ((row >> (1 - target) & 1) == (column >> (1 - target) & 1))
                {
                    matrix[row, column] = local[(row >> target) & 1, (column >> target) & 1];
                }
            }
        }

        return matrix;
    }

    [Fact]
    public void Unrepresentable_gradients_and_invalid_shifted_states_are_rejected()
    {
        var shared = new ParameterizedCircuit(1, 1).Ry(0, 0).Ry(0, 0);
        Assert.Throws<InvalidOperationException>(() => Create().EvaluateWithGradient(shared, Z(1e308), [Math.PI / 4]));
        var single = new ParameterizedCircuit(1, 1).Ry(0, 0);
        Assert.Throws<InvalidOperationException>(() => new VariationalEnergyEvaluator(new InvalidStateBackend(3))
            .EvaluateWithGradient(single, new PauliHamiltonian(1, []), [0.4]));
    }

    private static Complex Inner(Complex[] left, Complex[] right) => Enumerable.Range(0, left.Length)
        .Aggregate(Complex.Zero, (sum, index) => sum + Complex.Conjugate(left[index]) * right[index]);
    private static void Near(double expected, double actual) => Assert.InRange(Math.Abs(expected - actual), 0, 5e-12);
    private sealed class ResetCheckingBackend : IStateVectorBackend
    {
        public int Calls { get; private set; }
        public void Execute(QuantumCircuit circuit, Memory<Complex> amplitudes, CancellationToken cancellationToken = default)
        {
            Assert.Equal(Complex.One, amplitudes.Span[0]);
            Assert.All(amplitudes.ToArray().Skip(1), value => Assert.Equal(Complex.Zero, value));
            Calls++;
            new CpuStateVectorBackend().Execute(circuit, amplitudes, cancellationToken);
        }
    }

    private sealed class FailingBackend : IStateVectorBackend
    {
        public int Calls { get; private set; }
        public void Execute(QuantumCircuit circuit, Memory<Complex> amplitudes, CancellationToken cancellationToken = default) { Calls++; throw new InvalidOperationException("Backend failed"); }
    }

    private sealed class CancelBackend(CancellationTokenSource source) : IStateVectorBackend
    {
        private int _calls;
        public void Execute(QuantumCircuit circuit, Memory<Complex> amplitudes, CancellationToken cancellationToken = default)
        {
            if (++_calls == 2) { source.Cancel(); }
            new CpuStateVectorBackend().Execute(circuit, amplitudes, cancellationToken);
        }
    }

    private sealed class InvalidStateBackend(int invalidOnCall = 1) : IStateVectorBackend
    {
        private int _calls;
        public void Execute(QuantumCircuit circuit, Memory<Complex> amplitudes, CancellationToken cancellationToken = default)
        {
            if (++_calls == invalidOnCall) { amplitudes.Span.Clear(); }
            else { new CpuStateVectorBackend().Execute(circuit, amplitudes, cancellationToken); }
        }
    }
}
