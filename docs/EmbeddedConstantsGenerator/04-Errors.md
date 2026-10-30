---
title: Errors
description: The MSBuild errors reported by the task, and how to fix them.
---

The task validates the configuration and the files before it writes anything. When it finds a problem, it reports an
MSBuild error and the build stops: the generated file is not written.

| ID | Title |
| --- | --- |
| [MFECG0001](#mfecg0001-embedded-constants-namespace-is-invalid) | Embedded constants namespace is invalid |
| [MFECG0002](#mfecg0002-embedded-constants-class-name-is-invalid) | Embedded constants class name is invalid |
| [MFECG0003](#mfecg0003-embedded-constant-kind-is-missing) | Embedded constant kind is missing |
| [MFECG0004](#mfecg0004-embedded-constant-kind-is-invalid) | Embedded constant kind is invalid |
| [MFECG0005](#mfecg0005-embedded-constant-member-name-is-duplicated) | Embedded constant member name is duplicated |
| [MFECG0006](#mfecg0006-embedded-constant-file-cannot-be-read) | Embedded constant file cannot be read |
| [MFECG0007](#mfecg0007-embedded-constant-text-file-is-not-valid-utf-8) | Embedded constant text file is not valid UTF-8 |
| [MFECG0008](#mfecg0008-embedded-constant-text-file-is-too-large) | Embedded constant text file is too large |
| [MFECG0009](#mfecg0009-embedded-constants-visibility-is-invalid) | Embedded constants visibility is invalid |
| [MFECG0010](#mfecg0010-embedded-constant-member-name-is-the-same-as-the-generated-class-name) | Embedded constant member name is the same as the generated class name |
| [MFECG0011](#mfecg0011-the-embedded-constants-could-not-be-computed-or-written) | The embedded constants could not be computed or written |

## MFECG0001: Embedded constants namespace is invalid

The namespace is not a valid C# namespace: each part between the dots must be an identifier, and C# keywords such as
`class` or `namespace` are not accepted.

**Fix:** set `EmbeddedConstantsNamespace` to a valid namespace. When the property is not set, the namespace is the root
namespace of the project, or its name when the root namespace is empty: check these values.

## MFECG0002: Embedded constants class name is invalid

`EmbeddedConstantsClassName` is not a valid C# identifier, or is a C# keyword.

**Fix:** use a valid identifier, such as `EmbeddedFiles`.

## MFECG0003: Embedded constant kind is missing

An `EmbeddedConstant` item has no `Kind` metadata.

**Fix:** add `Kind="Text"`, `Kind="Binary"` or `Kind="Both"` to the item.

## MFECG0004: Embedded constant kind is invalid

The `Kind` metadata is not `Text`, `Binary` or `Both`.

**Fix:** use one of these values. The case does not matter.

## MFECG0005: Embedded constant member name is duplicated

Several files produce the same member name. The message lists the files involved. This happens when two files have
the same `Name` metadata, or when files with the same name are not separated by their relative path, for instance
because one of them is outside the project directory. See [Member names](03-Configuration.md#member-names).

**Fix:** set the `Name` metadata on all but one of the files.

## MFECG0006: Embedded constant file cannot be read

A file listed in `EmbeddedConstant` does not exist or cannot be read. The message gives the reason.

**Fix:** check the path in the `Include` attribute; a relative path is relative to the project directory.

## MFECG0007: Embedded constant text file is not valid UTF-8

A file with `Kind="Text"` or `Kind="Both"` contains bytes that are not valid UTF-8, for instance a file saved in another
encoding, or a binary file.

**Fix:** save the file as UTF-8, or use `Kind="Binary"` for a file that is not text.

## MFECG0008: Embedded constant text file is too large

A text file is larger than 1 MiB, the maximum for a `const string`.

**Fix:** split the file, or use `Kind="Binary"` and decode the bytes at run time.

## MFECG0009: Embedded constants visibility is invalid

`EmbeddedConstantsClassVisibility` or `EmbeddedConstantsMemberVisibility` is not `internal` or `public`.

**Fix:** use `internal` or `public`, in lowercase.

## MFECG0010: Embedded constant member name is the same as the generated class name

A generated member would have the name of the class, which C# does not allow. For instance, the class is named
`ConfigText` and a file `config.txt` has `Kind="Text"`.

**Fix:** set the `Name` metadata on the file, or change `EmbeddedConstantsClassName`.

## MFECG0011: The embedded constants could not be computed or written

An unexpected error occurred, or the generated file could not be written (a read-only folder, a file locked by another
process...). The message gives the reason.

**Fix:** check that `EmbeddedConstantsOutputPath` points to a folder the build can write to.
