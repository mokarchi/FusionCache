using FusionCacheTests.Stuff;
using Xunit;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Plugins.SelfTuning;
using ZiggyCreatures.Caching.Fusion.Plugins.SelfTuning.Models;

namespace FusionCacheTests.Plugins.SelfTuning;

public sealed class SelfTuningCachePluginTests
{
	[Fact]
	public void Plugin_CanStartAndStop()
	{
		var options = new SelfTuningOptions
		{
			BaseTtl = TimeSpan.FromMinutes(5),
			MinTtl = TimeSpan.FromMinutes(1),
			MaxTtl = TimeSpan.FromHours(1)
		};

		var plugin = new SelfTuningCachePlugin(options);
		using var cache = new FusionCache(new FusionCacheOptions());

		// Add and start plugin
		cache.AddPlugin(plugin);

		// Plugin should be started
		var plugins = TestsUtils.GetPlugins(cache);
		Assert.NotNull(plugins);
		Assert.Contains(plugin, plugins);

		// Remove plugin
		var removed = cache.RemovePlugin(plugin);
		Assert.True(removed);
	}

	[Fact]
	public void Plugin_TracksMetrics()
	{
		var options = new SelfTuningOptions
		{
			EnableAutoTuning = true,
			MinAccessesForTuning = 1
		};

		var plugin = new SelfTuningCachePlugin(options);
		using var cache = new FusionCache(new FusionCacheOptions());
		cache.AddPlugin(plugin);

		var key = "test-key";

		// Initially no metrics
		var initialMetrics = plugin.GetMetrics(key);
		Assert.Null(initialMetrics);

		// Trigger a cache miss (should create metrics)
		var value = cache.GetOrSet(key, _ => "test-value");
		Assert.Equal("test-value", value);

		// Now we should have metrics
		var metrics = plugin.GetMetrics(key);
		Assert.NotNull(metrics);
		Assert.Equal(key, metrics.Key);
		Assert.Equal(1, metrics.MissCount);
		Assert.Equal(0, metrics.HitCount);
	}

	[Fact]
	public void Plugin_RecordsCosts()
	{
		var options = new SelfTuningOptions
		{
			EnableCostAwareness = true
		};

		var plugin = new SelfTuningCachePlugin(options);
		using var cache = new FusionCache(new FusionCacheOptions());
		cache.AddPlugin(plugin);

		var key = "cost-test-key";
		var cost = new CacheEntryCost
		{
			ComputationTimeMs = 100,
			MemorySizeBytes = 1024,
			MonetaryCost = 0.05m
		};

		// Record cost manually
		plugin.RecordCost(key, cost);

		var metrics = plugin.GetMetrics(key);
		Assert.NotNull(metrics);
		Assert.True(metrics.AverageCost > 0);
	}

	[Fact]
	public void Plugin_CalculatesRecommendedTtl()
	{
		var options = new SelfTuningOptions
		{
			EnableAutoTuning = true,
			BaseTtl = TimeSpan.FromMinutes(5),
			MinAccessesForTuning = 1,
			TargetHitRate = 0.8
		};

		var plugin = new SelfTuningCachePlugin(options);
		using var cache = new FusionCache(new FusionCacheOptions());
		cache.AddPlugin(plugin);

		var key = "ttl-test-key";

		// Create some cache activity to generate metrics
		cache.GetOrSet(key, _ => "value1"); // Miss
		cache.GetOrSet(key, _ => "value2"); // Hit
		cache.GetOrSet(key, _ => "value3"); // Hit

		var recommendedTtl = plugin.GetRecommendedTtl(key);
		Assert.NotNull(recommendedTtl);
		Assert.True(recommendedTtl.Value >= options.MinTtl);
		Assert.True(recommendedTtl.Value <= options.MaxTtl);
	}

	[Fact]
	public async Task Plugin_IntegratesWithAdaptiveCaching()
	{
		var options = new SelfTuningOptions
		{
			EnableAutoTuning = true,
			BaseTtl = TimeSpan.FromMinutes(2),
			MinAccessesForTuning = 1
		};

		var plugin = new SelfTuningCachePlugin(options);
		using var cache = new FusionCache(new FusionCacheOptions());
		cache.AddPlugin(plugin);

		var key = "adaptive-test-key";
		var factoryCallCount = 0;

		// Create a factory that will be wrapped by the plugin
		var factory = plugin.WrapFactory<string>(key, (ctx, ct) =>
		{
			factoryCallCount++;

			// The plugin should have applied a recommended TTL
			// We can't easily test the exact value, but we can verify the factory was called
			return Task.FromResult($"value-{factoryCallCount}");
		});

		// Call the wrapped factory (use named argument to disambiguate overload)
		var result = await cache.GetOrSetAsync(key, factory: factory);

		Assert.Equal("value-1", result);
		Assert.Equal(1, factoryCallCount);

		// Verify metrics were recorded
		var metrics = plugin.GetMetrics(key);
		Assert.NotNull(metrics);
		Assert.True(metrics.TotalAccesses > 0);
	}

	[Fact]
	public void Plugin_HandlesHighFrequencyAccess()
	{
		var options = new SelfTuningOptions
		{
			EnableAutoTuning = true,
			MinAccessesForTuning = 5,
			TargetHitRate = 0.7
		};

		var plugin = new SelfTuningCachePlugin(options);
		using var cache = new FusionCache(new FusionCacheOptions());
		cache.AddPlugin(plugin);

		var key = "high-frequency-key";

		// Simulate high frequency access
		for (int i = 0; i < 10; i++)
		{
			cache.GetOrSet(key, _ => $"value-{i}");
		}

		var metrics = plugin.GetMetrics(key);
		Assert.NotNull(metrics);
		Assert.Equal(10, metrics.TotalAccesses);

		// Should have 1 miss (first call) and 9 hits
		Assert.Equal(1, metrics.MissCount);
		Assert.Equal(9, metrics.HitCount);
		Assert.Equal(0.9, metrics.HitRate, 2); // 90% hit rate

		var recommendedTtl = plugin.GetRecommendedTtl(key);
		Assert.NotNull(recommendedTtl);
	}

	[Fact]
	public void SelfTuningOptions_HasReasonableDefaults()
	{
		var options = new SelfTuningOptions();

		Assert.Equal(TimeSpan.FromMinutes(1), options.MinTtl);
		Assert.Equal(TimeSpan.FromHours(24), options.MaxTtl);
		Assert.Equal(TimeSpan.FromMinutes(5), options.BaseTtl);
		Assert.Equal(0.8, options.TargetHitRate);
		Assert.Equal(10, options.MinAccessesForTuning);
		Assert.True(options.EnableAutoTuning);
		Assert.True(options.EnableCostAwareness);
		Assert.Equal(10_000, options.MaxTrackedEntries);
	}

	[Fact]
	public void CacheEntryCost_CalculatesTotalScore()
	{
		var cost = new CacheEntryCost
		{
			ComputationTimeMs = 1000, // 1 second
			MemorySizeBytes = 1024 * 1024, // 1 MB
			MonetaryCost = 0.10m, // 10 cents
			CustomCostFactor = 2.0
		};

		var totalScore = cost.GetTotalCostScore();

		// Should combine normalized values: 1.0 (seconds) + 1.0 (MB) + 0.10 (dollars) = 2.1, then * 2.0 = 4.2
		Assert.Equal(4.2, totalScore, 1);
	}
}
