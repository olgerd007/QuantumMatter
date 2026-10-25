using System.Numerics;
using QuantumMatter.Application.Noise;
using QuantumMatter.Core;
using QuantumMatter.Core.Noise;
using QuantumMatter.Infrastructure;
using Xunit;

namespace QuantumMatter.Tests;

public sealed class NoiseDensityReferenceTests
{
    [Fact]
    public void Mixed_channel_circuit_agrees_with_an_independent_density_matrix_calculation()
    {
        double h = Math.Sqrt(0.5);
        var hadamard = new Complex[,] { { h, h }, { h, -h } };
        var cnot = new Complex[,] { { 1, 0, 0, 0 }, { 0, 0, 0, 1 }, { 0, 0, 1, 0 }, { 0, 1, 0, 0 } };
        var rho = new Complex[4, 4];
        rho[0, 0] = 1;
        rho = Transform(rho, Embed(hadamard, 0));
        rho = Transform(rho, cnot);
        var circuit = new NoisyCircuit(2).Append(new QuantumCircuit(2).H(0).CNot(0, 1));
        (NoiseKind Kind, double Probability, int Target)[] channels =
        [
            (NoiseKind.BitFlip, 0.2, 0), (NoiseKind.PhaseFlip, 0.35, 1),
            (NoiseKind.Depolarizing, 0.3, 0), (NoiseKind.AmplitudeDamping, 0.4, 1)
        ];
        foreach ((NoiseKind kind, double p, int target) in channels)
        {
            circuit.ApplyNoise(NoiseChannelTests.Channel(kind, p), target);
            var next = new Complex[4, 4];
            foreach (Complex[,] kraus in NoiseChannelTests.Kraus(kind, p))
            {
                Complex[,] branch = Transform(rho, Embed(kraus, target));
                for (int row = 0; row < 4; row++)
                {
                    for (int column = 0; column < 4; column++)
                    {
                        next[row, column] += branch[row, column];
                    }
                }
            }

            rho = next;
        }

        double cosine = Math.Cos(0.7 / 2), sine = Math.Sin(0.7 / 2);
        rho = Transform(rho, Embed(hadamard, 0));
        rho = Transform(rho, Embed(new Complex[,] { { cosine, -sine }, { sine, cosine } }, 1));
        circuit.Append(new QuantumCircuit(2).H(0).Ry(1, 0.7));
        var simulator = new NoisyStateVectorSimulator(new CpuStateVectorBackend(), new CpuNoiseChannelBackend());
        NoisyProbabilityResult estimate = simulator.EstimateProbabilities(circuit, 40000, seed: 271);
        double trace = 0;
        for (int index = 0; index < 4; index++)
        {
            trace += rho[index, index].Real;
            Assert.InRange(Math.Abs(rho[index, index].Imaginary), 0, 1e-12);
            Assert.InRange(Math.Abs(estimate.Estimate(index).Probability - rho[index, index].Real), 0, 0.02);
        }

        Assert.InRange(Math.Abs(trace - 1), 0, 1e-12);
    }

    private static Complex[,] Embed(Complex[,] local, int target)
    {
        var result = new Complex[4, 4];
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                bool sameSpectator = target == 0 ? row / 2 == column / 2 : row % 2 == column % 2;
                if (sameSpectator)
                {
                    result[row, column] = local[target == 0 ? row % 2 : row / 2, target == 0 ? column % 2 : column / 2];
                }
            }
        }

        return result;
    }

    private static Complex[,] Transform(Complex[,] density, Complex[,] matrix)
    {
        var result = new Complex[4, 4];
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                for (int left = 0; left < 4; left++)
                {
                    for (int right = 0; right < 4; right++)
                    {
                        result[row, column] += matrix[row, left] * density[left, right] * Complex.Conjugate(matrix[column, right]);
                    }
                }
            }
        }

        return result;
    }
}
