# YAML conformance verification

## Requirement evidence

| User requirement | Concrete evidence |
| --- | --- |
| "Can you make sure the project follow the specification." | `YamlTestSuite_StringParser`, `YamlTestSuite_BufferedParser` compare all 402 official fixtures against upstream event sequences or required YAML exceptions. `JsonSchema_IntegerResolutionMatchesSpecification`, `JsonSchema_FloatResolutionMatchesSpecification`, `JsonSchema_UnmatchedPlainScalarsAreRejected` check independent specification spellings, tags, values and types. |
| "Add as many tests as needed to validate the yaml parser / serializer." | `StringStyles_PreserveWhitespaceAcrossRootSequenceAndMappingPositions`, `Emit_ScalarStyles_PreserveWhitespaceAndChomping`, `Serializer_NonFiniteNumbersUseYamlSpellingsAndExplicitJsonTags`, `Emit_LocalTags_EscapeSuffixAndPreserveDecodedTag` exercise serializer/emitter values and exact YAML spellings. |
| "Think about edge cases." | `InvalidUnicodeEscapes_ThrowYamlException`, `InvalidTagUtf8Escapes_ThrowYamlException`, `NonPrintableInput_ThrowsYamlException`, `ByteOrderMarks_AreAllowedInDocumentPrefixesAndQuotedScalars`, `ByteOrderMarks_AreRejectedInsideUnquotedScalars`, `ImplicitKeyLengthLimit_CountsUnicodeCharacters`, `MappingKeys_UseExplicitKeysBeyondTheImplicitKeyLengthLimit`. |

## Assertion quality and behavioral gap review

Applied the assertion-quality and test-gap-analysis skill guidance, with the .NET assertion reference, to the added methods and changed production branches. Thirty new test methods plus one corrected existing behavior test use parameterized cases; the suite gains 1,008 executed cases on each framework.

- Parser acceptance asserts complete independent upstream event streams, including styles, values, anchors, tags, nesting, document boundaries and stream exhaustion. Replacing parsing with identity, an empty result, or unconditional failure is caught by valid cases; accepting all malformed input is caught by 94 invalid fixtures on each input path.
- Scalar output roundtrips assert complete values and neighboring mapping entries, covering root, sequence, mapping, style, whitespace and control-character intersections. Independent fixture parsing and explicit escaped-output assertions avoid relying only on two mutually incorrect transformations.
- Schema checks assert both tags and decoded values/types, plus false results and null outputs for rejected scalars. Trailing-newline, JSON -0, nonfinite, decimal/exponent, integer-width, Core/Failsafe distinctions and decodeValue=false partitions are covered.
- Character-limit tests cover below/at/above 1,024, including escaped output and supplementary Unicode input. The 1,023/1,024 supplementary cases failed before the character-count fix; a UTF-16-length regression is therefore directly observed.
- Unicode validation tests distinguish YAML exceptions from unrelated argument/range errors and test malformed surrogate pairs, out-of-range escapes, overlong UTF-8, raw controls and BOM positions. The raw-control tests also run with the circular buffer.
- Actual pre-fix failing runs demonstrated the grammar, emission, JSON resolution, Unicode and key-length defects. No synthetic mutations were left in source; no tests were skipped or disabled. No trivial-only or assertion-free methods were added.

The pinned corpus is substantial conformance evidence, not a proof for every possible YAML document. CLR binding options remain configurable, and the existing default scalar-conversion path remains permissive; the README now explicitly describes `UseSchema = true` for schema-specific spelling rules.

## Validation

Baseline: 2,194 passed (1,129 net11.0; 1,065 net10.0), zero failed/skipped.
Final YAML verification after the last source edit passed all 4,210 tests (2,137 net11.0; 2,073 net10.0), zero failed/skipped.

The required `dotnet run ./eng/update-all.cs /p:TreatsWarningsAsErrors=true` exited 0 and produced no additional tracked changes. Fixture content was checked against the downloaded pinned upstream inputs/events; all 402 records match. `git diff --check` passed.

The initial non-incremental workspace build encountered CS0006 in the unrelated PublicApiGenerator.Tool net10.0 project (a reference assembly was absent while duplicate project builds ran). Its YAML analyzer warnings were fixed. The serial non-incremental workspace build subsequently exited 0 with 0 warnings and 0 errors:

```text
dotnet build Meziantou.Framework.slnx --no-incremental -m:1 /p:TreatsWarningsAsErrors=true /p:TreatWarningsAsErrors=true
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

Final commands (all exited 0):

```text
dotnet run ./eng/update-all.cs /p:TreatsWarningsAsErrors=true
dotnet test --project /Users/meziantou/.codex/worktrees/b695/meziantou-meziantou.framework/tests/Meziantou.Framework.Yaml.Tests/Meziantou.Framework.Yaml.Tests.csproj /p:TreatsWarningsAsErrors=true /p:TreatWarningsAsErrors=true
git diff --check
```

Final test summary: Passed: 4210, Failed: 0, Skipped: 0. No generated ref/ changes and no unrelated tracked modifications remained. `global.json` was unchanged. The update script was rerun successfully after the serial workspace build.
