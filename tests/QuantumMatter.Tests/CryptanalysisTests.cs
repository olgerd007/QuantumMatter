using System.Numerics;
using QuantumMatter.Application;
using QuantumMatter.Core;
using QuantumMatter.Demo.Cryptography;
using QuantumMatter.Infrastructure;
using Xunit;

namespace QuantumMatter.Tests;

public sealed class CryptanalysisTests
{
    private readonly StateVectorSimulator _simulator = new(new CpuStateVectorBackend());

    [Fact]
    public void Toy_oracle_matches_independent_cipher_for_every_public_pair_and_key()
    {
        for (int plaintext = 0; plaintext < 8; plaintext++)
        {
            for (int ciphertext = 0; ciphertext < 8; ciphertext++)
            {
                for (int key = 0; key < 8; key++)
                {
                    int expectedCiphertext = DirectCipher(plaintext, key);
                    Assert.Equal(expectedCiphertext, ToyKeySearch.Encrypt(plaintext, key));
                    QuantumCircuit oracle = ToyKeySearch.CreateOracle(plaintext, ciphertext);
                    var state = _simulator.Simulate(Prepare(oracle.QubitCount, key));
                    state.Evolve(oracle, new CpuStateVectorBackend());
                    for (int index = 0; index < state.Length; index++)
                    {
                        double expected = index == key ? (expectedCiphertext == ciphertext ? -1 : 1) : 0;
                        Near(expected, state.Amplitude(index));
                    }
                }
            }
        }
    }

    [Fact]
    public void Toy_Grover_matches_amplification_phases_and_uncomputes_all_work_bits()
    {
        double theta = Math.Asin(1 / Math.Sqrt(8));
        for (int key = 0; key < 8; key++)
        {
            for (int iterations = 0; iterations <= 5; iterations++)
            {
                StateVector state = _simulator.Simulate(ToyKeySearch.CreateSearch(3, DirectCipher(3, key), iterations));
                for (int index = 0; index < state.Length; index++)
                {
                    double expected = index >= 8 ? 0 : index == key
                        ? Math.Sin((2 * iterations + 1) * theta)
                        : Math.Cos((2 * iterations + 1) * theta) / Math.Sqrt(7);
                    Near(expected, state.Amplitude(index));
                }
            }
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => ToyKeySearch.CreateOracle(8, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ToyKeySearch.CreateSearch(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ToyKeySearch.CreateSearch(0, 0, 65));
        Assert.Throws<OperationCanceledException>(() =>
            ToyKeySearch.CreateSearch(0, 0, cancellationToken: new CancellationToken(true)));
    }

    [Fact]
    public void Every_small_controlled_modular_permutation_matches_integer_arithmetic_and_restores_scratch()
    {
        for (int modulus = 3; modulus <= 15; modulus += 2)
        {
            int width = BitOperations.Log2((uint)(modulus - 1)) + 1;
            for (int multiplier = 1; multiplier < modulus; multiplier++)
            {
                if (BigInteger.GreatestCommonDivisor(multiplier, modulus) != 1) { continue; }
                QuantumCircuit circuit = ShorExperiment.CreateMultiplier(modulus, multiplier);
                for (int control = 0; control < 2; control++)
                {
                    for (int value = 0; value < (1 << width); value++)
                    {
                        StateVector state = _simulator.Simulate(Prepare(circuit.QubitCount, value | (control << width)));
                        state.Evolve(circuit, new CpuStateVectorBackend());
                        int result = control == 1 && value < modulus ? value * multiplier % modulus : value;
                        int expected = result | (control << width);
                        for (int index = 0; index < state.Length; index++)
                        {
                            Near(index == expected ? 1 : 0, state.Amplitude(index));
                        }
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(15, 2, 8)]
    [InlineData(7, 2, 4)]
    public void Order_finding_complex_amplitudes_match_independent_direct_DFT(int modulus, int basis, int counting)
    {
        QuantumCircuit circuit = ShorExperiment.CreateCircuit(modulus, basis, counting);
        StateVector state = _simulator.Simulate(circuit);
        int dimension = 1 << counting;
        int width = BitOperations.Log2((uint)(modulus - 1)) + 1;
        // Independent mathematical sum, including work-register entanglement.
        for (int work = 0; work < (1 << width); work++)
        {
            for (int output = 0; output < dimension; output++)
            {
                Complex expected = Complex.Zero;
                for (int input = 0; input < dimension; input++)
                {
                    if (BigInteger.ModPow(basis, input, modulus) == work)
                    {
                        expected += Complex.FromPolarCoordinates(1.0 / dimension, -Math.Tau * input * output / dimension);
                    }
                }

                Near(expected, state.Amplitude(output | (work << counting)), 2e-12);
            }
        }

        for (int index = dimension << width; index < state.Length; index++) { Near(0, state.Amplitude(index)); }
    }

    [Fact]
    public void Shor_recovers_factors_and_handles_uninformative_and_trivial_candidates()
    {
        ShorResult result = ShorExperiment.Run(_simulator);
        Assert.Equal((3, 5), result.Factors!.Value);
        Assert.Equal(14, result.PhysicalQubits);
        Assert.Equal(512, result.CountingOutcomes.Values.Sum());
        Assert.All(result.CountingOutcomes.Keys, outcome => Assert.Contains(outcome, new[] { 0, 64, 128, 192 }));
        Assert.Null(ShorExperiment.RecoverFactors(15, 2, 0, 8));
        Assert.Null(ShorExperiment.RecoverFactors(15, 14, 128, 8));
        Assert.Null(ShorExperiment.RecoverFactors(7, 2, 4, 4));
        Assert.Equal((3, 5), ShorExperiment.RecoverFactors(15, 2, 128, 8)!.Value);
        Assert.Throws<ArgumentOutOfRangeException>(() => ShorExperiment.CreateCircuit(21));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShorExperiment.CreateCircuit(15, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShorExperiment.CreateCircuit(countingQubits: 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShorExperiment.RecoverFactors(15, 2, 256, 8));
        Assert.Throws<OperationCanceledException>(() =>
            ShorExperiment.CreateCircuit(cancellationToken: new CancellationToken(true)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ShorExperiment.Run(new StateVectorSimulator(new CpuStateVectorBackend(), new SimulationOptions { MemoryBudgetBytes = 1 })));
    }

    private static int DirectCipher(int plaintext, int key)
    {
        int first = (plaintext & 1) ^ (key & 1);
        int second = ((plaintext >> 1) & 1) ^ ((key >> 1) & 1);
        int third = ((plaintext >> 2) & 1) ^ ((key >> 2) & 1) ^ (first & second);
        return second | (third << 1) | (first << 2);
    }

    private static QuantumCircuit Prepare(int qubits, int basis)
    {
        var circuit = new QuantumCircuit(qubits);
        for (int bit = 0; bit < qubits; bit++)
        {
            if ((basis & (1 << bit)) != 0) { circuit.X(bit); }
        }

        return circuit;
    }

    private static void Near(Complex expected, Complex actual, double tolerance = 1e-12) =>
        Assert.InRange(Complex.Abs(expected - actual), 0, tolerance);
}
