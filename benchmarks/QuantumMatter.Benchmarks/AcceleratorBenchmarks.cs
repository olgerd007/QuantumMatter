using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using QuantumMatter.Core;
using QuantumMatter.Infrastructure;

internal static class AcceleratorBenchmarks
{
    internal static int Run(string outputPath)
    {
        const int repetitions = 5;
        int[] sizes = [12, 20, 25];
        string? configuredBudget = Environment.GetEnvironmentVariable("QUANTUMMATTER_BENCHMARK_MEMORY_BUDGET_BYTES");
        long arrayBudget = configuredBudget is null ? 2L << 30 : long.Parse(configuredBudget, CultureInfo.InvariantCulture);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(arrayBudget);
        // Three harness arrays plus one CUDA staging array, before any state/device allocation.
        foreach (int qubits in sizes)
        {
            long required = 4 * StateVector.RequiredBytes(qubits);
            if (required > arrayBudget)
            {
                throw new ArgumentOutOfRangeException(nameof(outputPath),
                    $"The {qubits}-qubit accelerator case requires {required} host-array bytes; budget is {arrayBudget} bytes.");
            }
        }

        using var gpu = new CudaStateVectorBackend(new CudaBackendOptions { EnableProfiling = true });
        var configurations = new List<Configuration>();
        foreach (int workers in new[] { 1, 4, 8 })
        {
            foreach (bool blocking in new[] { false, true })
            {
                foreach (bool wide in new[] { false, true })
                {
                    configurations.Add(new Configuration($"cpu-{(wide ? 512 : 256)}-w{workers}-block-{blocking.ToString().ToLowerInvariant()}",
                        new ParallelCpuStateVectorBackend(new ParallelCpuOptions
                        {
                            MaxDegreeOfParallelism = workers,
                            MinimumParallelQubits = 1,
                            EnableSimd = true,
                            EnableAvx512 = wide,
                            EnableGateBlocking = blocking
                        }), workers, wide, blocking));
                }
            }
        }

        configurations.Add(new Configuration("cuda", gpu, 0, false, false));
        string output = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output + ".metadata.json", JsonSerializer.Serialize(new
        {
            timestampUtc = TimeProvider.System.GetUtcNow(),
            privacy = "Anonymous benchmark; host hardware and installation details are not collected.",
            tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "runtime default",
            precision = "double real/imaginary; no fast math, coefficient fusion or renormalization",
            sizes,
            depths = "64 gates for 12/20 qubits; 8 gates for 25 qubits",
            mixes = "hadamard-high, mixed-high, varying-high; plus rx-high and phase-high at 20 qubits",
            repetitions,
            warmupsPerBackendAndCase = 2,
            configurations = configurations.Select(entry => new { entry.Name, entry.Workers, entry.Wide, entry.Blocking }),
            forcedParallelThresholdQubits = 1,
            benchmarkHostArrayBudgetBytes = arrayBudget,
            maximumLiveHostArrayBytes = 4 * StateVector.RequiredBytes(sizes.Max()),
            totalTiming = "Ordinary synchronous Execute; CUDA includes staging allocation/conversion, device allocation/free, transfers, dispatch/sync and host commit; events not created in timed Execute",
            deviceTiming = "Median of five separate post-timing ExecuteProfiled calls; event sequence excludes transfers/allocation but includes launch gaps and 32-launch barriers; not subtracted from ordinary Execute",
            excluded = "Context/kernel compilation, input reset, warmup and numerical validation",
            ordering = "Rotate all CPU/CUDA variants each repetition; reverse on odd repetitions",
            allocations = "Process-wide managed allocation delta, including CPU workers; excludes stack/native/device memory",
            workingSet = "Cumulative process peak, not an isolated backend peak",
            cpuMemory = "16N state; blocked CPU adds fixed 32-gate metadata and up to 16 KiB stack per active worker",
            cudaMemory = "Additional 16N staging + 16N device state; runtime/driver/context overhead separate",
            validation = "Independent scalar oracle; amplitude <=3e-12, compensated norm error <=1e-11; warmups and every timed/profiled output checked",
            source = "Local working tree; see companion report"
        }, new JsonSerializerOptions { WriteIndented = true }));

