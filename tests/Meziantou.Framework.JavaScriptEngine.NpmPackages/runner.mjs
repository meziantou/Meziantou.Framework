// Runs the workload named by the 'workload' parameter of the URL of the page (page.html): it imports
// /workloads/<name>.mjs, calls its default export, and writes the result in the #result element. The result is the
// value of the workload as a string, followed by what the packages wrote to the console. Once it is written, the
// root element has a 'data-done' attribute. The hosts (generate.js in Chromium, NpmPackagesTests.cs in
// Meziantou.Framework.WebPlatform) read it.
const messages = [];
for (const level of ["log", "info", "warn", "error", "debug", "trace"]) {
  console[level] = (...args) => messages.push(level + ": " + args.map(String).join(" "));
}

const describe = error => error instanceof Error ? String(error) + (error.cause === undefined ? "" : " (cause: " + String(error.cause) + ")") : String(error);

// The exceptions of the callbacks (timers, events) are reported to the window
addEventListener("error", event => messages.push("uncaught: " + describe(event.error)));

let result;
try {
  const name = new URLSearchParams(location.search).get("workload");
  const workload = await import("/workloads/" + name + ".mjs");
  result = String(await workload.default());
}
catch (error) {
  result = "Error: " + describe(error);
}

if (messages.length > 0)
  result += "\n--- console ---\n" + messages.join("\n");

document.getElementById("result").textContent = result;
document.documentElement.dataset.done = "";
