namespace ZiggyCreatures.Caching.Fusion.Plugins.SelfTuning.Models;

/// <summary>
/// Tracks performance metrics for a specific cache entry.
/// </summary>
public class CacheEntryMetrics
{
	private readonly object _lock = new();
	private readonly Queue<DateTimeOffset> _recentAccesses = new();
	private readonly Queue<CacheEntryCost> _recentCosts = new();
	private readonly Queue<double> _factoryLatencies = new();

	// Exponential Moving Average state for factory latency
	private double _factoryLatencyEma;
	private bool _hasFactoryLatencyEma;
	private const double EmaAlpha = 0.1; // Smoothing factor for EMA

	// Failure tracking with exponential decay
	private double _failureRate;
	private DateTimeOffset _lastFailureUpdate = DateTimeOffset.UtcNow;
	private const double FailureDecayRate = 0.95; // Decay rate per hour

	/// <summary>
	/// The cache key these metrics relate to.
	/// </summary>
	public string Key { get; }

	/// <summary>
	/// Total number of cache hits for this entry (memory + L2 if available).
	/// </summary>
	public long HitCount { get; private set; }

	/// <summary>
	/// Total number of cache misses for this entry.
	/// </summary>
	public long MissCount { get; private set; }

	/// <summary>
	/// Total number of times this entry was accessed (includes hits and misses).
	/// </summary>
	public long AccessCount { get; private set; }

	/// <summary>
	/// Total number of factory execution failures.
	/// </summary>
	public long FailureCount { get; private set; }

	/// <summary>
	/// The hit rate as a percentage (0.0 to 1.0).
	/// </summary>
	public double HitRate => AccessCount == 0 ? 0.0 : (double)HitCount / AccessCount;

	/// <summary>
	/// The failure rate with exponential decay (0.0 to 1.0).
	/// </summary>
	public double FailureRate
	{
		get
		{
			// Apply exponential decay based on time elapsed
			var hoursElapsed = (DateTimeOffset.UtcNow - _lastFailureUpdate).TotalHours;
			return _failureRate * Math.Pow(FailureDecayRate, hoursElapsed);
		}
	}

	/// <summary>
	/// The current TTL being used for this entry.
	/// </summary>
	public TimeSpan CurrentTtl { get; set; }

	/// <summary>
	/// The timestamp when this entry was first seen/accessed (UTC).
	/// </summary>
	public DateTimeOffset FirstSeenUtc { get; private set; }

	/// <summary>
	/// The timestamp of the most recent access (UTC).
	/// </summary>
	public DateTimeOffset LastAccessUtc { get; private set; }

	/// <summary>
	/// Exponential moving average of factory execution latency in milliseconds.
	/// </summary>
	public double FactoryLatencyAvg => _factoryLatencyEma;

	/// <summary>
	/// 95th percentile of factory execution latency in milliseconds (optional).
	/// </summary>
	public double FactoryLatencyP95
	{
		get
		{
			if (_factoryLatencies.Count == 0) return 0.0;
			var sorted = _factoryLatencies.OrderBy(x => x).ToArray();
			var index = (int)Math.Ceiling(0.95 * sorted.Length) - 1;
			return sorted[Math.Max(0, Math.Min(index, sorted.Length - 1))];
		}
	}

	/// <summary>
	/// Last calculated cost score for this cache entry.
	/// </summary>
	public double CostScore => _recentCosts.Count == 0 ? 0.0 : _recentCosts.LastOrDefault()?.GetTotalCostScore() ?? 0.0;

	/// <summary>
	/// Total number of times this entry was accessed (backward compatibility).
	/// </summary>
	public long TotalAccesses => AccessCount;

	/// <summary>
	/// Average cost of operations for this cache entry (backward compatibility).
	/// </summary>
	public double AverageCost => _recentCosts.Count == 0 ? 0.0 : _recentCosts.Average(c => c.GetTotalCostScore());

	/// <summary>
	/// The timestamp when this entry was first accessed (backward compatibility).
	/// </summary>
	public DateTimeOffset FirstAccessTime => FirstSeenUtc;

	/// <summary>
	/// The timestamp of the most recent access (backward compatibility).
	/// </summary>
	public DateTimeOffset LastAccessTime => LastAccessUtc;

	/// <summary>
	/// Creates a new instance of cache entry metrics.
	/// </summary>
	/// <param name="key">The cache key.</param>
	/// <param name="initialTtl">The initial TTL for this entry.</param>
	public CacheEntryMetrics(string key, TimeSpan initialTtl)
	{
		Key = key;
		CurrentTtl = initialTtl;
		FirstSeenUtc = DateTimeOffset.UtcNow;
		LastAccessUtc = FirstSeenUtc;
	}

	/// <summary>
	/// Records a cache hit.
	/// </summary>
	public void RecordHit()
	{
		lock (_lock)
		{
			HitCount++;
			AccessCount++;
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
			AccessCount++;
			UpdateAccess();
		}
	}

	/// <summary>
	/// Records a factory execution failure.
	/// </summary>
	public void RecordFailure()
	{
		lock (_lock)
		{
			FailureCount++;
			UpdateFailureRate();
		}
	}

	/// <summary>
	/// Records the factory execution latency.
	/// </summary>
	/// <param name="latencyMs">The latency in milliseconds.</param>
	public void RecordFactoryLatency(double latencyMs)
	{
		lock (_lock)
		{
			// Update exponential moving average
			if (!_hasFactoryLatencyEma)
			{
				_factoryLatencyEma = latencyMs;
				_hasFactoryLatencyEma = true;
			}
			else
			{
				_factoryLatencyEma = EmaAlpha * latencyMs + (1 - EmaAlpha) * _factoryLatencyEma;
			}

			// Keep recent latencies for P95 calculation (last 100 measurements)
			_factoryLatencies.Enqueue(latencyMs);
			while (_factoryLatencies.Count > 100)
			{
				_factoryLatencies.Dequeue();
			}
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

			// Keep only recent costs (last 100 operations)
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
		LastAccessUtc = DateTimeOffset.UtcNow;
		_recentAccesses.Enqueue(LastAccessUtc);

		// Keep only recent accesses (last 24 hours worth)
		var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
		while (_recentAccesses.Count > 0 && _recentAccesses.Peek() < cutoff)
		{
			_recentAccesses.Dequeue();
		}
	}

	/// <summary>
	/// Updates the failure rate with exponential decay.
	/// </summary>
	private void UpdateFailureRate()
	{
		var now = DateTimeOffset.UtcNow;
		var hoursElapsed = (now - _lastFailureUpdate).TotalHours;

		// Apply decay to current failure rate
		_failureRate *= Math.Pow(FailureDecayRate, hoursElapsed);

		// Add new failure (increment by 0.1 or adjust based on needs)
		_failureRate = Math.Min(1.0, _failureRate + 0.1);

		_lastFailureUpdate = now;
	}
}
