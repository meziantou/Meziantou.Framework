# YAML conformance research

Scope: src/Meziantou.Framework.Yaml and tests/Meziantou.Framework.Yaml.Tests. The package targets YAML 1.2; normative reference: https://yaml.org/spec/1.2.2/ . Existing tests use xUnit v3 and Meziantou.Framework.Assertions with parameterized theories. SDK-style projects target net10.0 and net11.0 using native Microsoft.Testing.Platform.

Acceptance checklist (user wording):
- "make sure the project follow the specification": compare parser events and scalar resolution with YAML 1.2.2, including malformed input rejection; repair demonstrated defects.
- "Add as many tests as needed to validate the yaml parser / serializer": exercise scalar, collection, document, directive, tag and alias semantics, and emitted YAML preservation through public APIs.
- "Think about edge cases": include Unicode and escapes, whitespace/indentation, block scalar folding/chomping, indicators, empty nodes, schema ambiguity, and boundary errors.

Target inventory: Scanner.cs and Parser.cs (syntax/events), Emitter.cs and Serialization/YamlWriter.cs (output), Schemas/* and serialization scalar converters (resolution). Existing homes: ParserTests.cs, ScannerTests.cs, Yaml12CoreTests.cs, EmitterEdgeCaseTests.cs, and Serialization/*Tests.cs.

Static pairing analyzer was executed once against the explicitly requested source directory. It reported 232 source files and no test files because tests live in the sibling tests/ project; this scoped result is not evidence that the package lacks tests. Existing test conventions and behavior evidence were read directly from the YAML test project.

The official data-branch corpus is pinned at 6ad3d2c62885d82fc349026c136ef560838fdf3d: 402 fixtures (308 valid, 94 invalid), vendored with MIT license and byte-for-byte input preservation. Each fixture runs through the string parser and a 12-character circular look-ahead buffer.
