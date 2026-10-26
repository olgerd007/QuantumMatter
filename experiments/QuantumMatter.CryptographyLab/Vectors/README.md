# Selected NIST ACVP cases

Source: NIST's [ACVP-Server](https://github.com/usnistgov/ACVP-Server), pinned to
commit `975de31eb83d87039ec88934fdc47d8c312b892d`.

The original files are `gen-val/json-files/ML-KEM-encapDecap-FIPS203/internalProjection.json`
and `gen-val/json-files/ML-DSA-sigVer-FIPS204/internalProjection.json` at that commit.
`NistAcvp.json` records their SHA-256 hashes and the selected tgId/tcId values.

Modified on 2026-10-26: selected two cases per parameter set, renamed fields,
and removed unused data. Hexadecimal values and expected results are unchanged.
KEM cases include both valid and modified-ciphertext decapsulation. Signature
cases use the external, pure interface and include valid and invalid signatures,
with their original contexts. All values, including synthetic private KEM keys,
are public test fixtures and must never be used as operational keys.

NIST is explicitly acknowledged as the source. The complete required notice is
preserved in [licenses/NIST-ACVP.txt](../../../licenses/NIST-ACVP.txt).
This subset is an independent correctness check, not ACVP/CAVP certification.
