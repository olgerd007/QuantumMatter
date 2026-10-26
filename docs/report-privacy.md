# Public report privacy

Reports in this repository are anonymous by default. Do not publish CPU/GPU
models, detected processor counts, physical RAM/VRAM capacities, device identifiers,
driver/OS installation details, native-provider fingerprints or absolute host paths.
This applies to documentation, JSON metadata, captured logs and CI artifacts.

Benchmark parameters remain useful without identifying the host: requested backend
and kernel, worker limit, problem size, circuit depth, precision, array budgets,
timings, allocations and numerical errors may be retained. Requested settings and
algorithm memory requirements describe the workload, not physical hardware.
Anonymized results support comparisons within their recorded run; they are not
machine-specific recommendations or guarantees for another environment.

Benchmark and PQC tools omit host identity instead of collecting it and relying on
later redaction. Normal package-consumer output also omits device and installation
details. Runtime device-discovery APIs remain available to local callers.

Keep local diagnostic captures only in the ignored artifacts/ directory. Do not
add them to commits or upload them as public workflow artifacts. Existing current
reports have been anonymized; that does not rewrite earlier Git history or alter
previously published packages/artifacts.

Run the same check used by CI before publishing:

```powershell
./scripts/Test-ReportPrivacy.ps1
# Also inspect generated reports kept outside the versioned docs directory:
./scripts/Test-ReportPrivacy.ps1 -ReportPaths docs,artifacts/my-report.json
```

The guard rejects known host-identifying metadata fields and machine descriptions,
and checks reporting/sample sources for identity capture. It reports only file or
field names on failure, withholding matched values from logs. Review new report
formats against this policy; pattern checks cannot identify every possible private
field name or phrasing.
