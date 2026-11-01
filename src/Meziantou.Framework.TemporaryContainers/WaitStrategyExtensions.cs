using Meziantou.Framework.TemporaryContainers.Strategies;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Provides extension members for <see cref="IWaitStrategy"/>.</summary>
public static class WaitStrategyExtensions
{
    extension(IWaitStrategy strategy)
    {
        /// <summary>Bounds the strategy in time: once <paramref name="timeout"/> elapses, the wait fails with a <see cref="TimeoutException"/>. The container startup timeout still applies.</summary>
        /// <param name="timeout">The time the strategy has to complete.</param>
        /// <returns>A wait strategy.</returns>
        public IWaitStrategy WithTimeout(TimeSpan timeout)
        {
            ArgumentNullException.ThrowIfNull(strategy);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
            return new TimeoutWaitStrategy(strategy, timeout);
        }
    }
}
