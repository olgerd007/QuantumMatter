using QuantumMatter.Core;

namespace QuantumMatter.Demo.Cryptography;

// Deliberately invertible three-bit toy cipher, useful only to inspect oracle mechanics.
internal static class ToyKeySearch
{
    internal const int PhysicalQubits = 7;

    internal static int Encrypt(int plaintext, int key)
    {
        ValidateWord(plaintext, nameof(plaintext));
        ValidateWord(key, nameof(key));
        int value = plaintext ^ key;
        value ^= ((value & 1) & ((value >> 1) & 1)) << 2;
        return (value >> 1) | ((value & 1) << 2);
    }

    internal static QuantumCircuit CreateOracle(int plaintext, int ciphertext, CancellationToken cancellationToken = default)
    {
        ValidateWord(plaintext, nameof(plaintext));
        ValidateWord(ciphertext, nameof(ciphertext));
        cancellationToken.ThrowIfCancellationRequested();
        var circuit = new QuantumCircuit(PhysicalQubits);
        AppendOracle(circuit, plaintext, ciphertext);
        return circuit;
    }

    internal static QuantumCircuit CreateSearch(int plaintext, int ciphertext, int iterations = 2,
        CancellationToken cancellationToken = default)
    {
        ValidateWord(plaintext, nameof(plaintext));
        ValidateWord(ciphertext, nameof(ciphertext));
        if (iterations is < 0 or > 64) { throw new ArgumentOutOfRangeException(nameof(iterations)); }
        cancellationToken.ThrowIfCancellationRequested();
        var circuit = new QuantumCircuit(PhysicalQubits);
        for (int q = 0; q < 3; q++) { circuit.H(q); }
        for (int round = 0; round < iterations; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppendOracle(circuit, plaintext, ciphertext);
            for (int q = 0; q < 3; q++) { circuit.H(q).X(q); }
            circuit.Toffoli(0, 1, 6).CZ(6, 2).Toffoli(0, 1, 6);
            for (int q = 0; q < 3; q++) { circuit.X(q).H(q); }
            // Preserve the diffuser's exact global sign: D = 2|s><s| - I.
            circuit.X(0).Z(0).X(0).Z(0);
        }

        return circuit;
    }

    private static void AppendOracle(QuantumCircuit circuit, int plaintext, int ciphertext)
    {
        for (int bit = 0; bit < 3; bit++)
        {
            if ((plaintext & (1 << bit)) != 0) { circuit.X(bit + 3); }
            circuit.CNot(bit, bit + 3);
        }

        circuit.Toffoli(3, 4, 5).Swap(3, 4).Swap(4, 5);
        for (int bit = 0; bit < 3; bit++)
        {
            if ((ciphertext & (1 << bit)) == 0) { circuit.X(bit + 3); }
        }

        circuit.Toffoli(3, 4, 6).CZ(6, 5).Toffoli(3, 4, 6);
        for (int bit = 0; bit < 3; bit++)
        {
            if ((ciphertext & (1 << bit)) == 0) { circuit.X(bit + 3); }
        }

        circuit.Swap(4, 5).Swap(3, 4).Toffoli(3, 4, 5);
        for (int bit = 2; bit >= 0; bit--)
        {
            circuit.CNot(bit, bit + 3);
            if ((plaintext & (1 << bit)) != 0) { circuit.X(bit + 3); }
        }
    }

    private static void ValidateWord(int word, string name)
    {
        if (word is < 0 or > 7) { throw new ArgumentOutOfRangeException(name); }
    }
}
