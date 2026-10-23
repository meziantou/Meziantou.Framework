# Meziantou.Framework.Pdf.Signing

Signs and verifies the signatures of `Meziantou.Framework.Pdf` documents. It is a separate package because it needs
`System.Security.Cryptography.Pkcs`.

These instructions complete the repository-level `AGENTS.md` and the ones of `Meziantou.Framework.Pdf`. The user guide
is [readme.md](readme.md): keep the code, the XML documentation and `readme.md` consistent in the same change.

## Hard constraints

- **Clean room**, as `Meziantou.Framework.Pdf`: write the code from ISO 32000-2, ETSI EN 319 142-1 (PAdES), RFC 5652
  (CMS), RFC 5035 (ESS) and RFC 3161. Apache PDFBox and Poppler are test oracles only.
- The library uses internal types of `Meziantou.Framework.Pdf` (the reader options, the dates), through
  `InternalsVisibleTo`, with its exact version (`UseExactProjectReferenceVersions`).
- The signature is appended as an incremental update: the bytes of the file it is added to never change, so that the
  earlier signatures stay valid. The byte ranges and the signature are written in place of placeholders of fixed
  widths; the writer of `Meziantou.Framework.Pdf` does not encrypt the `/Contents` of a signature dictionary.
- PAdES baseline: the signed attributes are content-type, message-digest and ESS signing-certificate-v2; the
  signing-time attribute is not written (the time is `/M`).

## Tests

- `tests/Meziantou.Framework.Pdf.Signing.Tests`: certificates created by the tests and a time stamp authority of the
  tests (an `HttpMessageHandler`), no network.
- `tests/Meziantou.Framework.Pdf.InteropTests` (`SignatureInteropTests`): Poppler's pdfsig checks the signatures; the
  pinned release has no pdfsig for Windows, so the tests run on Linux and macOS.
- `tests/Meziantou.Framework.Pdf.Corpus`: the signed files of the corpora are verified (`expectations/signatures.txt`),
  and the documents of the signature tests of Apache PDFBox are signed with their keystore.
