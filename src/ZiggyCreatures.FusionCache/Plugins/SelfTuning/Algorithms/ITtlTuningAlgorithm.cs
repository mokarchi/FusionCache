using ZiggyCreatures.Caching.Fusion.Plugins.SelfTuning.Models;

namespace ZiggyCreatures.Caching.Fusion.Plugins.SelfTuning.Algorithms;

/// <summary>
/// Interface for TTL tuning algorithms.
/// </summary>
public interface ITtlTuningAlgorithm
{
	/// <summary>
	/// Calculates the recommended TTL for a cache entry based on its metrics.
	/// </summary>
	/// <param name="metrics">The cache entry metrics.</param>
	/// <param name="options">The self-tuning options.</param>
	/// <returns>The recommended TTL.</returns>
	TimeSpan CalculateRecommendedTtl(CacheEntryMetrics metrics, SelfTuningOptions options);
}
