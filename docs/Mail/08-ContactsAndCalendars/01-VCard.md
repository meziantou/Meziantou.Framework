---
title: vCard
description: Read and write vCards (.vcf) in the versions 2.1, 3.0 and 4.0, and convert them to and from the contacts of Outlook.
---

A vCard (`.vcf`) is a contact: [VCard](cref:Meziantou.Framework.Mail.VCard) reads and writes the versions 2.1, 3.0
([RFC 2426]) and 4.0 ([RFC 6350]). A file can contain several cards:

<!-- ref:../../../tests/Meziantou.Framework.Mail.Tests/CalendarTests.cs;region=VCard_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var card = VCard.Load(path).Single();
Assert.Equal("Mary Ann O'Brien", card.FormattedName);
Assert.Equal("Example, Ltd.", card.Organization);
Assert.Equal("mary@example.com", card.Emails.First(email => email.HasType("pref")).Value);
Assert.Equal("Springfield", card.Addresses.Single().Locality);

// The card in another version of the format
var text = card.ToString(VCardVersion.V40);
Assert.Contains("EMAIL;PREF=1;TYPE=internet,work:mary@example.com", text, StringComparison.Ordinal);
```
<!-- endref -->

- The common properties have members: the names (`FN`, `N`), the nicknames, the email addresses, the telephone numbers,
  the postal addresses and the URLs with their [types](cref:Meziantou.Framework.Mail.VCardValue.Types) (`work`, `home`,
  `cell`, `pref`...), the organization, the title, the role, the birthday, the note, the categories and the photo when
  it is in the card. The other properties are in [OtherProperties](cref:Meziantou.Framework.Mail.VCard.OtherProperties),
  with their groups and their parameters, and are written back.
- The reader decodes the texts (`\n`, `\,`, `\;`), the quoted-printable values of vCard 2.1 in their charset, the
  folded lines, the photos in base64 (`ENCODING=b`, `ENCODING=BASE64`, `data:` URIs) and the escapes of the parameters
  ([RFC 6868]). A file is read in UTF-8, or in Latin-1 when its bytes are not UTF-8.
- [ToString](cref:Meziantou.Framework.Mail.VCard.ToString) writes a card in any version: the types as `TEL;WORK;VOICE`
  in 2.1, `TYPE=work,voice` in 3.0 and 4.0 (with `PREF=1` in 4.0), the values that are not ASCII in quoted-printable in
  2.1, and the lines folded at 75 bytes. A card without a formatted name gets one from its other names, since vCard 3.0
  and 4.0 require it.

## Contacts of Outlook

[VCard.FromPst](cref:Meziantou.Framework.Mail.VCard.FromPst) creates the card of a
[PstContact](cref:Meziantou.Framework.Mail.PstContact), of an Outlook data file or of an `.msg` file, and
[ToPstContactBuilder](cref:Meziantou.Framework.Mail.VCard.ToPstContactBuilder) creates the builder of a contact to write
in one. A contact of Outlook has three email addresses, a business and a home postal address, and a business, home,
mobile and fax telephone number: the other values of a card are not converted.

[RFC 2426]: https://www.rfc-editor.org/rfc/rfc2426
[RFC 6350]: https://www.rfc-editor.org/rfc/rfc6350
[RFC 6868]: https://www.rfc-editor.org/rfc/rfc6868
