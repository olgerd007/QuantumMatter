# v1.8 authenticated PQC validation lab

Version **1.8.0** extends the [v1.7 experiments](cryptography.md) in the same
development branch. It adds an in-process authenticated-session experiment,
adversarial checks, strict CLI policies and a dedicated security CI job. Runtime
libraries retain net9.0 and their existing API inventories. No runtime dependency
or public SDK API is added. See the [release record](releases/1.8.0.md).

## Goal and trust model

The starting point is the ephemeral, unilaterally authenticated exchange in
[NIST SP 800-227, section 5.2.3](https://csrc.nist.gov/pubs/sp/800/227/final).
The client generates an ephemeral KEM key; the server encapsulates and signs the
transcript. This lab substitutes an explicitly provisioned signing-key pin for
certificate validation and adds bounded framing, key confirmation and records.

Those additions are our educational lab design. They are not an interoperable
standard, a reviewed production protocol, TLS, or NIST certification. There is no
socket listener or persistent key store. All providers are actual platform
cryptography implementations; there is no simulation of PQC primitives.

The client trusts an exact server name and ML-DSA verification key obtained
through an authenticated out-of-band provisioning step. The server is
authenticated; **the client is anonymous**. Possessing a session is not client
authorization. The experiment assumes uncompromised endpoints, trusted native
providers and cryptographic randomness. Pin provisioning/revocation, certificate
chains, endpoint compromise, side channels and network availability are outside
the implemented model.

An attacker may change, inject, reflect, reorder or replay any serialized message.
Checks exercise wrong signing keys, KEM key substitution, altered nonces,
identities, ciphertexts, signatures and confirmation tags. The expected result is
rejection without returning plaintext. No security strength or formal protocol
proof is inferred from these tests.

## Run it

```powershell
dotnet run --project experiments/QuantumMatter.CryptographyLab -c Release -- --session --strict --require ML-KEM-768 --require ML-DSA-65 --output artifacts/pqc-session.json
```

The command first runs the existing primitive checks and pinned NIST vectors.
The session experiment then checks a bidirectional record round trip and 17
adversarial cases. Its report contains status, the fixed suite, explicit trust
assumptions, successful directions and rejected-attack error codes. It contains
no keys, plaintext, handshake bytes or host-identifying details.

The suite is fixed: **ML-KEM-768, ML-DSA-65, HKDF-SHA256, HMAC-SHA256 and
AES-256-GCM**. There is no negotiation or silent algorithm fallback. Changing the
framing version or suite identifier aborts the attempt. Key sizes and algorithm
choices describe the experiment; they do not establish a combined security level.

## Handshake and serialization

Every message starts with the 8-byte header `QM18`, framing version 1, message
type, and big-endian 16-bit suite identifier 1. Handshakes have an additional
one-byte identity length and 1–64 ASCII letters, digits, dots, hyphens or
underscores. Names are exact, case-sensitive lab identities, not DNS validation.
All payload sizes are fixed and trailing bytes are rejected before cryptography.
The handshake input cap is 8 KiB.

| Message | Type | Remaining payload |
| --- | ---: | --- |
| Client hello | 1 | 32-byte client random; 1184-byte ephemeral encapsulation key |
| Server response | 2 | 32-byte server random; 1088-byte KEM ciphertext; 3309-byte signature; 32-byte server confirmation |
| Client confirmation | 3 | Header and 32-byte client confirmation only, without an identity field |
| Application record | 4 | Direction, sequence, ciphertext length, ciphertext and GCM tag; described below |

The signature input is the domain `QuantumMatter-v1.8-NIST-5.2.3-LAB`, followed
by three 32-bit big-endian length-prefixed fields: the complete client hello,
the unsigned server-response prefix through the ciphertext, and the provisioned
server verification key. It uses ML-DSA context `QM18-server`. The signature binds
both nonces, the ephemeral key, ciphertext, identity, framing and suite. A changed
client hello cannot be hidden by relaying a valid response for another hello.

Let H be SHA-256 of that signature input followed by the signature. HKDF-SHA256
uses the 32-byte KEM shared secret as input, H as salt, and distinct ASCII info
labels prefixed `QuantumMatter-v1.8/`:

- `client-traffic` and `server-traffic` produce separate 32-byte AES keys.
- `client-iv` and `server-iv` produce separate 12-byte nonce bases.
- `client-confirmation` and `server-confirmation` produce separate 32-byte HMAC keys.

The server confirmation is HMAC-SHA256 under its confirmation key over H. The
client verifies the pinned server signature before decapsulation, derives the
material, and verifies that confirmation using a fixed-time comparison. Its own
confirmation is HMAC-SHA256 under the client confirmation key over
`H || serverConfirmation`. The server returns a record channel only after
verifying this proof of matching key material. It does not identify the client.

A client obtains its channel after authenticating the server and generating its
own confirmation; the caller must deliver that confirmation to the server before
using application records. There is no extra server acknowledgement. Each pending
handshake accepts one completion attempt. Rejection or cancellation consumes it;
the ephemeral KEM key and temporary derived keys are disposed/cleared.

The server's signing key remains caller-owned and is borrowed only during response
creation. It is never exported as private bytes. Public hello/response properties
return copies. Session and pending-handshake objects are mutable and not thread-safe.

## Records and nonce policy

Records have a 21-byte header: the common 8 bytes, direction byte (client=0,
server=1), big-endian 64-bit sequence, and big-endian 32-bit ciphertext length.
Ciphertext is followed by a 16-byte GCM tag. Associated data is the complete record
header followed by H, binding every record to its direction and handshake.

Each direction starts its counter at zero. The nonce is its 12-byte derived base
with the sequence XORed into the last eight bytes in big-endian order. Counters
are private; callers cannot supply a nonce or reset a sequence. Distinct traffic
keys separate the directions. Empty records are supported, plaintext is limited
to 16 KiB, and each direction may emit at most 1024 records. Exhaustion aborts
the channel and requires a new handshake. This conservative lab limit is not a
cryptographic usage-bound proof or an implemented rekey protocol.

Receiving is strictly ordered. Duplicate, reflected, out-of-order or cross-session
records are rejected. A framing, sequence or authentication rejection disposes the
whole channel; no resynchronization is attempted. Failed plaintext buffers are
cleared and never returned. Successful plaintext is caller-owned and must be
handled appropriately. Provider contexts and owned nonce bases are cleared or
disposed when a channel is closed.

Cancellation is checked between provider operations. A bounded record encryption
or decryption and its counter update are atomic with respect to cancellation;
an operation already begun may complete. Pre-cancelled record calls leave their
counters unchanged. There is no hidden background work or Task.Run wrapper.

## Strict CLI and CI policy

Existing discovery behavior remains the default. New options are additive:

| Option | Behavior |
| --- | --- |
| `--strict` | All six default ML-KEM/ML-DSA parameter sets must be present and passed; selected SLH-DSA must pass too |
| `--require ALGORITHM` | Repeatable, case-insensitive known algorithm ID; missing, unsupported or unsuccessful required rows fail |
| `--require SLH-DSA-SHA2-128f` | Explicitly enables that optional experimental helper and requires its success |
| `--session` | Requires supported session providers and successful session/adversarial validation |

Reports are emitted before a policy failure so CI can inspect unsupported rows.
Requested report directories are created when needed, including fresh checkouts.
Automation must check exit status, not merely whether an older report file exists.
Exit codes are 0 for successful discovery/policy, 2 for invalid arguments, 3 for
unmet requirements, 4 for cryptographic/validation failure, 5 for a report-write
failure, and 130 for cancellation.
Cryptographic failures do not print provider exception details or a successful
report. Report-write failures withhold path details instead of printing an I/O
exception containing host paths.

The dedicated `pqc-security` job uses a digest-pinned official Microsoft SDK
container with a newer OpenSSL provider. It runs the focused protocol/parser/
policy tests, then runs `--session --strict`, and checks the generated report's
privacy before upload. The upload step requires a successful privacy check, even
when the strict run fails. Missing native PQC support fails the job. Container pins
require deliberate updates when provider/runtime fixes are adopted. Ordinary .NET 9/10 simulator CI
and the optional hardware workflow remain in place.

## Validation and limits

Tests include deterministic malformed-framing inputs, independent canonical-frame
predicates, pin snapshots, key ownership, session substitution, both directions,
confirmation reflection/replay, tampered headers/payloads/tags, zero/maximum-length
records, counter exhaustion, cancellation and disposal. Real KEM/signature tests
explicitly report unsupported providers on general CI; the dedicated strict job
requires actual execution. No socket deployment, mutual client authentication,
protocol interoperability, side-channel validation or full security certification
is claimed. Production use needs an established interoperable protocol and review.

The [recorded sample report](benchmarks/pqc-session-v1.8-2026-10-27.json) contains
the successful two-direction exchange and all 17 rejection outcomes. Timing
samples are anonymous local observations, not security-strength measurements.
