# Scripts of Playwright

The scripts that the Playwright server injects in the pages, copied from the `playwright-core` npm package (version
1.64.1, Apache License 2.0, see `LICENSE.txt` and `NOTICE.txt`):

- `injectedScriptSource.js`: the injected script (`packages/injected/src/injectedScript.ts` of the Playwright repository):
  the selector engines (CSS, XPath, text, role, label, test id...), the actionability checks, the hit target checks, the
  assertions (`expect`) and the aria snapshots.
- `utilityScriptSource.js`: the utility script (`packages/injected/src/utilityScript.ts`): the evaluation of the expressions
  of the clients, and the serialization of their arguments and of their results.

The files are the string literals of `lib/coreBundle.js` of the package (the generated `injectedScriptSource.ts` and
`utilityScriptSource.ts` modules), unchanged. Do not edit them. The `update-playwright-scripts.yml` workflow opens a pull
request every week when a new version of Playwright is released. The script it runs updates the scripts,
`PlaywrightServer.PlaywrightVersion`, the version of `Microsoft.Playwright` of the tests and of the samples, and the versions
in the documentation (the latest stable version of `Microsoft.Playwright` by default, or the given version):

```bash
dotnet run ./eng/update-playwright-scripts.cs -- [<version>]
```

Then build the project (to regenerate its `ref/` file), run the tests, and review the changes of the protocol
(`packages/protocol/spec` of the Playwright repository) since the previous version.
