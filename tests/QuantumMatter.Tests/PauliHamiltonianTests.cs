using System.Numerics;
using QuantumMatter.Core;
using QuantumMatter.Core.Observables;
using Xunit;

namespace QuantumMatter.Tests;

public sealed class PauliHamiltonianTests
{
    [Fact]
    public void Energies_match_independent_dense_tensor_products_without_mutating_or_renormalizing_states()
    {
        var random = new Random(945);
        for (int qubits = 1; qubits <= 4; qubits++)
        {
            Complex[] input = StateObservationTests.RandomState(qubits, random).Select(a => a * 2.3).ToArray();
            PauliTerm[] terms = Enumerable.Range(0, 17).Select(_ => new PauliTerm(random.NextDouble() * 4 - 2,
                new PauliString(new string(Enumerable.Range(0, qubits).Select(_ => "IXYZ"[random.Next(4)]).ToArray())))).ToArray();
            var hamiltonian = new PauliHamiltonian(qubits, terms);
            var matrix = new Complex[input.Length, input.Length];
            foreach (PauliTerm term in terms)
            {
                Complex[,] pauli = DensePauli(term.Observable.Operators);
                for (int row = 0; row < input.Length; row++)
                {
                    for (int column = 0; column < input.Length; column++) { matrix[row, column] += term.Coefficient * pauli[row, column]; }
                }
            }

            Complex[] transformed = Apply(matrix, input);
            Complex expected = Enumerable.Range(0, input.Length).Aggregate(Complex.Zero,
                (sum, index) => sum + Complex.Conjugate(input[index]) * transformed[index]) / input.Sum(Probability);
            var state = State(input);
            Assert.InRange(Math.Abs(expected.Imaginary), 0, 3e-12);
            Assert.InRange(Math.Abs(state.ExpectationEnergy(hamiltonian) - expected.Real), 0, 4e-12);
            Assert.Equal(input, state.Snapshot());
        }
    }

    [Fact]
    public void Identity_zero_negative_duplicate_and_canceling_terms_have_explicit_semantics()
    {
        var state = new StateVector(1);
        Assert.Equal(0, state.ExpectationEnergy(new PauliHamiltonian(1, [])));
        Assert.Equal(-3, state.ExpectationEnergy(new PauliHamiltonian(1, [new PauliTerm(-3, new PauliString("I"))])));
        var canceling = new PauliHamiltonian(1,
            [new PauliTerm(1e16, new PauliString("I")), new PauliTerm(1, new PauliString("I")), new PauliTerm(-1e16, new PauliString("I"))]);
        Assert.Equal(1, state.ExpectationEnergy(canceling));
        Assert.Equal(3, canceling.Terms.Count);
        var imaginaryEigenstate = State([Math.Sqrt(0.5), Complex.ImaginaryOne * Math.Sqrt(0.5)]);
        Assert.InRange(Math.Abs(imaginaryEigenstate.ExpectationEnergy(new PauliHamiltonian(1,
            [new PauliTerm(-2, new PauliString("Y")), new PauliTerm(0, new PauliString("Z"))])) + 2), 0, 1e-12);
    }

    [Fact]
    public void Hamiltonians_snapshot_inputs_and_reject_invalid_descriptions()
    {
        var original = new PauliTerm(2, new PauliString("Z"));
        PauliTerm[] input = [original];
        var hamiltonian = new PauliHamiltonian(1, input);
        input[0] = new PauliTerm(8, new PauliString("I"));
        Assert.Same(original, hamiltonian.Terms[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<PauliTerm>)hamiltonian.Terms)[0] = input[0]);
        Assert.Throws<ArgumentNullException>(() => new PauliTerm(1, null!));
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PauliTerm(invalid, new PauliString("Z")));
        }

        Assert.Throws<ArgumentNullException>(() => new PauliHamiltonian(1, null!));
        Assert.Throws<ArgumentException>(() => new PauliHamiltonian(1, [null!]));
        Assert.Throws<ArgumentException>(() => new PauliHamiltonian(2, [original]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PauliHamiltonian(0, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PauliHamiltonian(31, []));
        Assert.Throws<ArgumentNullException>(() => new StateVector(1).ExpectationEnergy(null!));
        Assert.Throws<ArgumentException>(() => new StateVector(2).ExpectationEnergy(hamiltonian));
    }

    [Fact]
    public void Invalid_mass_overflow_and_cancellation_are_rejected_even_for_empty_or_zero_Hamiltonians()
    {
        foreach (Complex[] input in new[] { new Complex[] { 0, 0 }, [double.NaN, 0], [double.PositiveInfinity, 0] })
        {
            var state = State(input);
            Assert.Throws<InvalidOperationException>(() => state.ExpectationEnergy(new PauliHamiltonian(1, [])));
            Assert.Throws<InvalidOperationException>(() => state.ExpectationEnergy(new PauliHamiltonian(1, [new PauliTerm(0, new PauliString("Z"))])));
        }

        var overflow = new PauliHamiltonian(1, [new PauliTerm(double.MaxValue, new PauliString("I")), new PauliTerm(double.MaxValue, new PauliString("I"))]);
        var normal = new StateVector(1);
        Assert.Throws<InvalidOperationException>(() => normal.ExpectationEnergy(overflow));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => normal.ExpectationEnergy(new PauliHamiltonian(1, []), canceled.Token));
        Assert.Equal(1, normal.Probability(0));
    }

    internal static Complex[,] DensePauli(string labels)
    {
        Complex[,] matrix = { { 1 } };
        foreach (char label in labels)
        {
            Complex[,] factor = label switch
            {
                'I' => new Complex[,] { { 1, 0 }, { 0, 1 } },
                'X' => new Complex[,] { { 0, 1 }, { 1, 0 } },
                'Y' => new Complex[,] { { 0, -Complex.ImaginaryOne }, { Complex.ImaginaryOne, 0 } },
                _ => new Complex[,] { { 1, 0 }, { 0, -1 } }
            };
            int size = matrix.GetLength(0);
            var next = new Complex[2 * size, 2 * size];
            for (int row = 0; row < size; row++)
            {
                for (int column = 0; column < size; column++)
                {
                    for (int a = 0; a < 2; a++)
                    {
                        for (int b = 0; b < 2; b++) { next[2 * row + a, 2 * column + b] = matrix[row, column] * factor[a, b]; }
                    }
                }
            }

            matrix = next;
        }

        return matrix;
    }

    internal static Complex[] Apply(Complex[,] matrix, Complex[] input)
    {
        var output = new Complex[input.Length];
        for (int row = 0; row < input.Length; row++)
        {
            for (int column = 0; column < input.Length; column++) { output[row] += matrix[row, column] * input[column]; }
        }

        return output;
    }

    internal static StateVector State(Complex[] input)
    {
        var state = new StateVector(BitOperations.Log2((uint)input.Length));
        state.Evolve(new QuantumCircuit(state.QubitCount), new InputBackend(input));
        return state;
    }

    private static double Probability(Complex value) => value.Real * value.Real + value.Imaginary * value.Imaginary;
    private sealed class InputBackend(Complex[] input) : IStateVectorBackend
    {
        public void Execute(QuantumCircuit circuit, Memory<Complex> amplitudes, CancellationToken cancellationToken = default) => input.CopyTo(amplitudes.Span);
    }
}
