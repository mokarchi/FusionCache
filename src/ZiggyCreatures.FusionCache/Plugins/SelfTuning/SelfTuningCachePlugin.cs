using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion.Events;
using ZiggyCreatures.Caching.Fusion.Plugins.SelfTuning.Algorithms;
using ZiggyCreatures.Caching.Fusion.Plugins.SelfTuning.Models;

namespace ZiggyCreatures.Caching.Fusion.Plugins.SelfTuning;

/// <summary>
/// A FusionCache plugin that automatically tunes TTL values based on cache performance metrics and operation costs.
/// </summary>
public class SelfTuningCachePlugin : ISelfTuningCachePlugin
{
	private readonly SelfTuningOptions _options;
	private readonly ITtlTuningAlgorithm _tuningAlgorithm;
	private readonly ILogger<SelfTuningCachePlugin>? _logger;
	private readonly ConcurrentDictionary<string, CacheEntryMetrics> _metrics;
	private readonly Timer _cleanupTimer;
	private IFusionCache? _cache;

	/// <summary>
	/// Initializes a new instance of the SelfTuningCachePlugin.
	/// </summary>
	/// <param name="options">Configuration options for the plugin.</param>
	/// <param name="tuningAlgorithm">The TTL tuning algorithm to use. If null, uses the default adaptive algorithm.</param>
	/// <param name="logger">Optional logger for diagnostic information.</param>
	public SelfTuningCachePlugin(
		SelfTuningOptions? options = null,
		ITtlTuningAlgorithm? tuningAlgorithm = null,
		ILogger<SelfTuningCachePlugin>? logger = null)
	{
		_options = options ?? new SelfTuningOptions();
		_tuningAlgorithm = tuningAlgorithm ?? new AdaptiveTtlTuningAlgorithm();
		_logger = logger;
		_metrics = new ConcurrentDictionary<string, CacheEntryMetrics>();

		// Setup cleanup timer
		_cleanupTimer = new Timer(CleanupMetrics, null, _options.MetricsCleanupInterval, _options.MetricsCleanupInterval);
	}

	/// <summary>
	/// Called when the plugin is added to a FusionCache instance.
	/// </summary>
	/// <param name="cache">The FusionCache instance.</param>
	public void Start(IFusionCache cache)
	{
		_cache = cache ?? throw new ArgumentNullException(nameof(cache));

		// Subscribe to cache events to track performance metrics
		_cache.Events.Hit += OnCacheHit;
		_cache.Events.Miss += OnCacheMiss;
		_cache.Events.FactorySuccess += OnFactorySuccess;
		_cache.Events.FactoryError += OnFactoryError;
		_cache.Events.FactorySyntheticTimeout += OnFactoryTimeout;

		_logger?.LogInformation("SelfTuningCachePlugin started for cache '{CacheName}'", _cache.CacheName);
	}

	/// <summary>
	/// Called when the plugin is removed from a FusionCache instance.
	/// </summary>
	/// <param name="cache">The FusionCache instance.</param>
	public void Stop(IFusionCache cache)
	{
		if (_cache == cache)
		{
			// Unsubscribe from cache events
			_cache.Events.Hit -= OnCacheHit;
			_cache.Events.Miss -= OnCacheMiss;
			_cache.Events.FactorySuccess -= OnFactorySuccess;
			_cache.Events.FactoryError -= OnFactoryError;
			_cache.Events.FactorySyntheticTimeout -= OnFactoryTimeout;

			_cache = null;
		}

		_cleanupTimer?.Dispose();
		_logger?.LogInformation("SelfTuningCachePlugin stopped for cache '{CacheName}'", cache.CacheName);
	}

	/// <summary>
	/// Gets the current metrics for a specific cache key.
	/// </summary>
	/// <param name="key">The cache key.</param>
	/// <returns>The cache entry metrics if available, otherwise null.</returns>
	public CacheEntryMetrics? GetMetrics(string key)
	{
		return _metrics.TryGetValue(key, out var metrics) ? metrics : null;
	}

	/// <summary>
	/// Gets the recommended TTL for a cache key based on current metrics.
	/// </summary>
	/// <param name="key">The cache key.</param>
	/// <returns>The recommended TTL, or null if no recommendation is available.</returns>
	public TimeSpan? GetRecommendedTtl(string key)
	{
		if (!_options.EnableAutoTuning)
			return null;

		var metrics = GetMetrics(key);
		if (metrics == null)
			return null;

		try
		{
			return _tuningAlgorithm.CalculateRecommendedTtl(metrics, _options);
		}
		catch (Exception ex)
		{
			_logger?.LogWarning(ex, "Failed to calculate recommended TTL for key '{Key}'", key);
			return null;
		}
	}

