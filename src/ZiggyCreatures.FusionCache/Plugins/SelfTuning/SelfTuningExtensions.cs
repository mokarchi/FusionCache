using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion.Plugins.SelfTuning.Algorithms;
using ZiggyCreatures.Caching.Fusion.Plugins.SelfTuning.Models;

namespace ZiggyCreatures.Caching.Fusion.Plugins.SelfTuning;

/// <summary>
/// Extension methods for integrating the SelfTuningCachePlugin with FusionCache.
/// </summary>
public static class SelfTuningExtensions
{
	/// <summary>
	/// Adds the SelfTuningCachePlugin to the FusionCache builder.
	/// </summary>
	/// <param name="builder">The FusionCache builder.</param>
	/// <param name="options">Configuration options for the plugin.</param>
	/// <param name="tuningAlgorithm">The TTL tuning algorithm to use. If null, uses the default adaptive algorithm.</param>
	/// <returns>The FusionCache builder for method chaining.</returns>
	public static IFusionCacheBuilder WithSelfTuning(
		this IFusionCacheBuilder builder,
		SelfTuningOptions? options = null,
		ITtlTuningAlgorithm? tuningAlgorithm = null)
	{
		return builder.WithPlugin(serviceProvider =>
		{
			var logger = serviceProvider.GetService<ILogger<SelfTuningCachePlugin>>();
			return new SelfTuningCachePlugin(options, tuningAlgorithm, logger);
		});
	}

	/// <summary>
	/// Adds the SelfTuningCachePlugin to the FusionCache builder with configuration.
	/// </summary>
	/// <param name="builder">The FusionCache builder.</param>
	/// <param name="configureOptions">Action to configure the plugin options.</param>
	/// <param name="tuningAlgorithm">The TTL tuning algorithm to use. If null, uses the default adaptive algorithm.</param>
	/// <returns>The FusionCache builder for method chaining.</returns>
	public static IFusionCacheBuilder WithSelfTuning(
		this IFusionCacheBuilder builder,
		Action<SelfTuningOptions> configureOptions,
		ITtlTuningAlgorithm? tuningAlgorithm = null)
	{
		var options = new SelfTuningOptions();
		configureOptions(options);

		return builder.WithSelfTuning(options, tuningAlgorithm);
	}

	/// <summary>
	/// Extension method to easily get or set a value with self-tuning TTL applied.
	/// </summary>
	/// <typeparam name="TValue">The type of value being cached.</typeparam>
	/// <param name="cache">The FusionCache instance.</param>
	/// <param name="key">The cache key.</param>
	/// <param name="factory">The factory function to create the value.</param>
	/// <param name="options">Cache entry options.</param>
	/// <param name="token">Cancellation token.</param>
	/// <returns>The cached or newly created value.</returns>
	public static async ValueTask<TValue> GetOrSetWithSelfTuningAsync<TValue>(
		this IFusionCache cache,
		string key,
		Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, Task<TValue>> factory,
		FusionCacheEntryOptions? options = null,
		CancellationToken token = default)
	{
		// Find the self-tuning plugin
		var selfTuningPlugin = GetSelfTuningPlugin(cache);

		if (selfTuningPlugin != null)
		{
			// Wrap the factory with self-tuning logic
			var wrappedFactory = selfTuningPlugin.WrapFactory(key, factory);
			return await cache.GetOrSetAsync(key, wrappedFactory, options, token);
		}
		else
		{
			// No self-tuning plugin found, use normal behavior
			return await cache.GetOrSetAsync(key, factory, options, token);
		}
	}

	/// <summary>
	/// Extension method to record the cost of a cache operation.
	/// </summary>
	/// <param name="cache">The FusionCache instance.</param>
	/// <param name="key">The cache key.</param>
	/// <param name="cost">The cost information to record.</param>
	public static void RecordCost(this IFusionCache cache, string key, CacheEntryCost cost)
	{
		var selfTuningPlugin = GetSelfTuningPlugin(cache);
		selfTuningPlugin?.RecordCost(key, cost);
	}

	/// <summary>
	/// Extension method to get the current metrics for a cache key.
	/// </summary>
	/// <param name="cache">The FusionCache instance.</param>
	/// <param name="key">The cache key.</param>
	/// <returns>The cache entry metrics if available, otherwise null.</returns>
	public static CacheEntryMetrics? GetMetrics(this IFusionCache cache, string key)
	{
		var selfTuningPlugin = GetSelfTuningPlugin(cache);
		return selfTuningPlugin?.GetMetrics(key);
	}

	/// <summary>
	/// Extension method to get the recommended TTL for a cache key.
	/// </summary>
	/// <param name="cache">The FusionCache instance.</param>
	/// <param name="key">The cache key.</param>
	/// <returns>The recommended TTL, or null if no recommendation is available.</returns>
	public static TimeSpan? GetRecommendedTtl(this IFusionCache cache, string key)
	{
		var selfTuningPlugin = GetSelfTuningPlugin(cache);
		return selfTuningPlugin?.GetRecommendedTtl(key);
	}
#pragma warning disable IL2075
	private static SelfTuningCachePlugin? GetSelfTuningPlugin(IFusionCache cache)
	{
		// Use reflection to get the plugins from the cache instance
		// This is a bit of a hack, but necessary since the plugin list is not exposed publicly
		var cacheType = cache.GetType();
		var pluginsField = cacheType.GetField("_plugins", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

		if (pluginsField?.GetValue(cache) is List<IFusionCachePlugin> plugins)
		{
			return plugins.OfType<SelfTuningCachePlugin>().FirstOrDefault();
		}

		return null;
	}
#pragma warning restore IL2075
}
