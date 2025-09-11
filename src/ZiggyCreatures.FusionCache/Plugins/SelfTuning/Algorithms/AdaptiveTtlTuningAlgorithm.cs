using ZiggyCreatures.Caching.Fusion.Plugins.SelfTuning.Models;

namespace ZiggyCreatures.Caching.Fusion.Plugins.SelfTuning.Algorithms;

/// <summary>
/// Adaptive TTL tuning algorithm that adjusts TTL based on hit rate, access frequency, and cost.
/// </summary>
public sealed class AdaptiveTtlTuningAlgorithm : ITtlTuningAlgorithm
{
	/// <summary>
	/// Calculates the recommended TTL for a cache entry based on its metrics.
	/// </summary>
	/// <param name="metrics">The cache entry metrics.</param>
	/// <param name="options">The self-tuning options.</param>
	/// <returns>The recommended TTL.</returns>
	public TimeSpan CalculateRecommendedTtl(CacheEntryMetrics metrics, SelfTuningOptions options)
	{
		if (metrics.TotalAccesses < options.MinAccessesForTuning)
		{
			return options.BaseTtl;
		}

		var currentTtl = metrics.CurrentTtl;
		var hitRate = metrics.HitRate;
		var targetHitRate = options.TargetHitRate;

		// Base adjustment based on hit rate
		var adjustment = CalculateHitRateAdjustment(hitRate, targetHitRate, options);

		// Factor in access frequency
		var frequencyAdjustment = CalculateFrequencyAdjustment(metrics, options);

		// Factor in cost considerations
		var costAdjustment = CalculateCostAdjustment(metrics, options);

		// Combine all adjustments
		var totalAdjustment = adjustment * frequencyAdjustment * costAdjustment;

		// Apply the adjustment
		var newTtl = TimeSpan.FromMilliseconds(currentTtl.TotalMilliseconds * totalAdjustment);

		// Clamp to min/max bounds
		if (newTtl < options.MinTtl)
			newTtl = options.MinTtl;
		else if (newTtl > options.MaxTtl)
			newTtl = options.MaxTtl;

		return newTtl;
	}

	/// <summary>
	/// Calculates the TTL adjustment factor based on hit rate performance.
	/// </summary>
	private double CalculateHitRateAdjustment(double hitRate, double targetHitRate, SelfTuningOptions options)
	{
		if (hitRate > targetHitRate)
		{
			var overPerformance = hitRate - targetHitRate;
			return 1.0 + (overPerformance * (options.TtlIncreaseMultiplier - 1.0));
		}
		else
		{
			var underPerformance = targetHitRate - hitRate;
			return 1.0 - (underPerformance * (1.0 - options.TtlDecreaseMultiplier));
		}
	}

	/// <summary>
	/// Calculates the TTL adjustment factor based on access frequency.
	/// </summary>
	private double CalculateFrequencyAdjustment(CacheEntryMetrics metrics, SelfTuningOptions options)
	{
		var accessFrequency = metrics.GetAccessFrequencyPerHour();

		if (accessFrequency > 10) // More than 10 accesses per hour
		{
			return 1.1; // Increase TTL by 10%
		}
		else if (accessFrequency < 1) // Less than 1 access per hour
		{
			return 0.9; // Decrease TTL by 10%
		}

		return 1.0; // No change for moderate frequency
	}

	/// <summary>
	/// Calculates the TTL adjustment factor based on operation cost.
	/// </summary>
	private double CalculateCostAdjustment(CacheEntryMetrics metrics, SelfTuningOptions options)
	{
		if (!options.EnableCostAwareness)
		{
			return 1.0;
		}

		var averageCost = metrics.AverageCost;

		if (averageCost > 5.0) // High-cost operations
		{
			return 1.0 + (options.CostSensitivity * Math.Log(averageCost));
		}
		else if (averageCost < 0.1) // Very low-cost operations
		{
			return Math.Max(0.5, 1.0 - (options.CostSensitivity * 2));
		}

		return 1.0; // No adjustment for moderate cost
	}
}
