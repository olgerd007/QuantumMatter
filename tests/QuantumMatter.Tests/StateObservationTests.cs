using System.Numerics;
using QuantumMatter.Application;
using QuantumMatter.Core;
using QuantumMatter.Infrastructure;
using Xunit;

namespace QuantumMatter.Tests;

public sealed class StateObservationTests
{
    [Fact]
    public void Marginals_and_every_reachable_projection_match_independent_projectors()
    {
        var random = new Random(193);
        for (int qubits = 1; qubits <= 5; qubits++)
        {
            Complex[] initial = RandomState(qubits, random);
            for (int mask = 1; mask < (1 << qubits); mask++)
            {
                // Reverse order deliberately: output bit zero is the first selected qubit.
                int[] selection = Enumerable.Range(0, qubits).Where(q => (mask & (1 << q)) != 0).Reverse().ToArray();
                var state = State(initial);
                for (int outcome = 0; outcome < (1 << selection.Length); outcome++)
                {
                    double expected = initial.Select((amplitude, basis) => Pack(basis, selection) == outcome
                        ? amplitude.Magnitude * amplitude.Magnitude : 0).Sum();
                    AssertClose(expected, state.MarginalProbability(selection, outcome));
                }

                Assert.Equal(initial, state.Snapshot());
                double cumulative = 0;
                for (int sampledBasis = 0; sampledBasis < initial.Length; sampledBasis++)
                {
                    double mass = initial[sampledBasis].Magnitude * initial[sampledBasis].Magnitude;
                    double draw = cumulative + mass / 2;
                    cumulative += mass;
                    int expectedOutcome = Pack(sampledBasis, selection);
                    Complex[] projected = initial.Select((amplitude, basis) => Pack(basis, selection) == expectedOutcome
                        ? amplitude : Complex.Zero).ToArray();
                    double norm = Math.Sqrt(projected.Sum(value => value.Magnitude * value.Magnitude));
                    state = State(initial);
                    Assert.Equal(expectedOutcome, state.Measure(selection, new FixedRandom(draw)));
                    for (int basis = 0; basis < state.Length; basis++)
                    {
                        AssertComplex(projected[basis] / norm, state.Amplitude(basis));
                    }

                    AssertClose(1, state.Snapshot().Sum(value => value.Magnitude * value.Magnitude));
                    AssertClose(1, state.MarginalProbability(selection, expectedOutcome));
                }
            }
        }
    }

