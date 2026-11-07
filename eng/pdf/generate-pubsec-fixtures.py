#!/usr/bin/env python3
# Generates tests/Meziantou.Framework.Pdf.Tests/TestData/PubSec: test certificates and documents encrypted for them by
# the public-key security handler (Adobe.PubSec), written by pyHanko 0.33.0 (pip install pyhanko==0.33.0), a tool
# independent of Meziantou.Framework.Pdf.
#
# The source is the hand-authored fixture structure/simple.pdf ("Hello, World"). The certificates are created once: pass --certificates to replace them, which invalidates every
# document. Run from the root of the repository:
#
#   python3 eng/pdf/generate-pubsec-fixtures.py [--certificates]
#
# --verify <directory> checks instead that pyHanko decrypts the documents of a directory, written by
# Meziantou.Framework.Pdf for recipient1 (every permission) and recipient2, and rejects the certificate other:
#
#   python3 eng/pdf/generate-pubsec-fixtures.py --verify <directory>
#
# The documents differ at every run (random seeds and initialization vectors): the tests only decrypt them.
import datetime
import glob
import os
import sys

from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec, rsa
from cryptography.hazmat.primitives.serialization import pkcs12
from cryptography.x509.oid import NameOID

from pyhanko.keys import load_cert_from_pemder
from pyhanko.pdf_utils.crypt import PubKeySecurityHandler, SecurityHandlerVersion, SimpleEnvelopeKeyDecrypter
from pyhanko.pdf_utils.crypt.api import AuthStatus
from pyhanko.pdf_utils.crypt.permissions import PubKeyPermissions
from pyhanko.pdf_utils.reader import PdfFileReader
from pyhanko.pdf_utils.writer import copy_into_new_writer

ROOT = os.path.join("tests", "Meziantou.Framework.Pdf.Tests", "TestData", "PubSec")
FIXTURES = os.path.join("tests", "Meziantou.Framework.Pdf.Fixtures", "structure")
PFX_PASSWORD = b"test"


def create_certificate(name, key):
    subject = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, name)])
    now = datetime.datetime(2026, 1, 1, tzinfo=datetime.timezone.utc)
    certificate = (
        x509.CertificateBuilder()
        .subject_name(subject)
        .issuer_name(subject)
        .public_key(key.public_key())
        .serial_number(x509.random_serial_number())
        .not_valid_before(now)
        .not_valid_after(now + datetime.timedelta(days=36500))
        .add_extension(x509.SubjectKeyIdentifier.from_public_key(key.public_key()), critical=False)
        .add_extension(x509.KeyUsage(digital_signature=True, content_commitment=False, key_encipherment=True, data_encipherment=True, key_agreement=isinstance(key, ec.EllipticCurvePrivateKey), key_cert_sign=False, crl_sign=False, encipher_only=False, decipher_only=False), critical=True)
        .sign(key, hashes.SHA256())
    )
    return certificate


def write_certificates():
    for name, key in [
        ("recipient1", rsa.generate_private_key(public_exponent=65537, key_size=2048)),
        ("recipient2", rsa.generate_private_key(public_exponent=65537, key_size=2048)),
        ("other", rsa.generate_private_key(public_exponent=65537, key_size=2048)),
        ("ec", ec.generate_private_key(ec.SECP256R1())),
    ]:
        certificate = create_certificate("Meziantou.Framework.Pdf test " + name, key)
        with open(os.path.join(ROOT, name + ".cer"), "wb") as f:
            f.write(certificate.public_bytes(serialization.Encoding.DER))
        with open(os.path.join(ROOT, name + ".pfx"), "wb") as f:
            f.write(pkcs12.serialize_key_and_certificates(name.encode(), key, certificate, None, serialization.BestAvailableEncryption(PFX_PASSWORD)))


def certificate(name):
    return load_cert_from_pemder(os.path.join(ROOT, name + ".cer"))


