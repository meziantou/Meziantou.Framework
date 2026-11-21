---
title: Untrusted files
description: Read untrusted files safely with the limits of the reader, and check the structures of a file.
---

The reader checks every structure it reads: the checksums of the pages and the blocks, the bounds of the references and the
levels of the trees. It reports a malformed file with an
[InvalidEmailDataException](cref:Meziantou.Framework.Email.InvalidEmailDataException), and a feature it does not support with an
[UnsupportedEmailFeatureException](cref:Meziantou.Framework.Email.UnsupportedEmailFeatureException). Both derive from
[EmailException](cref:Meziantou.Framework.Email.EmailException).

## Limits

[PstReadOptions](cref:Meziantou.Framework.Email.Storage.PstReadOptions) limits the memory and the time a file can use: the size of a
value loaded in memory, the depth of the trees and of the embedded messages, and the number of pages and blocks kept in a
cache. A file that exceeds them throws an [EmailResourceLimitException](cref:Meziantou.Framework.Email.EmailResourceLimitException):

<!-- ref:../../../tests/Meziantou.Framework.Email.Tests/PstFileTests.cs;region=PstReadOptions_Limits;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new PstReadOptions
{
    MaxPropertySize = 16 * 1024 * 1024,
    MaxEmbeddedMessageDepth = 4,
};

try
{
    using var file = PstFile.Open(path, options);
    Assert.NotEmpty(file.RootFolder.Folders);
}
catch (EmailException)
{
    // InvalidEmailDataException, EmailResourceLimitException or UnsupportedEmailFeatureException
}
```
<!-- endref -->

The content of an attachment read with [OpenRead](cref:Meziantou.Framework.Email.Mapi.MapiAttachment.OpenRead) is not limited: it
is read block by block.

## Checking a file

[PstFile.Validate](cref:Meziantou.Framework.Email.Storage.PstFile.Validate) checks the whole file without stopping at the first
problem: the header, the B-trees, every block and every node, the heaps and the tables, and the allocation maps. It returns
the problems it finds:

<!-- ref:../../../tests/Meziantou.Framework.Email.Tests/PstFileTests.cs;region=PstFile_Validate;format=md-fenced-code-block;dedent=true -->
```csharp
using var file = PstFile.Open(path);
var issues = file.Validate();
Assert.Empty(issues);
```
<!-- endref -->

[VerifyChecksums](cref:Meziantou.Framework.Email.Storage.PstReadOptions.VerifyChecksums) turns off the checksums, to read what a
damaged file still contains.
