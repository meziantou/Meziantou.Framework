---
title: S/MIME
description: Check the signatures of signed email messages and decrypt encrypted ones with S/MIME.
---

S/MIME ([RFC 8551]) signs and encrypts email messages with the certificates of their senders and recipients.
[Smime](cref:Meziantou.Framework.Email.Smime) reads them: pass the [Body](cref:Meziantou.Framework.Email.MimeMessage.Body)
of a message, or any entity of a message.

<!-- ref:../../../tests/Meziantou.Framework.Email.Tests/SmimeTests.cs;region=Smime_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var message = MimeMessage.Load(bytes);
Assert.Equal(SmimeFormat.Encrypted, Smime.GetFormat(message.Body));

// Decrypt with the certificate of the recipient, which has its private key
var decrypted = Smime.Decrypt(message.Body, [certificate]);
Assert.Equal(SmimeFormat.ClearSigned, Smime.GetFormat(decrypted));

// Check the signature: verifySignatureOnly does not check the chain of the certificate
var signed = Smime.Verify(decrypted, verifySignatureOnly: true);
Assert.True(signed.IsValid);
Assert.Equal("alice@example.com", signed.Signatures.Single().Certificate?.GetNameInfo(X509NameType.EmailName, forIssuer: false));
Assert.Equal("Hello", ((MimePart)signed.Content).GetText().TrimEnd());
```
<!-- endref -->

[GetFormat](cref:Meziantou.Framework.Email.Smime.GetFormat) tells the [structure](cref:Meziantou.Framework.Email.SmimeFormat)
of an entity:

| Structure | Content type |
| --- | --- |
| Clear-signed | `multipart/signed` with `protocol="application/pkcs7-signature"`: the content, which any client can read, then the signature |
| Opaque-signed | `application/pkcs7-mime` with `smime-type=signed-data`: the content is inside the signature |
| Encrypted | `application/pkcs7-mime` with `smime-type=enveloped-data` |

A message that does not say its `smime-type` is recognized from the type of its content.

## Check a signature

[Verify](cref:Meziantou.Framework.Email.Smime.Verify) checks the signatures of a signed entity and returns its
[Content](cref:Meziantou.Framework.Email.SmimeSignedContent.Content), with a
[signature](cref:Meziantou.Framework.Email.SmimeSignature) per signer: its certificate, its signing time, and its
[Error](cref:Meziantou.Framework.Email.SmimeSignature.Error) when it is not valid. By default, the certificates of the
signers are checked too: their chain of trust, their validity and their usage; `verifySignatureOnly: true` checks only
that the content was signed by the keys of the certificates, and `extraStore` gives the certificates to build the chains.

A clear-signed content is checked on the bytes it was read from, with the white space the signature covers, such as
the white space after the boundary delimiters of a multipart: a change made to the parsed content after reading does
not change the result. A message stored with LF line breaks, such as in an mbox file, is checked in its canonical form
too, with CRLF line breaks.

## Decrypt a message

[Decrypt](cref:Meziantou.Framework.Email.Smime.Decrypt) decrypts an encrypted entity with the certificate of one of its
recipients, which has its private key; the certificates of the store of the current user are tried too. The decrypted
entity is often signed: check it with [Verify](cref:Meziantou.Framework.Email.Smime.Verify). To read a decrypted message
as a message, set it as the body: `message.Body = Smime.Decrypt(message.Body, certificates)`.

The library does not sign or encrypt messages, and does not decrypt the authenticated encryption of RFC 5083
(`authEnveloped-data`).

[RFC 8551]: https://www.rfc-editor.org/rfc/rfc8551
