using System.Collections.Concurrent;

namespace ZiggyCreatures.Caching.Fusion.Plugins.SelfTuning;

internal sealed class SelfTuningMetricsTracker
{
	private readonly ConcurrentDictionary<string, KeyMetrics> _keyMetrics = new();
	private readonly object _cleanupLock = new();
	private DateTime _lastCleanup = DateTime.UtcNow;

	/// <summary>
	/// Records a cache hit event.
	/// </summary>
	/// <param name="key">The cache key.</param>
	public void RecordHit(string key)
	{
		var metrics = _keyMetrics.GetOrAdd(key, _ => new KeyMetrics());
		metrics.RecordHit();

		CleanupIfNeeded();
	}

	/// <summary>
	/// Records a cache miss with factory execution metrics.
	/// </summary>
	/// <param name="key">The cache key.</param>
	/// <param name="latency">Factory execution latency.</param>
	/// <param name="failed">Whether the factory execution failed.</param>
	public void RecordMiss(string key, TimeSpan latency, bool failed)
	{
		var metrics = _keyMetrics.GetOrAdd(key, _ => new KeyMetrics());
		metrics.RecordMiss(latency, failed);

		CleanupIfNeeded();
	}

	/// <summary>
	/// Removes stale metrics entries periodically.
	/// </summary>
	private void CleanupIfNeeded()
	{
		var now = DateTime.UtcNow;
		if (now - _lastCleanup < TimeSpan.FromMinutes(5))
			return;

		lock (_cleanupLock)
		{
			if (now - _lastCleanup < TimeSpan.FromMinutes(5))
				return;

			var cutoff = now - TimeSpan.FromHours(1);
			var keysToRemove = new List<string>();

			foreach (var kvp in _keyMetrics)
			{
				if (kvp.Value.LastActivity < cutoff)
					keysToRemove.Add(kvp.Key);
			}

			foreach (var key in keysToRemove)
			{
				_keyMetrics.TryRemove(key, out _);
			}

			_lastCleanup = now;
		}
	}

	/// <summary>
	/// Tracks metrics for a single cache key using EWMA for statistical smoothing.
	/// </summary>
	private sealed class KeyMetrics
	{
		private readonly object _lock = new();
		private readonly List<AccessSample> _samples = new();

		// EWMA (Exponential Weighted Moving Average) values for smooth metrics
		private double _ewmaHitRate = 0.5; // Start with neutral hit rate
		private double _ewmaLatencyMs = 0.0;
		private double _ewmaFailureRate = 0.0;
		private int _totalSamples = 0;

		// EWMA smoothing factor (0-1, higher = more responsive to recent data)
		private const double EWMA_ALPHA = 0.2;

		public DateTime LastActivity { get; private set; } = DateTime.UtcNow;

		public void RecordHit()
		{
			lock (_lock)
			{
				_samples.Add(new AccessSample
				{
					Timestamp = DateTime.UtcNow,
					IsHit = true,
					Latency = TimeSpan.Zero,
					Failed = false
				});
				LastActivity = DateTime.UtcNow;

				// Update EWMA - hit contributes to hit rate improvement
				UpdateEwmaMetrics(isHit: true, latencyMs: 0.0, failed: false);
			}
		}

		public void RecordMiss(TimeSpan latency, bool failed)
		{
			lock (_lock)
			{
				_samples.Add(new AccessSample
				{
					Timestamp = DateTime.UtcNow,
					IsHit = false,
					Latency = latency,
					Failed = failed
				});
				LastActivity = DateTime.UtcNow;

				// Update EWMA - miss contributes to latency and failure metrics
				UpdateEwmaMetrics(isHit: false, latencyMs: latency.TotalMilliseconds, failed: failed);
			}
		}

		private void UpdateEwmaMetrics(bool isHit, double latencyMs, bool failed)
		{
			_totalSamples++;

			// Update hit rate EWMA
			var currentHitValue = isHit ? 1.0 : 0.0;
			_ewmaHitRate = EWMA_ALPHA * currentHitValue + (1.0 - EWMA_ALPHA) * _ewmaHitRate;

			// Update latency EWMA (only for misses)
			if (!isHit)
			{
				_ewmaLatencyMs = EWMA_ALPHA * latencyMs + (1.0 - EWMA_ALPHA) * _ewmaLatencyMs;
			}

			// Update failure rate EWMA (only for misses)
			if (!isHit)
			{
				var currentFailureValue = failed ? 1.0 : 0.0;
				_ewmaFailureRate = EWMA_ALPHA * currentFailureValue + (1.0 - EWMA_ALPHA) * _ewmaFailureRate;
			}
		}
	}

	/// <summary>
	/// Represents a single access sample.
	/// </summary>
	private sealed class AccessSample
	{
		public DateTime Timestamp { get; set; }
		public bool IsHit { get; set; }
		public TimeSpan Latency { get; set; }
		public bool Failed { get; set; }
	}
}
