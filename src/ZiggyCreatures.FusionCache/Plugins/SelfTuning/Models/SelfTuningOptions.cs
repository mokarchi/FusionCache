namespace ZiggyCreatures.Caching.Fusion.Plugins.SelfTuning.Models;

/// <summary>
/// Configuration options for the self-tuning cache plugin.
/// </summary>
public sealed class SelfTuningOptions
{
	/// <summary>
	/// The minimum TTL that can be applied to cache entries.
	/// </summary>
	public TimeSpan MinTtl { get; set; } = TimeSpan.FromMinutes(1);

	/// <summary>
	/// The maximum TTL that can be applied to cache entries.
	/// </summary>
	public TimeSpan MaxTtl { get; set; } = TimeSpan.FromHours(24);

	/// <summary>
	/// The base TTL to use for new cache entries.
	/// </summary>
	public TimeSpan BaseTtl { get; set; } = TimeSpan.FromMinutes(5);

	/// <summary>
	/// The target hit rate for optimal performance (0.0 to 1.0).
	/// </summary>
	public double TargetHitRate { get; set; } = 0.8;

	/// <summary>
	/// The minimum number of accesses before TTL tuning begins.
	/// </summary>
	public int MinAccessesForTuning { get; set; } = 10;

	/// <summary>
	/// The factor by which to increase TTL when hit rate is above target.
	/// </summary>
	public double TtlIncreaseMultiplier { get; set; } = 1.2;

	/// <summary>
	/// The factor by which to decrease TTL when hit rate is below target.
	/// </summary>
	public double TtlDecreaseMultiplier { get; set; } = 0.8;

	/// <summary>
	/// How much weight to give to cost considerations when adjusting TTL.
	/// Higher values mean more expensive operations get longer TTL.
	/// </summary>
	public double CostSensitivity { get; set; } = 0.1;

	/// <summary>
	/// How often to clean up stale metrics (entries that haven't been accessed recently).
	/// </summary>
	public TimeSpan MetricsCleanupInterval { get; set; } = TimeSpan.FromHours(1);

	/// <summary>
	/// How long to keep metrics for entries that haven't been accessed.
	/// </summary>
	public TimeSpan MetricsRetentionPeriod { get; set; } = TimeSpan.FromHours(24);

	/// <summary>
	/// Whether to enable automatic TTL adjustment based on access patterns.
	/// </summary>
	public bool EnableAutoTuning { get; set; } = true;

	/// <summary>
	/// Whether to enable cost-aware caching adjustments.
	/// </summary>
	public bool EnableCostAwareness { get; set; } = true;

	/// <summary>
	/// Maximum number of cache entry metrics to track concurrently.
	/// Helps prevent memory growth for caches with many unique keys.
	/// </summary>
	public int MaxTrackedEntries { get; set; } = 10_000;
}
