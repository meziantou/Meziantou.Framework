// Records the results of the workloads in Chromium (Playwright) in expected/<workload>.txt: see readme.md.
//
// node generate.js [--check] [workload...]
//
// --check compares the results with the files instead of writing them.
"use strict";

const fs = require("fs");
const path = require("path");

const root = path.join(__dirname, "..", "Meziantou.Framework.JavaScriptEngine.External", "npm-packages");
const expectedDirectory = path.join(__dirname, "expected");
const origin = "https://npm.test";

let chromium;
try {
  ({ chromium } = require("./generator/node_modules/playwright"));
}
catch {
  console.error("Playwright is not installed: run 'npm ci --prefix generator' and 'npx --prefix generator playwright install chromium'");
  process.exit(1);
}

if (!fs.existsSync(path.join(root, "importmap.json"))) {
  console.error(`${root}/importmap.json does not exist: run 'dotnet run eng/javascriptengine/fetch-tests.cs npm-packages'`);
  process.exit(1);
}

const args = process.argv.slice(2);
const check = args[0] === "--check";
if (check)
  args.shift();

const contentTypes = { ".js": "text/javascript", ".mjs": "text/javascript", ".cjs": "text/javascript", ".json": "application/json", ".css": "text/css", ".wasm": "application/wasm", ".html": "text/html" };
const page = fs.readFileSync(path.join(__dirname, "page.html"), "utf8").replace("{{importmap}}", fs.readFileSync(path.join(root, "importmap.json"), "utf8"));

// The files of the site: the page, the runner and the workloads of this directory, and the installed packages
function getFile(pathname) {
  if (pathname === "/")
    return { body: page, contentType: "text/html" };

  const fullPath = pathname.startsWith("/node_modules/") ? path.join(root, pathname) : pathname === "/runner.mjs" || pathname.startsWith("/workloads/") ? path.join(__dirname, pathname) : undefined;
  if (fullPath === undefined || !fs.statSync(fullPath, { throwIfNoEntry: false })?.isFile())
    return undefined;
  return { body: fs.readFileSync(fullPath), contentType: contentTypes[path.extname(fullPath)] ?? "application/octet-stream" };
}

// Returns the result of the workload, and what explains a failure: the missing files and the errors of the page
async function runWorkload(browser, name) {
  const context = await browser.newContext({ timezoneId: "UTC", locale: "en-US" });
  const diagnostics = [];
  try {
    await context.route(origin + "/**", route => {
      const file = getFile(decodeURIComponent(new URL(route.request().url()).pathname));
      if (file === undefined)
        diagnostics.push("404 " + route.request().url());
      return file === undefined ? route.fulfill({ status: 404, body: "" }) : route.fulfill({ status: 200, body: file.body, contentType: file.contentType });
    });

    const page = await context.newPage();
    page.on("pageerror", error => diagnostics.push("page error: " + error.message));
    page.on("console", message => message.type() === "error" && !message.text().startsWith("Failed to load resource") && diagnostics.push("console: " + message.text()));
    await page.goto(origin + "/?workload=" + encodeURIComponent(name));

    // The page runs in real time (the fake clock of Playwright replaces Date by a function that cannot be subclassed)
    try {
      await page.waitForFunction(() => "done" in document.documentElement.dataset, null, { timeout: 120_000, polling: 50 });
    }
    catch {
      return { result: "Error: the workload did not complete", diagnostics };
    }

    return { result: await page.evaluate(() => document.getElementById("result").textContent), diagnostics };
  }
  finally {
    await context.close();
  }
}

(async () => {
  const names = args.length > 0 ? args : fs.readdirSync(path.join(__dirname, "workloads")).filter(name => name.endsWith(".mjs")).map(name => name.slice(0, -4)).sort();
  const browser = await chromium.launch();
  console.log(`Chromium ${browser.version()}`);
  let failed = false;
  fs.mkdirSync(expectedDirectory, { recursive: true });
  for (const name of names) {
    const start = process.hrtime.bigint();
    const { result, diagnostics } = await runWorkload(browser, name);
    const duration = Number((process.hrtime.bigint() - start) / 1000000n);
    const expectedPath = path.join(expectedDirectory, name + ".txt");
    if (check) {
      const same = fs.existsSync(expectedPath) && fs.readFileSync(expectedPath, "utf8") === result;
      failed ||= !same;
      console.log(`${name}: ${same ? "same" : "different"} (${duration} ms)`);
    }
    else {
      fs.writeFileSync(expectedPath, result);
      console.log(`${name}: ${result.length} characters (${duration} ms)${result.startsWith("Error: ") ? " " + result.split("\n")[0] : ""}`);
    }

    if (result.startsWith("Error: "))
      diagnostics.slice(0, 10).forEach(diagnostic => console.log("  " + diagnostic));
  }

  await browser.close();
  process.exit(failed ? 1 : 0);
})();