    [Fact]
    public void Every_ordered_basis_selection_packs_bits_in_the_requested_order()
    {
        var simulator = new StateVectorSimulator(new CpuStateVectorBackend());
        foreach (int[] selection in OrderedSelections([], [0, 1, 2, 3]))
        {
            for (int basis = 0; basis < 16; basis++)
            {
                var circuit = new QuantumCircuit(4);
                for (int q = 0; q < 4; q++)
                {
                    if ((basis & (1 << q)) != 0) { circuit.X(q); }
                }

                var state = simulator.Simulate(circuit);
                int expected = Pack(basis, selection);
                AssertClose(1, state.MarginalProbability(selection, expected));
                Assert.Equal(expected, state.Measure(selection, new FixedRandom(0)));
                AssertClose(1, state.Probability(basis));
            }
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.9999999999999999, 1)]
    public void Partial_measurement_preserves_entanglement_correlations_and_spectator_phase(double draw, int expected)
    {
        var simulator = new StateVectorSimulator(new CpuStateVectorBackend());
        var globalPhase = SingleQubitUnitary.Create(Complex.ImaginaryOne, 0, 0, Complex.ImaginaryOne);
        var state = simulator.Simulate(new QuantumCircuit(3).H(0).CNot(0, 2).H(1).S(1).U(0, globalPhase));
        Assert.Equal(expected, state.Measure([2], new FixedRandom(draw)));
        int branch = expected == 0 ? 0 : 5;
        AssertComplex(Complex.ImaginaryOne / Math.Sqrt(2), state.Amplitude(branch));
        AssertComplex(-1 / Math.Sqrt(2), state.Amplitude(branch | 2));
        AssertClose(1, state.ExpectationValue(new PauliString("IYI")));
        Assert.Equal(expected, state.Measure([0], new FixedRandom(1 - draw)));
        // Evolution remains possible; an inverse on the spectator returns it to |0>.
        state.Evolve(new QuantumCircuit(3).Sdg(1).H(1), new CpuStateVectorBackend());
        AssertClose(1, state.Probability(branch));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0.5, 3)]
    [InlineData(0.9999999999999999, 3)]
    public void Subset_draw_skips_zero_plateaus_and_uses_strict_full_basis_boundaries(double draw, int expected)
    {
        var state = State([0, Math.Sqrt(0.5), 0, Math.Sqrt(0.5)]);
        Assert.Equal(expected, state.Measure([0, 1], new FixedRandom(draw)));
        AssertClose(1, state.Probability(expected));
    }

    [Fact]
    public void Rounding_fallback_selects_supported_basis_and_scales_actual_mass()
    {
        // A subnormal mass makes multiplication round up to the total, exercising the last-support fallback.
        var state = State([0, new Complex(0, Math.Sqrt(double.Epsilon)), 0, 0]);
        AssertClose(1, state.MarginalProbability([1, 0], 2));
        Assert.Equal(2, state.Measure([1, 0], new FixedRandom(Math.BitDecrement(1.0))));
        AssertComplex(Complex.ImaginaryOne, state.Amplitude(1));
    }

    [Fact]
    public void New_observations_normalize_actual_mass_without_mutating_it()
    {
        var state = State([0.3, new Complex(0, 0.4)]);
        Complex[] before = state.Snapshot();
        AssertClose(0.64, state.MarginalProbability([0], 1));
        AssertClose(1, state.ExpectationValue(new PauliString("I")));
        AssertClose(-0.28, state.ExpectationValue(new PauliString("Z")));
        Assert.Equal(before, state.Snapshot());
        AssertClose(0.16, state.Probability(1)); // The original raw-mass API is unchanged.
    }

    [Fact]
    public void All_Pauli_products_through_four_qubits_match_independent_dense_matrices()
    {
        var random = new Random(628);
        for (int qubits = 1; qubits <= 4; qubits++)
        {
            Complex[] initial = RandomState(qubits, random);
            var state = State(initial);
            for (int code = 0; code < (1 << (2 * qubits)); code++)
            {
                int value = code;
                char[] labels = new char[qubits];
                for (int q = 0; q < qubits; q++)
                {
                    labels[qubits - 1 - q] = "IXYZ"[value % 4];
                    value /= 4;
                }

                var observable = new PauliString(new string(labels));
                Complex expected = DenseExpectation(initial, observable.Operators);
                AssertClose(0, expected.Imaginary);
                AssertClose(expected.Real, state.ExpectationValue(observable));
            }

            Assert.Equal(initial, state.Snapshot());
        }
    }

    [Theory]
    [InlineData("X", 1, 0)]
    [InlineData("Y", 1, 1)]
    [InlineData("Y", -1, 2)]
    [InlineData("Z", -1, 3)]
    public void Pauli_eigenstates_have_the_signed_eigenvalue(string pauli, double expected, int preparation)
    {
        var circuit = new QuantumCircuit(1);
        if (preparation == 3) { circuit.X(0); }
        else
        {
            circuit.H(0);
            if (preparation == 1) { circuit.S(0); }
            if (preparation == 2) { circuit.Sdg(0); }
        }

        var state = new StateVectorSimulator(new CpuStateVectorBackend()).Simulate(circuit);
        AssertClose(expected, state.ExpectationValue(new PauliString(pauli)));
    }

    [Theory]
    [InlineData("XX", 1)]
    [InlineData("YY", -1)]
    [InlineData("ZZ", 1)]
    [InlineData("ZI", 0)]
    [InlineData("IZ", 0)]
    public void Bell_observables_preserve_correlations_and_Y_sign(string pauli, double expected)
    {
        var state = new StateVectorSimulator(new CpuStateVectorBackend()).Simulate(new QuantumCircuit(2).H(0).CNot(0, 1));
        AssertClose(expected, state.ExpectationValue(new PauliString(pauli)));
    }

    [Fact]
    public void Pauli_label_endianness_includes_highest_supported_qubit_without_allocation()
    {
        var observable = new PauliString("Y" + new string('I', 28) + "X");
        Assert.Equal(30, observable.QubitCount);
        Assert.Equal('Y', observable.Operators[0]);
        var state = new StateVectorSimulator(new CpuStateVectorBackend()).Simulate(new QuantumCircuit(2).X(0));
        AssertClose(1, state.ExpectationValue(new PauliString("ZI")));
        AssertClose(-1, state.ExpectationValue(new PauliString("IZ")));
    }

    [Fact]
    public void Invalid_arguments_are_rejected_before_randomness_or_mutation()
    {
        var state = new StateVector(2);
        var random = new ThrowingRandom();
        foreach (int[] selection in new int[][] { [], [0, 0], [0, 1, 0] })
        {
            Assert.Throws<ArgumentException>(() => state.Measure(selection, random));
            Assert.Throws<ArgumentException>(() => state.MarginalProbability(selection, 0));
        }

        foreach (int[] selection in new int[][] { [-1], [2], [int.MaxValue], [int.MinValue] })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => state.Measure(selection, random));
            Assert.Throws<ArgumentOutOfRangeException>(() => state.MarginalProbability(selection, 0));
        }

        foreach (int outcome in new[] { -1, 2, int.MaxValue, int.MinValue })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => state.MarginalProbability([1], outcome));
        }

        Assert.Throws<ArgumentNullException>(() => state.Measure([0], null!));
        Assert.Throws<ArgumentNullException>(() => state.ExpectationValue(null!));
        Assert.Throws<ArgumentException>(() => state.ExpectationValue(new PauliString("X")));
        Assert.Equal(new Complex[] { 1, 0, 0, 0 }, state.Snapshot());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.MaxValue)]
    public void Invalid_mass_is_rejected_without_random_draw_or_projection(double amplitude)
    {
        var state = State([amplitude, 0]);
        Complex[] before = state.Snapshot();
        Assert.Throws<InvalidOperationException>(() => state.Measure([0], new ThrowingRandom()));
        Assert.Throws<InvalidOperationException>(() => state.MarginalProbability([0], 0));
        Assert.Throws<InvalidOperationException>(() => state.ExpectationValue(new PauliString("X")));
        Assert.Equal(before, state.Snapshot());
    }

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("A")]
    [InlineData(" X")]
    [InlineData("-Y")]
    [InlineData("XYZIIIIIIIIIIIIIIIIIIIIIIIIIIII")]
    public void Invalid_Pauli_labels_are_rejected(string operators) =>
        Assert.ThrowsAny<ArgumentException>(() => new PauliString(operators));

    [Fact]
    public void Null_Pauli_label_is_rejected() => Assert.Throws<ArgumentNullException>(() => new PauliString(null!));

    [Fact]
    public void Seeded_subset_samples_follow_marginals_and_are_repeatable()
    {
        Complex[] amplitudes = [Math.Sqrt(0.1), Math.Sqrt(0.2), Math.Sqrt(0.3), Math.Sqrt(0.4)];
        var first = new Random(301);
        var second = new Random(301);
        int ones = 0;
        for (int shot = 0; shot < 4000; shot++)
        {
            int outcome = State(amplitudes).Measure([0], first);
            Assert.Equal(outcome, State(amplitudes).Measure([0], second));
            ones += outcome;
        }

        // Binomial p=0.6; this interval exceeds six standard deviations.
        Assert.InRange(ones, 2200, 2600);
    }

    [Theory]
    [InlineData("scalar")]
    [InlineData("parallel")]
    [InlineData("simd")]
