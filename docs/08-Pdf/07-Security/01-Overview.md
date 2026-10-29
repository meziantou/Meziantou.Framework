---
title: Security
description: Protect documents with passwords and permissions, and sign them so that their readers know who wrote them and that they did not change.
---

| To | Use | Page |
| --- | --- | --- |
| Require a password to open a document, or restrict printing and copying | `PdfDocument.Encryption` | [Encryption](02-Encryption.md) |
| Open an encrypted document | `PdfReadOptions.Password` | [Encryption](02-Encryption.md) |
| Prove who approved a document, and that it did not change since | `PdfSigner` | [Digital signatures](03-DigitalSignatures.md) |
| Check the signatures of a document | `PdfSignatureVerifier` | [Digital signatures](03-DigitalSignatures.md) |

Encryption and signatures protect different things. Encryption hides the content from those who do not have the
password; its permissions are honored by the readers, not enforced by the file. A signature does not hide anything: it
proves that the signed bytes did not change, and binds them to the certificate of the signer, whom the application must
decide to trust.

## In this section

- [Encryption](02-Encryption.md): passwords, algorithms and permissions, and how to remove or change them.
- [Digital signatures](03-DigitalSignatures.md): PAdES signatures and time stamps, and their verification.
