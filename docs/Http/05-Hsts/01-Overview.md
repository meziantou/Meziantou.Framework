---
title: HSTS
description: HstsClientHandler sends the requests to HSTS hosts over HTTPS, using the built-in preload list and the policies the servers declare.
---

HTTP Strict Transport Security (HSTS, RFC 6797) lets a host declare that it must only be reached over HTTPS. Browsers
honor it; `HttpClient` does not: a request to `http://github.com` is sent in plaintext. [HstsClientHandler](cref:Meziantou.Framework.Http.HstsClientHandler), in the
`Meziantou.Framework.Http.Hsts` package, closes that gap. Before a request is sent, it rewrites `http://` to
`https://` when the host has an HSTS policy.

<InstallNuGetPackage Id="Meziantou.Framework.Http.Hsts" />

The policies come from two places:

- **The preload list**: the hosts that ship preloaded in the browsers, `github.com` among them, are built into the
  package. A request to them is upgraded from the first one.
- **The `Strict-Transport-Security` header**: when an HTTPS response carries it, the handler remembers the host, and
  the next requests to it are upgraded.

The policies are kept in an [HstsDomainPolicyCollection](cref:Meziantou.Framework.Http.HstsDomainPolicyCollection), which you can also query and edit yourself.

## Pages of this group

- [Upgrading requests](02-UpgradingRequests.md): add the handler to an `HttpClient`, what it upgrades and how it reads
  the `Strict-Transport-Security` header.
- [Managing policies](03-ManagingPolicies.md): add, check and remove policies, share or isolate a collection, and the
  limits that protect it.
- [Redirects](04-Redirects.md): how the handler follows redirects so that every hop is upgraded, and what it changes on
  your inner handler.
- [The preload list](05-PreloadList.md): what the built-in list contains and how to turn it off.