#if CUDA_TESTS
    [InlineData("cuda")]
#endif
    public void Observations_work_after_each_backend_and_allow_continued_evolution(string backendName)
    {
        IStateVectorBackend backend = backendName switch
        {
            "parallel" => new ParallelCpuStateVectorBackend(new ParallelCpuOptions { MinimumParallelQubits = 1 }),
            "simd" => new ParallelCpuStateVectorBackend(new ParallelCpuOptions { MinimumParallelQubits = 1, EnableSimd = true }),
            "cuda" => new CudaStateVectorBackend(),
            _ => new CpuStateVectorBackend()
        };
        using IDisposable? ownedBackend = backend as IDisposable;
        var circuit = new QuantumCircuit(4).H(0).S(0).Ry(1, 0.73).CNot(0, 3).Rx(2, -1.1).CNot(2, 1);
        var reference = new StateVectorSimulator(new CpuStateVectorBackend()).Simulate(circuit);
        var actual = new StateVectorSimulator(backend).Simulate(circuit);
        foreach (string labels in new[] { "IIII", "XYZI", "YYYX", "ZYZZ", "IYII" })
        {
            var pauli = new PauliString(labels);
            AssertClose(reference.ExpectationValue(pauli), actual.ExpectationValue(pauli));
        }

        AssertClose(reference.MarginalProbability([3, 0], 2), actual.MarginalProbability([3, 0], 2));
        Assert.Equal(reference.Measure([3, 0], new FixedRandom(0.813)), actual.Measure([3, 0], new FixedRandom(0.813)));
        var continuation = new QuantumCircuit(4).H(1).Rz(2, 0.42).CNot(1, 3);
        reference.Evolve(continuation, new CpuStateVectorBackend());
        actual.Evolve(continuation, backend);
        for (int basis = 0; basis < reference.Length; basis++) { AssertComplex(reference.Amplitude(basis), actual.Amplitude(basis)); }
    }

    [Fact]
    public void Cancellation_before_projection_leaves_state_unchanged()
    {
        Complex[] initial = RandomState(3, new Random(82));
        var state = State(initial);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => state.MarginalProbability([0], 0, canceled.Token));
        Assert.Throws<OperationCanceledException>(() => state.ExpectationValue(new PauliString("XYZ"), canceled.Token));
        Assert.Throws<OperationCanceledException>(() => state.Measure([0], new ThrowingRandom(), canceled.Token));
        using var duringDraw = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => state.Measure([0, 2], new CancelingRandom(duringDraw), duringDraw.Token));
        Assert.Equal(initial, state.Snapshot());
    }

    internal static Complex[] RandomState(int qubits, Random random)
    {
        Complex[] values = Enumerable.Range(0, 1 << qubits)
            .Select(_ => new Complex(random.NextDouble() - 0.5, random.NextDouble() - 0.5)).ToArray();
        double norm = Math.Sqrt(values.Sum(value => value.Magnitude * value.Magnitude));
        return values.Select(value => value / norm).ToArray();
    }

    private static StateVector State(Complex[] amplitudes)
    {
        int qubits = BitOperations.Log2((uint)amplitudes.Length);
        var state = new StateVector(qubits);
        state.Evolve(new QuantumCircuit(qubits), new InputBackend(amplitudes));
        return state;
    }

    private static int Pack(int basis, int[] qubits)
    {
        int outcome = 0;
        for (int bit = qubits.Length - 1; bit >= 0; bit--) { outcome = 2 * outcome + (basis / (1 << qubits[bit]) % 2); }
        return outcome;
    }

    private static IEnumerable<int[]> OrderedSelections(int[] prefix, int[] remaining)
    {
        if (prefix.Length > 0) { yield return prefix; }
        foreach (int qubit in remaining)
        {
            foreach (int[] selection in OrderedSelections([.. prefix, qubit], remaining.Where(q => q != qubit).ToArray()))
            {
                yield return selection;
            }
        }
    }

    private static Complex DenseExpectation(Complex[] state, string labels)
    {
        Complex[,] identity = { { 1, 0 }, { 0, 1 } };
        Complex[,] x = { { 0, 1 }, { 1, 0 } };
        Complex[,] y = { { 0, -Complex.ImaginaryOne }, { Complex.ImaginaryOne, 0 } };
        Complex[,] z = { { 1, 0 }, { 0, -1 } };
        Complex[,] matrix = { { 1 } };
        // Independent Kronecker construction uses explicit 2x2 entries, without production masks.
        foreach (char label in labels)
        {
            Complex[,] factor = label switch { 'X' => x, 'Y' => y, 'Z' => z, _ => identity };
            int size = matrix.GetLength(0);
            var product = new Complex[2 * size, 2 * size];
            for (int row = 0; row < size; row++)
            {
                for (int column = 0; column < size; column++)
                {
                    for (int a = 0; a < 2; a++)
                    {
                        for (int b = 0; b < 2; b++) { product[2 * row + a, 2 * column + b] = matrix[row, column] * factor[a, b]; }
                    }
                }
            }

            matrix = product;
        }

        Complex expectation = 0;
        for (int row = 0; row < state.Length; row++)
        {
            for (int column = 0; column < state.Length; column++) { expectation += Complex.Conjugate(state[row]) * matrix[row, column] * state[column]; }
        }

        return expectation / state.Sum(value => value.Magnitude * value.Magnitude);
    }

    private static void AssertClose(double expected, double actual) => Assert.InRange(Math.Abs(expected - actual), 0, 3e-12);
    private static void AssertComplex(Complex expected, Complex actual) => Assert.InRange(Complex.Abs(expected - actual), 0, 3e-12);

    private sealed class FixedRandom(double value) : Random
    {
        public override double NextDouble() => value;
    }

    private sealed class ThrowingRandom : Random
    {
        public override double NextDouble() => throw new InvalidOperationException("Randomness should not be consumed.");
    }

    private sealed class CancelingRandom(CancellationTokenSource source) : Random
    {
        public override double NextDouble() { source.Cancel(); return 0.5; }
    }

    private sealed class InputBackend(Complex[] values) : IStateVectorBackend
    {
        public void Execute(QuantumCircuit circuit, Memory<Complex> amplitudes, CancellationToken cancellationToken = default) => values.CopyTo(amplitudes.Span);
    }
}
