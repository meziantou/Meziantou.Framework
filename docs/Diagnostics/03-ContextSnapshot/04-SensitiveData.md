---
title: Sensitive data
description: Redact the secrets of the environment variables, choose your own rule, and know what else a snapshot reveals.
---

A snapshot describes the machine and the process in detail. Before you write it to a log or send it to someone, know
what it contains and remove what must not leave the machine.

## Secrets in environment variables

Environment variables often hold secrets: access tokens, passwords, connection strings. [AddEnvironmentVariables](cref:Meziantou.Framework.Diagnostics.ContextSnapshot.ContextSnapshotBuilder.AddEnvironmentVariables),
and therefore [AddDefault](cref:Meziantou.Framework.Diagnostics.ContextSnapshot.ContextSnapshotBuilder.AddDefault), replaces the value of a variable by [ContextSnapshotBuilder.RedactedValue](cref:Meziantou.Framework.Diagnostics.ContextSnapshot.ContextSnapshotBuilder.RedactedValue) (`***`) when its
name looks like a secret. You can change the rule:

<!-- ref:../../../tests/Meziantou.Framework.Diagnostics.ContextSnapshot.Tests/SnapshotTests.cs;region=ContextSnapshot_RedactEnvironmentVariables;format=md-fenced-code-block;dedent=true -->
```csharp
// By default, the value of a variable whose name contains TOKEN, SECRET, PASSWORD, API_KEY... is replaced by "***"
Assert.True(ContextSnapshotBuilder.IsSecretEnvironmentVariableName("GITHUB_TOKEN"));
Assert.True(ContextSnapshotBuilder.IsSecretEnvironmentVariableName("ConnectionStrings__Password"));
Assert.False(ContextSnapshotBuilder.IsSecretEnvironmentVariableName("ConnectionStrings__Default"));

// Extend the default rule to redact the connection strings too
var snapshot = new ContextSnapshotBuilder()
    .AddEnvironmentVariables(EnvironmentVariableTarget.Process, name =>
        ContextSnapshotBuilder.IsSecretEnvironmentVariableName(name) ||
        name.StartsWith("ConnectionStrings__", StringComparison.OrdinalIgnoreCase))
    .BuildSnapshot();

var variables = Assert.IsType<ImmutableSortedDictionary<string, object>>(snapshot["EnvironmentVariables.Process"]);
foreach (var (name, value) in variables)
{
    if (name.StartsWith("ConnectionStrings__", StringComparison.OrdinalIgnoreCase))
    {
        Assert.Equal(ContextSnapshotBuilder.RedactedValue, value);
    }
}
```
<!-- endref -->

- The default rule, [ContextSnapshotBuilder.IsSecretEnvironmentVariableName](cref:Meziantou.Framework.Diagnostics.ContextSnapshot.ContextSnapshotBuilder.IsSecretEnvironmentVariableName), redacts a variable whose name contains,
  ignoring case, one of `TOKEN`, `SECRET`, `PASSWORD`, `PASSWD`, `PWD`, `APIKEY`, `API_KEY`, `ACCESS_KEY`,
  `PRIVATE_KEY`, `CREDENTIAL` or `SESSION`.
- `AddEnvironmentVariables(target, shouldRedact)` takes your own rule: it is called with the name of each variable,
  and the value is redacted when it returns `true`. Combine it with the default rule, as in the example, rather than
  replacing it. Pass `_ => false` to capture every value as is.
- The rule only looks at names. It also redacts variables that are not secrets, such as `PWD` and `OLDPWD`, the current
  and previous directories in a Unix shell, or the `*_SESSION_*` variables of a desktop session.

[AddDefault](cref:Meziantou.Framework.Diagnostics.ContextSnapshot.ContextSnapshotBuilder.AddDefault) uses the default rule. To use your own rule with [AddDefault](cref:Meziantou.Framework.Diagnostics.ContextSnapshot.ContextSnapshotBuilder.AddDefault), call [AddEnvironmentVariables](cref:Meziantou.Framework.Diagnostics.ContextSnapshot.ContextSnapshotBuilder.AddEnvironmentVariables) after it, for
each target (`Process`, `User` and `Machine`): adding a key that already exists replaces its value.

## Name-based redaction is best effort

A secret stored in a variable with an unremarkable name, such as `DATABASE_URL` or `ConnectionStrings__Default`, is
captured as is. Add the names your application uses to the rule, or leave the environment variables out of the
snapshot: build it with the `Add*` methods you need instead of [AddDefault](cref:Meziantou.Framework.Diagnostics.ContextSnapshot.ContextSnapshotBuilder.AddDefault).

## Other information to consider

Only environment variables are redacted. The rest of the snapshot is captured as is, and can reveal:

- The **command line** of the process, which contains the secrets passed as arguments, and the path of the executable.
- The **user name**, the **domain name** and the **machine name**.
- **Paths** that contain the user name: the current directory, the special folders, the location of the assemblies and
  the modules.
- The `AppContext` switches and data, which hold the configuration of the runtime.
- The values you add yourself with [AddValue](cref:Meziantou.Framework.Diagnostics.ContextSnapshot.ContextSnapshotBuilder.AddValue).

Treat a snapshot like a log file that contains this information: store it and share it accordingly.