	/// <summary>
	/// Manually records the cost of a cache entry operation.
	/// </summary>
	/// <param name="key">The cache key.</param>
	/// <param name="cost">The cost information to record.</param>
	public void RecordCost(string key, CacheEntryCost cost)
	{
		if (!_options.EnableCostAwareness)
			return;

		var metrics = GetOrCreateMetrics(key);
		metrics.RecordCost(cost);
	}

	/// <summary>
	/// Creates a factory wrapper that automatically applies self-tuning TTL and records costs.
	/// </summary>
	/// <typeparam name="TValue">The type of value being cached.</typeparam>
	/// <param name="key">The cache key.</param>
	/// <param name="originalFactory">The original factory function.</param>
	/// <returns>A wrapped factory that includes self-tuning logic.</returns>
	public Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, Task<TValue>> WrapFactory<TValue>(
		string key,
		Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, Task<TValue>> originalFactory)
	{
		return async (ctx, ct) =>
		{
			var startTime = DateTimeOffset.UtcNow;
			var memoryBefore = GC.GetTotalMemory(false);

			try
			{
				// Apply recommended TTL if available
				var recommendedTtl = GetRecommendedTtl(key);
				if (recommendedTtl.HasValue)
				{
					ctx.Options.Duration = recommendedTtl.Value;
					GetOrCreateMetrics(key).CurrentTtl = recommendedTtl.Value;
				}

				// Execute the original factory
				var result = await originalFactory(ctx, ct);

				// Record operation cost
				var executionTime = DateTimeOffset.UtcNow - startTime;
				var memoryAfter = GC.GetTotalMemory(false);
				var memoryUsed = Math.Max(0, memoryAfter - memoryBefore);

				var cost = new CacheEntryCost
				{
					ComputationTimeMs = executionTime.TotalMilliseconds,
					MemorySizeBytes = memoryUsed,
					Timestamp = DateTimeOffset.UtcNow
				};

				RecordCost(key, cost);

				return result;
			}
			catch (Exception ex)
			{
				_logger?.LogWarning(ex, "Factory execution failed for key '{Key}'", key);
				throw;
			}
		};
	}

	private void OnCacheHit(object? sender, FusionCacheEntryHitEventArgs e)
	{
		var metrics = GetOrCreateMetrics(e.Key);
		metrics.RecordHit();
	}

	private void OnCacheMiss(object? sender, FusionCacheEntryEventArgs e)
	{
		var metrics = GetOrCreateMetrics(e.Key);
		metrics.RecordMiss();
	}

	private void OnFactorySuccess(object? sender, FusionCacheEntryEventArgs e)
	{
		_logger?.LogTrace("Factory success for key '{Key}'", e.Key);
	}

	private void OnCacheSet(object? sender, FusionCacheEntryEventArgs e)
	{
		_logger?.LogTrace("Cache set for key '{Key}'", e.Key);
	}

	private void OnFactoryError(object? sender, FusionCacheEntryEventArgs e)
	{
	}

	private void OnFactoryTimeout(object? sender, FusionCacheEntryEventArgs e)
	{
	}

	private CacheEntryMetrics GetOrCreateMetrics(string key)
	{
		return _metrics.GetOrAdd(key, k => new CacheEntryMetrics(k, _options.BaseTtl));
	}

	private void CleanupMetrics(object? state)
	{
		try
		{
			var cutoff = DateTimeOffset.UtcNow - _options.MetricsRetentionPeriod;
			var keysToRemove = new List<string>();

			foreach (var kvp in _metrics)
			{
				if (kvp.Value.LastAccessTime < cutoff)
				{
					keysToRemove.Add(kvp.Key);
				}
			}

			foreach (var key in keysToRemove)
			{
				_metrics.TryRemove(key, out _);
			}

			// If we're still over the limit, remove oldest entries
			if (_metrics.Count > _options.MaxTrackedEntries)
			{
				var entriesToRemove = _metrics
					.OrderBy(kvp => kvp.Value.LastAccessTime)
					.Take(_metrics.Count - _options.MaxTrackedEntries)
					.Select(kvp => kvp.Key)
					.ToList();

				foreach (var key in entriesToRemove)
				{
					_metrics.TryRemove(key, out _);
				}
			}

			_logger?.LogDebug("Cleaned up {RemovedCount} stale metrics entries. Total entries: {TotalCount}",
				keysToRemove.Count, _metrics.Count);
		}
		catch (Exception ex)
		{
			_logger?.LogWarning(ex, "Error during metrics cleanup");
		}
	}

	/// <summary>
	/// Disposes of the plugin resources.
	/// </summary>
	public void Dispose()
	{
		_cleanupTimer?.Dispose();
		_metrics.Clear();
	}
}
