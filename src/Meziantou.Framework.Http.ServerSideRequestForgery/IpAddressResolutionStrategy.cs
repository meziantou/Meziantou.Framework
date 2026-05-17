using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Meziantou.Framework.Http.ServerSideRequestForgery;

public abstract class IpAddressResolutionStrategy
{
    public static IpAddressResolutionStrategy Ipv4Only { get; } = new Ipv4OnlyIpAddressResolutionStrategy();

    public static IpAddressResolutionStrategy Ipv6Only { get; } = new Ipv6OnlyIpAddressResolutionStrategy();

    public static IpAddressResolutionStrategy PreferIpv4 { get; } = new PreferIpv4IpAddressResolutionStrategy();

    public static IpAddressResolutionStrategy Random { get; } = new RandomIpAddressResolutionStrategy();

    public static IpAddressResolutionStrategy RoundRobin { get; } = new RoundRobinIpAddressResolutionStrategy();

    protected internal abstract ValueTask<IPAddress> ResolveAsync(IReadOnlyList<IPAddress> addresses, ServerSideRequestForgeryOptions options, CancellationToken cancellationToken);

    private static IPAddress FindFirstAddress(IReadOnlyList<IPAddress> addresses, AddressFamily family)
    {
        foreach (var address in addresses)
        {
            if (address.AddressFamily == family)
            {
                return address;
            }
        }

        throw new ServerSideRequestForgeryException($"No address found for address family '{family}'.");
    }

    private sealed class Ipv4OnlyIpAddressResolutionStrategy : IpAddressResolutionStrategy
    {
        protected internal override ValueTask<IPAddress> ResolveAsync(IReadOnlyList<IPAddress> addresses, ServerSideRequestForgeryOptions options, CancellationToken cancellationToken)
        {
            _ = options;
            _ = cancellationToken;
            return ValueTask.FromResult(FindFirstAddress(addresses, AddressFamily.InterNetwork));
        }
    }

    private sealed class Ipv6OnlyIpAddressResolutionStrategy : IpAddressResolutionStrategy
    {
        protected internal override ValueTask<IPAddress> ResolveAsync(IReadOnlyList<IPAddress> addresses, ServerSideRequestForgeryOptions options, CancellationToken cancellationToken)
        {
            _ = options;
            _ = cancellationToken;
            return ValueTask.FromResult(FindFirstAddress(addresses, AddressFamily.InterNetworkV6));
        }
    }

    private sealed class PreferIpv4IpAddressResolutionStrategy : IpAddressResolutionStrategy
    {
        protected internal override ValueTask<IPAddress> ResolveAsync(IReadOnlyList<IPAddress> addresses, ServerSideRequestForgeryOptions options, CancellationToken cancellationToken)
        {
            _ = options;
            _ = cancellationToken;
            foreach (var address in addresses)
            {
                if (address.AddressFamily == AddressFamily.InterNetwork)
                    return ValueTask.FromResult(address);
            }

            if (addresses.Count == 0)
                throw new ServerSideRequestForgeryException("No safe IP addresses available after validation.");

            return ValueTask.FromResult(addresses[0]);
        }
    }

    private sealed class RandomIpAddressResolutionStrategy : IpAddressResolutionStrategy
    {
        protected internal override ValueTask<IPAddress> ResolveAsync(IReadOnlyList<IPAddress> addresses, ServerSideRequestForgeryOptions options, CancellationToken cancellationToken)
        {
            _ = options;
            _ = cancellationToken;

            if (addresses.Count == 0)
            {
                throw new ServerSideRequestForgeryException("No safe IP addresses available after validation.");
            }

            return ValueTask.FromResult(addresses[RandomNumberGenerator.GetInt32(addresses.Count)]);
        }
    }

    private sealed class RoundRobinIpAddressResolutionStrategy : IpAddressResolutionStrategy
    {
        private int _counter = -1;

        protected internal override ValueTask<IPAddress> ResolveAsync(IReadOnlyList<IPAddress> addresses, ServerSideRequestForgeryOptions options, CancellationToken cancellationToken)
        {
            _ = options;
            _ = cancellationToken;

            if (addresses.Count is 0)
                throw new ServerSideRequestForgeryException("No safe IP addresses available after validation.");

            var index = Interlocked.Increment(ref _counter);
            var position = index % addresses.Count;
            if (position < 0)
            {
                position += addresses.Count;
            }

            return ValueTask.FromResult(addresses[position]);
        }
    }
}
