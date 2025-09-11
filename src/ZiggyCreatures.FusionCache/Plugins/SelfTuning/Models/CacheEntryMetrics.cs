namespace ZiggyCreatures.Caching.Fusion.Plugins.SelfTuning.Models;

/// <summary>
/// Tracks performance metrics for a specific cache entry.
/// </summary>
public sealed class CacheEntryMetrics
{
	private readonly object _lock = new();
	private readonly Queue<DateTimeOffset> _recentAccesses = new();
	private readonly Queue<CacheEntryCost> _recentCosts = new();

	/// <summary>
	/// The cache key these metrics relate to.
	/// </summary>
	public string Key { get; }

	/// <summary>
	/// Total number of cache hits for this entry.
	/// </summary>
	public long HitCount { get; private set; }

	/// <summary>
	/// Total number of cache misses for this entry.
	/// </summary>
	public long MissCount { get; private set; }

	/// <summary>
	/// Total number of times this entry was accessed.
	/// </summary>
	public long TotalAccesses => HitCount + MissCount;

	/// <summary>
	/// The hit rate as a percentage (0.0 to 1.0).
	/// </summary>
	public double HitRate => TotalAccesses == 0 ? 0.0 : (double)HitCount / TotalAccesses;

	/// <summary>
	/// The current TTL being used for this entry.
	/// </summary>
	public TimeSpan CurrentTtl { get; set; }

	/// <summary>
	/// The timestamp when this entry was first accessed.
	/// </summary>
	public DateTimeOffset FirstAccessTime { get; private set; }

	/// <summary>
	/// The timestamp of the most recent access.
	/// </summary>
	public DateTimeOffset LastAccessTime { get; private set; }

	/// <summary>
	/// Average cost of operations for this cache entry.
	/// </summary>
	public double AverageCost => _recentCosts.Count == 0 ? 0.0 : _recentCosts.Average(c => c.GetTotalCostScore());

	/// <summary>
	/// Creates a new instance of cache entry metrics.
	/// </summary>
	/// <param name="key">The cache key.</param>
	/// <param name="initialTtl">The initial TTL for this entry.</param>
	public CacheEntryMetrics(string key, TimeSpan initialTtl)
	{
		Key = key;
		CurrentTtl = initialTtl;
		FirstAccessTime = DateTimeOffset.UtcNow;
		LastAccessTime = FirstAccessTime;
	}

	/// <summary>
	/// Records a cache hit.
	/// </summary>
	public void RecordHit()
	{
		lock (_lock)
		{
			HitCount++;
			UpdateAccess();
		}
	}

	/// <summary>
	/// Records a cache miss.
	/// </summary>
	public void RecordMiss()
	{
		lock (_lock)
		{
			MissCount++;
			UpdateAccess();
		}
	}

	/// <summary>
	/// Records the cost of an operation for this cache entry.
	/// </summary>
	/// <param name="cost">The cost information.</param>
	public void RecordCost(CacheEntryCost cost)
	{
		lock (_lock)
		{
			_recentCosts.Enqueue(cost);

			while (_recentCosts.Count > 100)
			{
				_recentCosts.Dequeue();
			}
		}
	}

	/// <summary>
	/// Gets the access frequency per hour over the last 24 hours.
	/// </summary>
	/// <returns>The access frequency per hour.</returns>
	public double GetAccessFrequencyPerHour()
	{
		lock (_lock)
		{
			var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
			var recentAccessCount = _recentAccesses.Count(access => access > cutoff);
			return recentAccessCount / 24.0; // Per hour
		}
	}

	/// <summary>
	/// Updates the access tracking information.
	/// </summary>
	private void UpdateAccess()
	{
		LastAccessTime = DateTimeOffset.UtcNow;
		_recentAccesses.Enqueue(LastAccessTime);

		var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
		while (_recentAccesses.Count > 0 && _recentAccesses.Peek() < cutoff)
		{
			_recentAccesses.Dequeue();
		}
	}
}
