namespace ZiggyCreatures.Caching.Fusion.Plugins.SelfTuning.Models;

/// <summary>
/// Represents the cost information for a cache entry operation.
/// </summary>
public sealed class CacheEntryCost
{
	/// <summary>
	/// The computational cost in milliseconds (e.g., time to compute/retrieve the value).
	/// </summary>
	public double ComputationTimeMs { get; set; }

	/// <summary>
	/// The memory cost in bytes (e.g., estimated size of the cached value).
	/// </summary>
	public long MemorySizeBytes { get; set; }

	/// <summary>
	/// The monetary cost (e.g., API call costs, database query costs).
	/// </summary>
	public decimal MonetaryCost { get; set; }

	/// <summary>
	/// Custom cost factor that can be used for application-specific cost calculations.
	/// </summary>
	public double CustomCostFactor { get; set; } = 1.0;

	/// <summary>
	/// The timestamp when this cost was recorded.
	/// </summary>
	public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

	/// <summary>
	/// Calculates a normalized total cost score combining all cost factors.
	/// </summary>
	/// <returns>A normalized cost score.</returns>
	public double GetTotalCostScore()
	{
		// Normalize different cost types to a comparable scale
		var normalizedComputation = ComputationTimeMs / 1000.0; // Convert to seconds
		var normalizedMemory = MemorySizeBytes / (1024.0 * 1024.0); // Convert to MB
		var normalizedMonetary = (double)MonetaryCost;

		return (normalizedComputation + normalizedMemory + normalizedMonetary) * CustomCostFactor;
	}
}
