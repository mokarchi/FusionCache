using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Plugins.SelfTuning;
using ZiggyCreatures.Caching.Fusion.Plugins.SelfTuning.Models;

namespace AdvancedSelfTuningDemo;

class Program
{
	static async Task Main(string[] args)
	{
		Console.WriteLine("Advanced FusionCache Self-Tuning Plugin Demo");
		Console.WriteLine("=============================================");

		// Create self-tuning options with aggressive tuning for demo
		var options = new SelfTuningOptions
		{
			BaseTtl = TimeSpan.FromSeconds(30),
			MinTtl = TimeSpan.FromSeconds(10),
			MaxTtl = TimeSpan.FromMinutes(5),
			TargetHitRate = 0.8,
			MinAccessesForTuning = 2, // Very low for demo
			EnableAutoTuning = true,
			EnableCostAwareness = true,
			TtlIncreaseMultiplier = 1.5, // More aggressive for demo
			TtlDecreaseMultiplier = 0.7,
			CostSensitivity = 0.3
		};

		var plugin = new SelfTuningCachePlugin(options);
		using var cache = new FusionCache(new FusionCacheOptions());
		cache.AddPlugin(plugin);

		Console.WriteLine("Created cache with aggressive self-tuning settings");

		// Demo 1: Cost-aware caching
		await DemoCostAwareness(cache, plugin);

		// Demo 2: Hit rate based tuning
		await DemoHitRateBasedTuning(cache, plugin);

		// Demo 3: Factory wrapping with automatic cost tracking
		await DemoFactoryWrapping(cache, plugin);

		Console.WriteLine("\n All demos completed successfully!");
	}

	static async Task DemoCostAwareness(IFusionCache cache, SelfTuningCachePlugin plugin)
	{
		Console.WriteLine("\n Demo 1: Cost-Aware Caching");
		Console.WriteLine("==============================");

		var expensiveKey = "expensive-operation";
		var cheapKey = "cheap-operation";

		// Simulate expensive operation
		var expensiveResult = cache.GetOrSet(expensiveKey, _ =>
		{
			Console.WriteLine(" Executing expensive operation...");
			Thread.Sleep(500); // Simulate expensive computation
			return "Expensive Result";
		});

		// Record high cost for expensive operation
		plugin.RecordCost(expensiveKey, new CacheEntryCost
		{
			ComputationTimeMs = 500,
			MemorySizeBytes = 10 * 1024 * 1024, // 10 MB
			MonetaryCost = 0.25m, // 25 cents
			CustomCostFactor = 2.0
		});

		// Simulate cheap operation
		var cheapResult = cache.GetOrSet(cheapKey, _ =>
		{
			Console.WriteLine(" Executing cheap operation...");
			Thread.Sleep(10); // Simulate cheap computation
			return "Cheap Result";
		});

		// Record low cost for cheap operation
		plugin.RecordCost(cheapKey, new CacheEntryCost
		{
			ComputationTimeMs = 10,
			MemorySizeBytes = 1024, // 1 KB
			MonetaryCost = 0.001m, // 0.1 cent
			CustomCostFactor = 0.5
		});

		// Generate some cache hits to improve hit rate
		for (int i = 0; i < 3; i++)
		{
			cache.GetOrSet(expensiveKey, _ => "Should not be called");
			cache.GetOrSet(cheapKey, _ => "Should not be called");
		}

		// Show TTL recommendations based on cost
		ShowMetricsAndRecommendations(plugin, expensiveKey, "Expensive Operation");
		ShowMetricsAndRecommendations(plugin, cheapKey, "Cheap Operation");
	}

