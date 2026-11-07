# Public-key encryption fixtures

Documents encrypted by the public-key security handler (`/Adobe.PubSec`, ISO 32000-2, 7.6.5) for test certificates,
written by [pyHanko](https://github.com/MatthiasValvekens/pyHanko) 0.33.0, a tool independent of
`Meziantou.Framework.Pdf`, from `tests/Meziantou.Framework.Pdf.Fixtures/structure/simple.pdf` ("Hello, World").
`eng/pdf/generate-pubsec-fixtures.py` writes them; the documents differ at every run (random seeds and vectors).

| File | Encryption |
| --- | --- |
| `pyhanko-aes256.pdf` | `adbe.pkcs7.s5`, AESV3, with a PDF MAC (ISO/TS 32004) |
| `pyhanko-aes256-nomac.pdf` | `adbe.pkcs7.s5`, AESV3 |
| `pyhanko-aes128.pdf` | `adbe.pkcs7.s5`, AESV2 |
| `pyhanko-rc4-128.pdf` | `adbe.pkcs7.s5`, V2 (RC4) with a 128-bit key |
| `pyhanko-rc4-40-legacy.pdf` | `adbe.pkcs7.s4`, version 1 (RC4 with a 40-bit key), no crypt filter |
| `pyhanko-two-recipients.pdf` | AESV3, two enveloped data: `recipient1` may do everything, `recipient2` only print |
| `pyhanko-metadata-in-clear.pdf` | AESV3, `/EncryptMetadata false` |
| `pyhanko-ec.pdf` | AESV3 for the elliptic-curve certificate `ec` (key agreement, not supported) |

The certificates are self-signed test certificates created for these tests (`.cer`: the certificate; `.pfx`: the
certificate and its private key, password `test`). `other` is a recipient of no document. The files are `CC0-1.0`.
