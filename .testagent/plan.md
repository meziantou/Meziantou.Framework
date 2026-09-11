# YAML conformance plan

1. Establish existing test baseline and deterministic source/test pairing. Compare official YAML test-suite event fixtures with the parser, retaining deterministic offline fixtures and exact assertions where practical.
2. Parser: expand acceptance/rejection and event-value cases; fix verified grammar issues in Scanner/Parser.
3. Serializer/emitter: add independent expected-output and roundtrip cases for string escaping, schema-sensitive values, block/flow contexts and Unicode; repair verified output loss or invalid YAML.
4. Schema resolution: add specification-based Core/JSON/Failsafe boundary cases and repair demonstrated inconsistencies.
5. Review assertion quality and behavioral blind spots, record status, run the full relevant test project on both target frameworks with /p:TreatsWarningsAsErrors=true, and execute dotnet run ./eng/update-all.cs. Attempt final workspace build without using generated slnx/ files.

Requirement mapping: specification compliance -> parser conformance fixtures + schema theories; parser/serializer validation -> parser events + writer/serializer exact outputs; edge cases -> parameterized scalar, escape, collection and invalid-input regressions. Exact implemented names and verification outcomes will be recorded in status.md.

Implemented phases cover all 402 upstream cases with no exclusions. The assertion review added invalid Unicode and UTF-8 input, byte-order-mark positions, exact local-tag output and nonfinite scalar output, and Unicode-aware key-length boundary tests. Final validation results are in status.md.
