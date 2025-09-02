using ZiggyCreatures.Caching.Fusion.Plugins.SelfTuning.Models;

namespace ZiggyCreatures.Caching.Fusion.Plugins.SelfTuning;

/// <summary>
/// Interface for self-tuning cache plugins that automatically adjust TTL and cost-aware caching.
/// </summary>
public interface ISelfTuningCachePlugin : IFusionCachePlugin
{
	/// <summary>
	/// Gets the current metrics for a specific cache key.
	/// </summary>
	/// <param name="key">The cache key.</param>
	/// <returns>The cache entry metrics if available, otherwise null.</returns>
	CacheEntryMetrics? GetMetrics(string key);

	/// <summary>
	/// Gets the recommended TTL for a cache key based on current metrics.
	/// </summary>
	/// <param name="key">The cache key.</param>
	/// <returns>The recommended TTL, or null if no recommendation is available.</returns>
	TimeSpan? GetRecommendedTtl(string key);

	/// <summary>
	/// Manually records the cost of a cache entry operation.
	/// </summary>
	/// <param name="key">The cache key.</param>
	/// <param name="cost">The cost information to record.</param>
	void RecordCost(string key, CacheEntryCost cost);
}