	static async Task DemoHitRateBasedTuning(IFusionCache cache, SelfTuningCachePlugin plugin)
	{
		Console.WriteLine("\n Demo 2: Hit Rate Based Tuning");
		Console.WriteLine("=================================");

		var highHitKey = "high-hit-rate";
		var lowHitKey = "low-hit-rate";

		// Create entry with high hit rate
		cache.GetOrSet(highHitKey, _ => "High Hit Value");
		for (int i = 0; i < 10; i++)
		{
			cache.GetOrSet(highHitKey, _ => "Should not be called");
		}

		// Create entry with low hit rate by manually managing metrics
		var lowHitMetrics = new CacheEntryMetrics(lowHitKey, TimeSpan.FromSeconds(30));
		for (int i = 0; i < 10; i++)
		{
			if (i % 3 == 0) // Only every 3rd access is a "hit"
			{
				lowHitMetrics.RecordHit();
			}
			else
			{
				lowHitMetrics.RecordMiss();
			}
		}

		ShowMetricsAndRecommendations(plugin, highHitKey, "High Hit Rate Entry");
		// For the low hit rate demo, we'll use a different approach since GetMetrics won't return our manual metrics
		Console.WriteLine($"\n Low Hit Rate Entry (simulated) ({lowHitKey}):");
		Console.WriteLine($"   Total accesses: {lowHitMetrics.TotalAccesses}");
		Console.WriteLine($"   Hit rate: {lowHitMetrics.HitRate:P1}");
		Console.WriteLine($"   Note: This entry would have a much lower recommended TTL due to poor hit rate");
	}

	static async Task DemoFactoryWrapping(IFusionCache cache, SelfTuningCachePlugin plugin)
	{
		Console.WriteLine("\n Demo 3: Factory Wrapping with Auto Cost Tracking");
		Console.WriteLine("====================================================");

		var wrappedKey = "auto-tracked";

		// Create a factory that will be wrapped for automatic cost tracking
		var originalFactory = plugin.WrapFactory<string>(wrappedKey, async (ctx, ct) =>
		{
			Console.WriteLine(" Wrapped factory executing...");

			// Simulate some work
			await Task.Delay(200, ct);

			// Allocate some memory to simulate memory cost
			var data = new byte[1024 * 1024]; // 1 MB

			return $"Auto-tracked result at {DateTime.Now:HH:mm:ss}";
		});

		// Execute the wrapped factory
		var result = await cache.GetOrSetAsync<string>(wrappedKey, originalFactory, default(MaybeValue<string>), options: null);
		Console.WriteLine($"Result: {result}");

		// Execute again to see cache hit
		var cachedResult = await cache.GetOrSetAsync<string>(wrappedKey, originalFactory, default(MaybeValue<string>), options: null);
		Console.WriteLine($"Cached result: {cachedResult}");

		ShowMetricsAndRecommendations(plugin, wrappedKey, "Auto-Tracked Entry");
	}

	static void ShowMetricsAndRecommendations(SelfTuningCachePlugin plugin, string key, string description)
	{
		var metrics = plugin.GetMetrics(key);
		var recommendedTtl = plugin.GetRecommendedTtl(key);

		Console.WriteLine($"\n {description} ({key}):");
		if (metrics != null)
		{
			Console.WriteLine($"   Total accesses: {metrics.TotalAccesses}");
			Console.WriteLine($"   Hit rate: {metrics.HitRate:P1}");
			Console.WriteLine($"   Average cost: {metrics.AverageCost:F3}");
			Console.WriteLine($"   Current TTL: {metrics.CurrentTtl}");
			Console.WriteLine($"   Access frequency: {metrics.GetAccessFrequencyPerHour():F1}/hour");
		}

		if (recommendedTtl.HasValue)
		{
			Console.WriteLine($"    Recommended TTL: {recommendedTtl.Value}");
			if (metrics != null)
			{
				var change = recommendedTtl.Value.TotalSeconds / metrics.CurrentTtl.TotalSeconds;
				var direction = change > 1 ? " INCREASE" : change < 1 ? " DECREASE" : " MAINTAIN";
				Console.WriteLine($"   {direction} ({change:F2}x current)");
			}
		}
		else
		{
			Console.WriteLine($"    Not enough data for TTL recommendation yet");
		}
	}
}
