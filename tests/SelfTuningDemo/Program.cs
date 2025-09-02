using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Plugins.SelfTuning;
using ZiggyCreatures.Caching.Fusion.Plugins.SelfTuning.Models;

namespace SelfTuningDemo;

class Program
{
	static void Main(string[] args)
	{
		Console.WriteLine("FusionCache Self-Tuning Plugin Demo");
		Console.WriteLine("====================================");

		// Create self-tuning options
		var options = new SelfTuningOptions
		{
			BaseTtl = TimeSpan.FromMinutes(5),
			MinTtl = TimeSpan.FromMinutes(1),
			MaxTtl = TimeSpan.FromHours(1),
			TargetHitRate = 0.8,
			MinAccessesForTuning = 3,
			EnableAutoTuning = true,
			EnableCostAwareness = true
		};

		// Create plugin
		var plugin = new SelfTuningCachePlugin(options);

		// Create cache
		using var cache = new FusionCache(new FusionCacheOptions());
		cache.AddPlugin(plugin);

		Console.WriteLine("Created cache with self-tuning plugin");

		// Test basic functionality
		var key = "test-key";

		Console.WriteLine("\nSimulating cache operations...");

		// First access - cache miss
		var value1 = cache.GetOrSet(key, _ =>
		{
			Console.WriteLine("Factory called for first access");
			return "Hello, World!";
		});
		Console.WriteLine($"First access: {value1}");

		// Second access - cache hit
		var value2 = cache.GetOrSet(key, _ =>
		{
			Console.WriteLine("Factory called for second access");
			return "This shouldn't be called";
		});
		Console.WriteLine($"Second access: {value2}");

		// Check metrics
		var metrics = plugin.GetMetrics(key);
		if (metrics != null)
		{
			Console.WriteLine($"\nMetrics for key '{key}':");
			Console.WriteLine($"  Total accesses: {metrics.TotalAccesses}");
			Console.WriteLine($"  Hit count: {metrics.HitCount}");
			Console.WriteLine($"  Miss count: {metrics.MissCount}");
			Console.WriteLine($"  Hit rate: {metrics.HitRate:P2}");
			Console.WriteLine($"  Current TTL: {metrics.CurrentTtl}");
		}

		// Record a cost
		var cost = new CacheEntryCost
		{
			ComputationTimeMs = 150,
			MemorySizeBytes = 1024,
			MonetaryCost = 0.01m
		};
		plugin.RecordCost(key, cost);

		Console.WriteLine($"\nRecorded cost: {cost.GetTotalCostScore():F2}");

		// Get recommended TTL
		var recommendedTtl = plugin.GetRecommendedTtl(key);
		if (recommendedTtl.HasValue)
		{
			Console.WriteLine($"Recommended TTL: {recommendedTtl.Value}");
		}
		else
		{
			Console.WriteLine("No TTL recommendation available yet (need more accesses)");
		}

		// Simulate more accesses to trigger tuning
		Console.WriteLine("\nSimulating more cache accesses...");
		for (int i = 0; i < 5; i++)
		{
			cache.GetOrSet(key, _ => "Factory shouldn't be called");
		}

		// Check updated metrics
		metrics = plugin.GetMetrics(key);
		if (metrics != null)
		{
			Console.WriteLine($"\nUpdated metrics for key '{key}':");
			Console.WriteLine($"  Total accesses: {metrics.TotalAccesses}");
			Console.WriteLine($"  Hit count: {metrics.HitCount}");
			Console.WriteLine($"  Miss count: {metrics.MissCount}");
			Console.WriteLine($"  Hit rate: {metrics.HitRate:P2}");
		}

		// Get updated TTL recommendation
		recommendedTtl = plugin.GetRecommendedTtl(key);
		if (recommendedTtl.HasValue)
		{
			Console.WriteLine($"Updated recommended TTL: {recommendedTtl.Value}");
		}

		Console.WriteLine("\nDemo completed successfully!");
	}
}
