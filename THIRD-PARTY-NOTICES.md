# Third-party notices

QuantumMatter code remains under the existing MIT license in LICENSE.

## Runtime dependency

QuantumMatter.Infrastructure references ILGPU 1.5.3, a general GPU compiler/runtime
under the University of Illinois/NCSA Open Source License. ILGPU supplies no
quantum simulation implementation here. Its license and included third-party
notices are reproduced in licenses/ILGPU-LICENSE.txt and
licenses/ILGPU-LICENSE-3RD-PARTY.txt. It is restored as a dependency, not embedded
inside the QuantumMatter assemblies.

## Development dependencies

Microsoft.CodeAnalysis.PublicApiAnalyzers 3.3.4 is an MIT-licensed, build-only API
compatibility analyzer, marked PrivateAssets=all. The test SDK and xUnit packages
are development dependencies. None is a runtime dependency of the SDK packages.
Consult the restored packages' own license notices when redistributing tooling.

NVIDIA drivers are supplied separately by NVIDIA and are not redistributed here.

## NIST ACVP experiment fixtures

The standalone cryptography lab embeds selected public synthetic NIST ACVP-Server
test vectors from commit 975de31eb83d87039ec88934fdc47d8c312b892d. NIST is the
source; the full required notice is preserved in licenses/NIST-ACVP.txt. On
2026-10-26, the fixture subset was selected, fields renamed and unused data
removed; numerical values and expected results are unchanged. Provenance and
source hashes accompany the fixture in experiments/QuantumMatter.CryptographyLab/Vectors.
These fixtures add no runtime dependency to the SDK libraries.