        using var writer = new StreamWriter(output);
        writer.WriteLine("backend,qubits,gates,mix,max_workers,avx512_requested,gate_blocking,median_ms,min_ms,max_ms,median_allocated_bytes,state_bytes,device_state_bytes,host_staging_bytes,profiled_device_median_ms,process_peak_working_set_bytes,max_amplitude_error,max_norm_error");
        using Process process = Process.GetCurrentProcess();
        foreach (int qubits in sizes)
        {
            string[] mixes = qubits == 20
                ? ["hadamard-high", "mixed-high", "varying-high", "rx-high", "phase-high"]
                : ["hadamard-high", "mixed-high", "varying-high"];
            int depth = qubits == 25 ? 8 : 64;
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
                new CpuStateVectorBackend().Execute(circuit, expected);
                var state = new Complex[input.Length];
                var samples = configurations.Select(_ => new List<Sample>()).ToArray();
                var errors = new double[configurations.Count];
                var norms = new double[configurations.Count];
                for (int config = 0; config < configurations.Count; config++)
                {
                    for (int warmup = 0; warmup < 2; warmup++)
                    {
                        input.CopyTo(state, 0);
                        configurations[config].Backend.Execute(circuit, state);
                        Check(config);
                    }
                }

                for (int iteration = 0; iteration < repetitions; iteration++)
                {
                    for (int offset = 0; offset < configurations.Count; offset++)
                    {
                        int config = (iteration + offset) % configurations.Count;
                        if ((iteration & 1) != 0) { config = configurations.Count - 1 - config; }
                        input.CopyTo(state, 0);
                        long allocation = GC.GetTotalAllocatedBytes(precise: true);
                        long start = Stopwatch.GetTimestamp();
                        configurations[config].Backend.Execute(circuit, state);
                        double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                        allocation = GC.GetTotalAllocatedBytes(precise: true) - allocation;
                        samples[config].Add(new Sample(elapsed, allocation));
                        Check(config);
                    }
                }

                var deviceTimes = new double[repetitions];
                for (int iteration = 0; iteration < repetitions; iteration++)
                {
                    input.CopyTo(state, 0);
                    deviceTimes[iteration] = gpu.ExecuteProfiled(circuit, state).DeviceEvolutionTime.TotalMilliseconds;
                    Check(configurations.Count - 1);
                }

                Array.Sort(deviceTimes);
                for (int config = 0; config < configurations.Count; config++)
                {
                    Configuration entry = configurations[config];
                    double[] times = samples[config].Select(sample => sample.Milliseconds).Order().ToArray();
                    long[] allocations = samples[config].Select(sample => sample.AllocatedBytes).Order().ToArray();
                    bool cuda = entry.Name == "cuda";
                    long bytes = StateVector.RequiredBytes(qubits);
                    process.Refresh();
                    object[] row = [entry.Name, qubits, depth, mix, entry.Workers, entry.Wide, entry.Blocking,
                        times[2], times[0], times[^1], allocations[2], bytes, cuda ? bytes : 0, cuda ? bytes : 0,
                        cuda ? deviceTimes[2] : 0, process.PeakWorkingSet64, errors[config], norms[config]];
                    writer.WriteLine(string.Join(",", row.Select(value => Convert.ToString(value, CultureInfo.InvariantCulture))));
                    Console.WriteLine(FormattableString.Invariant($"{qubits}q {depth} gates {mix} {entry.Name}: {times[2]:F4} ms"));
                }

                writer.Flush();
                GC.Collect();

                void Check(int config)
                {
                    (double error, double norm) = CudaBenchmarks.Check(expected, state);
                    errors[config] = Math.Max(errors[config], error);
                    norms[config] = Math.Max(norms[config], norm);
                }
            }
        }

        return 0;
    }

    private static QuantumCircuit Circuit(int qubits, int depth, string mix)
    {
        if (mix == "varying-high") { return CudaBenchmarks.Circuit(qubits, depth, "mixed-high"); }
        int target = qubits - 1;
        int control = qubits / 2;
        var circuit = new QuantumCircuit(qubits);
        double h = Math.Sqrt(0.5);
        var unitary = SingleQubitUnitary.Create(h, Complex.ImaginaryOne * h, Complex.ImaginaryOne * h, h);
        for (int index = 0; index < depth; index++)
        {
            if (mix == "hadamard-high") { circuit.H(target); continue; }
            if (mix == "rx-high") { circuit.Rx(target, 0.37); continue; }
            if (mix == "phase-high") { circuit.Phase(target, -0.29); continue; }
            switch (index % 8)
            {
                case 0: circuit.H(target); break;
                case 1: circuit.Rx(target, 0.37); break;
                case 2: circuit.Ry(target, -0.71); break;
                case 3: circuit.Rz(target, 1.19); break;
                case 4: circuit.CNot(control, target); break;
                case 5: circuit.Swap(target, (target + 1) % qubits); break;
                case 6: circuit.CS(control, target); break;
                case 7: circuit.U(target, unitary); break;
            }
        }

        return circuit;
    }

    private sealed record Configuration(string Name, IStateVectorBackend Backend, int Workers, bool Wide, bool Blocking);
}
