using QuantumMatter.Core;

namespace QuantumMatter.Demo.Cryptography;

// Bounded lab synthesis. All callers own distinct controls, targets and initially-zero scratch.
internal static class ReversibleGates
{
    internal static void MultiControlledX(QuantumCircuit circuit, int[] controls, int target, int[] scratch)
    {
        switch (controls.Length)
        {
            case 0: circuit.X(target); return;
            case 1: circuit.CNot(controls[0], target); return;
            case 2: circuit.Toffoli(controls[0], controls[1], target); return;
        }

        circuit.Toffoli(controls[0], controls[1], scratch[0]);
        for (int index = 2; index < controls.Length - 1; index++)
        {
            circuit.Toffoli(scratch[index - 2], controls[index], scratch[index - 1]);
        }

        circuit.Toffoli(scratch[controls.Length - 3], controls[^1], target);
        for (int index = controls.Length - 2; index >= 2; index--)
        {
            circuit.Toffoli(scratch[index - 2], controls[index], scratch[index - 1]);
        }

        circuit.Toffoli(controls[0], controls[1], scratch[0]);
    }
}
