---
title: Formats and encryption
description: The formats of the Outlook data files, the encryption of their blocks, and the password of a file.
---

[PstFile.Format](cref:Meziantou.Framework.Mail.PstFile.Format) gives the format of a file:

- [Ansi](cref:Meziantou.Framework.Mail.PstFormat.Ansi), the format of Outlook 97 to 2002: files up to 2 GB, strings in a
  code page.
- [Unicode](cref:Meziantou.Framework.Mail.PstFormat.Unicode), the format of Outlook 2003 and later.
- [Unicode4K](cref:Meziantou.Framework.Mail.PstFormat.Unicode4K), the format of the offline files (OST) of Outlook 2013 and
  later: pages of 4 KB and blocks compressed with zlib. [IsOfflineFile](cref:Meziantou.Framework.Mail.PstFile.IsOfflineFile)
  tells an OST from a PST.

## Encryption

[PstFile.Encryption](cref:Meziantou.Framework.Mail.PstFile.Encryption) gives how the data blocks are obfuscated:
[Permute](cref:Meziantou.Framework.Mail.PstEncryption.Permute) (the "compressible encryption" Outlook uses by default),
[Cyclic](cref:Meziantou.Framework.Mail.PstEncryption.Cyclic) ("high encryption") or
[None](cref:Meziantou.Framework.Mail.PstEncryption.None). None of them uses a key: the library reads and writes all of them
without a password.

## Password

The password of a file ([HasPassword](cref:Meziantou.Framework.Mail.PstStore.HasPassword)) is only a check that Outlook
does: the file stores a CRC of the password, and its content is not encrypted with it. The library reads a protected file
like any other one. Do not rely on the password of a PST file to protect its content.

The files that Windows Information Protection encrypts are not supported: an
[UnsupportedPstFeatureException](cref:Meziantou.Framework.Mail.UnsupportedPstFeatureException) reports them.
