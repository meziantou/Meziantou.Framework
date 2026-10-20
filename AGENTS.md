**Any code you commit SHOULD compile, and new and existing tests related to the change SHOULD pass.**

You MUST make your best effort to ensure your changes satisfy those criteria before committing. If for any reason you were unable to build or test the changes, you MUST report that. You MUST NOT claim success unless all builds and tests pass as described above.

Do not complete without checking the relevant code builds and relevant tests still pass after the last edits you make. Do not simply assume that your changes fix test failures you see, actually build and run those tests again to confirm.
When building and running tests, use `/p:TreatsWarningsAsErrors=true`.
Also, do not assume that tests pass just because you did not see any failures in your last test run; verify that all relevant tests were actually run.
When running tests, you can ignore the warning about "Blame" data collector errors.

In addition to building and testing your changes, you MUST run the following scripts:
- `dotnet run ./eng/update-all.cs`

You MUST follow all code-formatting and naming conventions defined in [`.editorconfig`](/.editorconfig).

In addition to the rules enforced by `.editorconfig`, you SHOULD:

- Prefer file-scoped namespace declarations and single-line using directives.
- Ensure that the final return statement of a method is on its own line.
- Use pattern matching and switch expressions wherever possible.
- Use `nameof` instead of string literals when referring to member names.
- Always use `is null` or `is not null` instead of `== null` or `!= null`.
- Trust the C# null annotations and don't add null checks when the type system says a value cannot be null.
- Prefer `?.` if applicable (e.g. `scope?.Dispose()`).
- Use `ObjectDisposedException.ThrowIf` where applicable.
- When adding new unit tests, strongly prefer to add them to existing test code files rather than creating new code files.
- When running tests, if possible use filters and check test run counts, or look at test logs, to ensure they actually ran.
- Do not finish work with any tests commented out or disabled that were not previously commented out or disabled.
- Do not update `global.json` file
- When writing tests, do not emit "Act", "Arrange" or "Assert" comments.
- There should be no trailing whitespace in any lines.
- Add a blank line before XML documentation comments (`///`) when they follow other code (methods, properties, fields, etc.).
- Do not use `static` lambdas unless necessary to avoid a closure allocation.

Do not consider solution files (sln, slnx) in the slnx folders. These files are automatically generated and only for developers working on the project.

Do not manually update files under the `ref/` folder. Their content is generated when building the solution, and any resulting changes must be committed to git.

CI only runs the tests of a project in invariant globalization mode (`/p:InvariantGlobalization=true`) when the test project sets `<GlobalizationSensitive>true</GlobalizationSensitive>`. The `MFGS0001` diagnostic reports code that depends on ICU (named cultures, linguistic comparisons, string normalization, time zone names and ID conversion, `IdnMapping`). Either suppress it with a justification explaining why the result does not depend on ICU, or set the property in the project. A library that sets the property makes `MFGS0002` require it in every test project that tests the library, or tests a library that references it. The tested library is the referenced assembly whose name is the longest prefix of the test project name; set `GlobalizationSensitiveTestedAssemblies` to a comma-separated list of assembly names when the names do not match. The references a test project adds itself, such as test helpers, do not count. Set the property by hand when the ICU dependency is inside a package, such as a database client.

Code examples in XML documentation (`<example>`, `<code>`) and in the `readme.md` files of the projects under `src/` MUST NOT be written by hand. They are copied from code that compiles, using [Meziantou.FileReferencer](https://github.com/meziantou/Meziantou.FileReferencer):

- Write the example in a `#region <Type>_<Scenario>` of a test, in an existing test file of the library's test project. Replace `Console.WriteLine(x); // value` with assertions such as `Assert.Equal(value, x);`, so the documented behavior is tested. When the code cannot run in a test (Windows-only APIs, UI, network, user folders, etc.), put the region in a project under `samples/` instead: CI compiles these projects but does not run them. Reuse the library's sample project, or create `samples/<Library>.Samples`.
- Keep the region self-contained: it is read as a standalone snippet, so it must not use members of the test class.
- Reference the region from the documentation. The path is relative to the file containing the reference. In XML documentation:
  ```csharp
  /// <example>
  /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Sample.Tests/SampleTests.cs" ref:region="Sample_Usage" ref:dedent="true">
  /// </code>
  /// </example>
  ```
  In a readme file, write `<!-- ref:../../tests/Meziantou.Framework.Sample.Tests/SampleTests.cs;region=Sample_Usage;format=md-fenced-code-block;dedent=true -->` and `<!-- endref -->` on two lines, each alone on its line: the tool generates the fenced code block between them. (They are inline code here so that the tool does not process this file.)
- Run `dotnet run ./eng/update-all.cs` (or `dotnet run ./eng/update-all.cs -- --step snippets`) to fill or refresh the content. Do not edit the generated content. CI fails when the content is out of date or when a reference is invalid. When you add vendored files (copies of upstream data or test suites), add them to `eng/file-references-exclusions.txt`: their comments can look like file references.
