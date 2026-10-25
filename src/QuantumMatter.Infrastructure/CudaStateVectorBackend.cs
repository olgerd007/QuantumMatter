using System.Numerics;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using QuantumMatter.Core;

namespace QuantumMatter.Infrastructure;

/// <summary>Double-precision NVIDIA execution. Owns its context; dispose after use.</summary>
public sealed class CudaStateVectorBackend : IStateVectorBackend, IDisposable
{
    private readonly CudaBackendOptions _options;
    private readonly Context _context;
    private readonly CudaAccelerator _accelerator;
    private readonly Action<AcceleratorStream, Index1D, ArrayView1D<DeviceComplex, Stride1D.Dense>, DeviceGate> _apply;
    private readonly SemaphoreSlim _execution = new(1, 1);
    private bool _disposed;

    /// <summary>Owns a context on an explicitly selected compatible device and compiles the double-precision kernel.</summary>
    /// <exception cref="PlatformNotSupportedException">CUDA is unavailable for this process.</exception>
    public CudaStateVectorBackend(CudaBackendOptions? options = null)
    {
        _options = options ?? new CudaBackendOptions();
        _options.Validate();
        _context = CreateContext(_options.EnableProfiling);
        CudaAccelerator? accelerator = null;
        bool initialized = false;
        try
        {
            var devices = _context.GetCudaDevices();
            if (devices.Count == 0)
            {
                throw new PlatformNotSupportedException(
                    "No compatible NVIDIA CUDA device/driver was found. Select a CPU backend explicitly or install a compatible NVIDIA driver.");
            }

            if (_options.DeviceIndex >= devices.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "CUDA device index is not available.");
            }

            CudaDevice device = devices[_options.DeviceIndex];
            accelerator = device.CreateCudaAccelerator(_context);
            _apply = accelerator.LoadAutoGroupedKernel<Index1D, ArrayView1D<DeviceComplex, Stride1D.Dense>, DeviceGate>(
                CudaStateVectorKernel.Apply);
            _accelerator = accelerator;
            Device = Describe(device, _options.DeviceIndex);
            initialized = true;
        }
        finally
        {
            if (!initialized)
            {
                try
                {
                    accelerator?.Dispose();
                }
                finally
                {
                    _context.Dispose();
                }
            }
        }
    }

    /// <summary>The selected device's immutable description.</summary>
    public CudaDeviceInfo Device { get; }

    /// <summary>Returns compatible devices; an empty list means CUDA is unavailable. Never selects a CPU device.</summary>
    public static IReadOnlyList<CudaDeviceInfo> GetDevices()
    {
        if (!Environment.Is64BitProcess || !(OperatingSystem.IsWindows() || OperatingSystem.IsLinux()))
        {
            return Array.Empty<CudaDeviceInfo>();
        }

        using Context context = CreateContext(profiling: false);
        var result = new List<CudaDeviceInfo>();
        foreach (CudaDevice device in context.GetCudaDevices())
        {
            result.Add(Describe(device, result.Count));
        }

        return result.AsReadOnly();
    }

    /// <inheritdoc />
    public void Execute(QuantumCircuit circuit, Memory<Complex> amplitudes, CancellationToken cancellationToken = default) =>
        ExecuteCore(circuit, amplitudes, profile: false, cancellationToken);

    /// <summary>Evolves in place and reports event timing; requires EnableProfiling at construction.</summary>
    public CudaExecutionProfile ExecuteProfiled(QuantumCircuit circuit, Memory<Complex> amplitudes,
        CancellationToken cancellationToken = default)
    {
        if (!_options.EnableProfiling)
        {
            throw new InvalidOperationException("EnableProfiling must be selected when creating the CUDA backend.");
        }

        return ExecuteCore(circuit, amplitudes, profile: true, cancellationToken)!;
    }

    private CudaExecutionProfile? ExecuteCore(QuantumCircuit circuit, Memory<Complex> amplitudes,
        bool profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        if (amplitudes.Length != 1 << circuit.QubitCount)
        {
            throw new ArgumentException("Buffer length must equal 2^qubitCount.", nameof(amplitudes));
        }

        cancellationToken.ThrowIfCancellationRequested();
        long bytes = 16L * amplitudes.Length;
        // Serialize calls on this instance: one resident state and staging array fit the configured budgets.
        _execution.Wait(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (bytes > _options.DeviceMemoryBudgetBytes || bytes > Device.MemoryBytes
                || bytes > _options.StagingMemoryBudgetBytes || amplitudes.Length > Array.MaxLength)
            {
                throw new InvalidOperationException("CUDA state exceeds the device or host staging memory budget.");
            }

            var staging = new DeviceComplex[amplitudes.Length];
            ReadOnlySpan<Complex> input = amplitudes.Span;
            for (int index = 0; index < staging.Length; index++)
            {
                if ((index & 4095) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                staging[index] = DeviceComplex.FromComplex(input[index]);
            }

            using var buffer = _accelerator.Allocate1D<DeviceComplex>(staging.Length);
            using AcceleratorStream stream = _accelerator.CreateStream();
            try
            {
                buffer.View.CopyFromCPU(stream, staging);
                using ProfilingMarker? start = profile ? stream.AddProfilingMarker() : null;
                int launches = 0;
                foreach (GateOperation operation in circuit.Operations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    PreparedStateVectorGate gate = PreparedStateVectorGate.Prepare(operation);
                    if (gate.Kernel == StateVectorKernelKind.Identity)
                    {
                        continue;
                    }

                    _apply(stream, staging.Length / 2, buffer.View, new DeviceGate(gate));
                    // Bound queued work and cancellation latency; an active kernel cannot be interrupted.
                    if ((++launches & 31) == 0)
                    {
                        stream.Synchronize();
                    }
                }

                using ProfilingMarker? end = profile ? stream.AddProfilingMarker() : null;
                stream.Synchronize();
                cancellationToken.ThrowIfCancellationRequested();
                TimeSpan elapsed = profile ? end! - start! : TimeSpan.Zero;
                buffer.View.CopyToCPU(stream, staging);
                cancellationToken.ThrowIfCancellationRequested();
                // Conversion cannot fail. Commit after successful device completion and cancellation checks.
                Span<Complex> output = amplitudes.Span;
                cancellationToken.ThrowIfCancellationRequested();
                for (int index = 0; index < staging.Length; index++)
                {
                    output[index] = staging[index].ToComplex();
                }

                return profile ? new CudaExecutionProfile(elapsed, bytes, launches) : null;
            }
            finally
            {
                // No queued operation may outlive the buffer, including cancellation and launch failures.
                stream.Synchronize();
            }
        }
        finally
        {
            _execution.Release();
        }
    }

    /// <summary>Waits for active work and releases context/device resources. Repeated disposal is safe.</summary>
    public void Dispose()
    {
        _execution.Wait();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _accelerator.Dispose();
            }
            finally
            {
                _context.Dispose();
            }
        }
        finally
        {
            // Keep the semaphore alive so concurrent/late calls observe ObjectDisposedException reliably.
            _execution.Release();
        }
    }

    private static Context CreateContext(bool profiling)
    {
        if (!Environment.Is64BitProcess || !(OperatingSystem.IsWindows() || OperatingSystem.IsLinux()))
        {
            throw new PlatformNotSupportedException("CUDA requires a supported 64-bit Windows or Linux process.");
        }

        return Context.Create(builder =>
        {
            builder.Cuda().Math(MathMode.Default).Optimize(OptimizationLevel.O2);
            if (profiling)
            {
                builder.Profiling();
            }
        });
    }

    private static CudaDeviceInfo Describe(CudaDevice device, int index) =>
        new(index, device.Name, device.Architecture.ToString()!, device.DriverVersion.ToString(), device.MemorySize);
}
