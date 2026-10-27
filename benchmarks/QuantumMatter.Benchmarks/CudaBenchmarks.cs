using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using QuantumMatter.Core;
using QuantumMatter.Infrastructure;

internal static class CudaBenchmarks
{
    internal static int Run(string outputPath)
    {
        const int repetitions = 5;
        using var gpu = new CudaStateVectorBackend(new CudaBackendOptions { EnableProfiling = true });
        var cpu = new CpuStateVectorBackend();
        int[] sizes = [12, 18, 22];
        int[] depths = [16, 64];
        string[] mixes = ["hadamard-low", "hadamard-high", "mixed-low", "mixed-high"];
        string output = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var metadata = new
        {
            timestampUtc = TimeProvider.System.GetUtcNow(),
            privacy = "Anonymous benchmark; host hardware and installation details are not collected.",
            tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "runtime default",
            precision = "IEEE double real and imaginary; ILGPU 1.5.3 MathMode.Default; no renormalization",
            sizes,
            depths,
            mixes,
            repetitions,
            warmupsPerCase = 2,
            totalTiming = "ExecuteProfiled including staging conversion, allocation/free, upload, gate dispatch/sync, download, commit and CUDA event overhead; excludes context/JIT, input reset and oracle checks",
            deviceTiming = "CUDA event interval around gate sequence; excludes transfer and allocation; includes launch gaps and synchronization every 32 gates; not summed isolated kernel durations",
            allocations = "Calling-thread managed allocation; includes staging array, excludes driver/context/native and device memory",
            normSummation = "Kahan compensated sum of squared real and imaginary components; avoids large-register summation drift",
            memory = "One 16N device state + one 16N host staging array; context/driver metadata separate; no amplitude scratch arrays"
        };
        File.WriteAllText(output + ".metadata.json", JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        using var writer = new StreamWriter(output);
        writer.WriteLine("qubits,depth,mix,repetitions,cpu_median_ms,gpu_total_min_ms,gpu_total_median_ms,gpu_total_max_ms,gpu_device_min_ms,gpu_device_median_ms,gpu_device_max_ms,gpu_total_speedup,device_state_bytes,host_staging_bytes,gpu_managed_allocated_median_bytes,max_amplitude_error,norm_error");
        foreach (int qubits in sizes)
        {
            foreach (int depth in depths)
            {
                foreach (string mix in mixes)
                {
                    QuantumCircuit circuit = Circuit(qubits, depth, mix);
                    var input = new Complex[1 << qubits];
                    double scale = 1 / Math.Sqrt(input.Length);
                    for (int index = 0; index < input.Length; index++)
                    {
                        input[index] = Complex.FromPolarCoordinates(scale, index * 0.17);
                    }

                    var expected = (Complex[])input.Clone();
                    cpu.Execute(circuit, expected);
                    var state = new Complex[input.Length];
                    for (int warmup = 0; warmup < 2; warmup++)
                    {
                        input.CopyTo(state, 0);
                        cpu.Execute(circuit, state);
                        Check(expected, state);
                        input.CopyTo(state, 0);
                        gpu.ExecuteProfiled(circuit, state);
                        Check(expected, state);
                    }

                    var cpuTimes = new double[repetitions];
                    var totals = new double[repetitions];
                    var device = new double[repetitions];
                    var allocated = new double[repetitions];
                    double maxError = 0;
                    double normError = 0;
                    for (int iteration = 0; iteration < repetitions; iteration++)
                    {
                        // Alternate CPU/GPU order, excluding input restoration from both timing intervals.
                        for (int pass = 0; pass < 2; pass++)
                        {
                            input.CopyTo(state, 0);
                            bool runGpu = ((iteration + pass) & 1) != 0;
                            long before = GC.GetAllocatedBytesForCurrentThread();
                            long start = Stopwatch.GetTimestamp();
                            if (runGpu)
                            {
                                CudaExecutionProfile profile = gpu.ExecuteProfiled(circuit, state);
                                totals[iteration] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                                device[iteration] = profile.DeviceEvolutionTime.TotalMilliseconds;
                                allocated[iteration] = GC.GetAllocatedBytesForCurrentThread() - before;
                            }
                            else
                            {
                                cpu.Execute(circuit, state);
                                cpuTimes[iteration] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                            }

                            (double error, double norm) = Check(expected, state);
                            maxError = Math.Max(maxError, error);
                            normError = Math.Max(normError, norm);
                        }
                    }

                    Array.Sort(cpuTimes);
                    Array.Sort(totals);
                    Array.Sort(device);
                    Array.Sort(allocated);
                    long bytes = 16L * input.Length;
                    object[] row = [qubits, depth, mix, repetitions, cpuTimes[2], totals[0], totals[2], totals[^1],
                        device[0], device[2], device[^1], cpuTimes[2] / totals[2], bytes, bytes, allocated[2], maxError, normError];
                    writer.WriteLine(string.Join(",", row.Select(value => Convert.ToString(value, CultureInfo.InvariantCulture))));
                    writer.Flush();
                    Console.WriteLine($"{qubits}q {depth} gates {mix}: CPU {cpuTimes[2]:F3} ms; CUDA total {totals[2]:F3} ms, device {device[2]:F3} ms");
                }
            }
        }

        return 0;
    }

    internal static QuantumCircuit Circuit(int qubits, int depth, string mix)
    {
        var circuit = new QuantumCircuit(qubits);
        bool high = mix.EndsWith("high", StringComparison.Ordinal);
        for (int index = 0; index < depth; index++)
        {
            int target = high ? qubits - 1 - index % 3 : index % 3;
            int control = (target + 1) % qubits;
            if (mix.StartsWith("hadamard", StringComparison.Ordinal))
            {
                circuit.H(target);
                continue;
            }

            switch (index % 8)
            {
                case 0: circuit.H(target); break;
                case 1: circuit.Rx(target, 0.37); break;
                case 2: circuit.CNot(control, target); break;
                case 3: circuit.Ry(target, -0.71); break;
                case 4: circuit.CRz(control, target, 1.19); break;
                case 5: circuit.Swap(target, control); break;
                case 6: circuit.Phase(target, -0.29); break;
                case 7: circuit.Toffoli(control, (target + 2) % qubits, target); break;
            }
        }

        return circuit;
    }

    internal static (double Error, double NormError) Check(Complex[] expected, Complex[] actual)
    {
        double maxError = 0;
        double norm = 0;
        double compensation = 0;
        for (int index = 0; index < actual.Length; index++)
        {
            maxError = Math.Max(maxError, Complex.Abs(expected[index] - actual[index]));
            double probability = actual[index].Real * actual[index].Real + actual[index].Imaginary * actual[index].Imaginary;
            double corrected = probability - compensation;
            double next = norm + corrected;
            compensation = (next - norm) - corrected;
            norm = next;
        }

        double normError = Math.Abs(norm - 1);
        if (!double.IsFinite(maxError) || !double.IsFinite(normError) || maxError > 3e-12 || normError > 1e-11)
        {
            throw new InvalidOperationException($"Backend benchmark failed numerical validation: amplitude {maxError:R}, norm {normError:R}.");
        }

        return (maxError, normError);
    }
}
