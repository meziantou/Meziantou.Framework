# Third-Party Notices

This project includes third-party software components. The original copyright notices and the licenses under which the
respective authors made such components available are reproduced below.

- [Playwright](#playwright)

---

## Playwright

- Project: Playwright
- Source: <https://github.com/microsoft/playwright>, the `playwright-core` npm package
- License: Apache-2.0
- Version: 1.64.1
- Used for: the scripts injected in the pages (`Scripts/injectedScriptSource.js` and `Scripts/utilityScriptSource.js`,
  copied unchanged, see `Scripts/README.md`), and the server logic ported to C# (the dispatchers, the frames, the actions on
  the elements, the input and the keyboard layout of the Playwright server). The license and the notice are distributed with
  the package (`licenses/playwright-LICENSE.txt` and `licenses/playwright-NOTICE.txt`).

```
Playwright
Copyright (c) Microsoft Corporation

This software contains code derived from the Puppeteer project (https://github.com/puppeteer/puppeteer),
available under the Apache 2.0 license (https://github.com/puppeteer/puppeteer/blob/master/LICENSE).
```
