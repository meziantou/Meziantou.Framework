---
title: Running the proxy
description: Install the DNS proxy as a .NET tool, start it, change its settings, and point clients at it.
---

## Install the tool

The proxy is a .NET tool. It needs the ASP.NET Core runtime:

```bash
dotnet tool install --global Meziantou.DnsProxy
```

The command has the name of the package. Update it with `dotnet tool update --global Meziantou.DnsProxy`.

## Start it

```bash
Meziantou.DnsProxy
```

With the default settings, the proxy:

- answers DNS queries over UDP and TCP on port 5053, on the loopback addresses only (`127.0.0.1` and `::1`);
- serves the diagnostics page on `http://localhost:5080`. See [Diagnostics](05-Diagnostics.md);
- downloads two public block lists, then refreshes them every 30 minutes. See
  [Filter lists](03-Configuration.md#filter-lists);
- forwards the other queries to public resolvers over DNS over HTTPS and DNS over QUIC. See
  [Upstreams](03-Configuration.md#upstreams).

Send a query to `127.0.0.1` on port 5053 with any DNS client to check that it answers. It runs until you stop it
(Ctrl+C).

## Change the settings

The settings are in the `DnsProxy` section of the ASP.NET Core configuration. Set them in any of the usual sources:

- **Command-line arguments**, with `:` between the levels:

  ```bash
  Meziantou.DnsProxy --DnsProxy:DnsPort=53 --DnsProxy:HttpPort=8080
  ```

- **Environment variables**, with `__` between the levels:

  ```bash
  DnsProxy__DnsPort=53 DnsProxy__MaxCacheEntries=50000 Meziantou.DnsProxy
  ```

- **An `appsettings.json` file in the current directory**, the content root of the application. The
  [`appsettings.json` of the project](https://github.com/meziantou/Meziantou.Framework/blob/main/src/Meziantou.DnsProxy/appsettings.json)
  shows the structure, with the default values.

The tool package does not include an `appsettings.json` file, so the installed tool starts with the defaults of the
code. They are the same as that file, except two settings of the file: `AllowedHosts` (`localhost`, see
[Diagnostics](05-Diagnostics.md#security-of-the-page)) and the `localhost` custom records. Put them in your own
`appsettings.json` to get them.

Lists are indexed from 0: `--DnsProxy:Upstreams:0:Url=...` sets the first upstream. A list you set replaces the default
list entirely, so the defaults do not stay next to your entries. See [Configuration](03-Configuration.md).

The settings are read at startup: restart the proxy after a change.

## Point clients at the proxy

Operating systems send their DNS queries to port 53, so a proxy that serves the machine itself or the network must
listen on port 53 (`--DnsProxy:DnsPort=53`). On Linux and macOS, port 53 needs elevated privileges, and another local
DNS service may already use it. Then set `127.0.0.1` (or the address of the machine, for the network) as the DNS server
in the network settings of the clients, or in the DHCP settings of the router. To serve other machines, see
[Serving the network](04-ServingTheNetwork.md).

To run the proxy permanently, run the command as a service of the operating system.