def encrypt_pyhanko(source, output, handler):
    with open(os.path.join(FIXTURES, source), "rb") as f:
        writer = copy_into_new_writer(PdfFileReader(f))
    writer._assign_security_handler(handler)
    with open(os.path.join(ROOT, output), "wb") as f:
        writer.write(f)


def write_pyhanko():
    everything = PubKeyPermissions.allow_everything()
    encrypt_pyhanko("simple.pdf", "pyhanko-aes256.pdf", PubKeySecurityHandler.build_from_certs([certificate("recipient1")]))
    encrypt_pyhanko("simple.pdf", "pyhanko-aes256-nomac.pdf", PubKeySecurityHandler.build_from_certs([certificate("recipient1")], perms=everything | PubKeyPermissions.TOLERATE_MISSING_PDF_MAC, pdf_mac=False))
    encrypt_pyhanko("simple.pdf", "pyhanko-aes128.pdf", PubKeySecurityHandler.build_from_certs([certificate("recipient1")], version=SecurityHandlerVersion.RC4_OR_AES128, use_aes=True, keylen_bytes=16))
    encrypt_pyhanko("simple.pdf", "pyhanko-rc4-128.pdf", PubKeySecurityHandler.build_from_certs([certificate("recipient1")], version=SecurityHandlerVersion.RC4_OR_AES128, use_aes=False, keylen_bytes=16))
    encrypt_pyhanko("simple.pdf", "pyhanko-rc4-40-legacy.pdf", PubKeySecurityHandler.build_from_certs([certificate("recipient1")], version=SecurityHandlerVersion.RC4_40, use_aes=False, use_crypt_filters=False, keylen_bytes=5))

    # Two enveloped data: recipient1 may do everything, recipient2 only print
    handler = PubKeySecurityHandler.build_from_certs([certificate("recipient1")])
    handler.add_recipients([certificate("recipient2")], perms=PubKeyPermissions.ALLOW_PRINTING)
    encrypt_pyhanko("simple.pdf", "pyhanko-two-recipients.pdf", handler)

    # pyHanko leaves in clear the XMP packet it writes for PDF 2.0 (with the /Identity crypt filter), but encrypts the
    # metadata stream of the source document, against ISO 32000-2 (7.6.4.2): the source has none
    encrypt_pyhanko("simple.pdf", "pyhanko-metadata-in-clear.pdf", PubKeySecurityHandler.build_from_certs([certificate("recipient1")], encrypt_metadata=False))

    # An elliptic-curve recipient: key agreement, which the library does not support
    encrypt_pyhanko("simple.pdf", "pyhanko-ec.pdf", PubKeySecurityHandler.build_from_certs([certificate("ec")]))


def verify(directory):
    failures = 0
    for path in sorted(glob.glob(os.path.join(directory, "*.pdf"))):
        for name in ("recipient1", "recipient2", "other"):
            decrypter = SimpleEnvelopeKeyDecrypter.load_pkcs12(os.path.join(ROOT, name + ".pfx"), PFX_PASSWORD)
            with open(path, "rb") as f:
                reader = PdfFileReader(f)
                try:
                    result = reader.decrypt_pubkey(decrypter)
                except Exception as error:  # pyHanko raises when the certificate is not a recipient
                    result = error
                if name == "other":
                    ok = isinstance(result, Exception) or result.status == AuthStatus.FAILED
                    print(os.path.basename(path), name, "rejected" if ok else "accepted")
                else:
                    # The content stream and a string of each page are decrypted
                    ok = not isinstance(result, Exception) and result.status != AuthStatus.FAILED and len(reader.root["/Pages"]["/Kids"][0]["/Contents"].data) > 0
                    print(os.path.basename(path), name, result if isinstance(result, Exception) else result.permission_flags)
                failures += not ok
    print(f"{failures} failures")
    return failures


def main():
    if "--verify" in sys.argv:
        sys.exit(1 if verify(sys.argv[sys.argv.index("--verify") + 1]) else 0)

    os.makedirs(ROOT, exist_ok=True)
    if "--certificates" in sys.argv:
        write_certificates()
    write_pyhanko()


main()
