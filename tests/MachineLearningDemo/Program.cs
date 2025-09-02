using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Plugins.SelfTuning;
using ZiggyCreatures.Caching.Fusion.Plugins.SelfTuning.Algorithms;
using ZiggyCreatures.Caching.Fusion.Plugins.SelfTuning.Models;

namespace MachineLearningDemo;

class Program
{
	static async Task Main(string[] args)
	{
		Console.WriteLine("FusionCache Machine Learning TTL Tuning Demo");
		Console.WriteLine("=============================================");

		// Create options for ML algorithm
		var options = new SelfTuningOptions
		{
			BaseTtl = TimeSpan.FromSeconds(60),
			MinTtl = TimeSpan.FromSeconds(10),
			MaxTtl = TimeSpan.FromMinutes(10),
			TargetHitRate = 0.85,
			MinAccessesForTuning = 3,
			EnableAutoTuning = true,
			EnableCostAwareness = true
		};

		// Use the machine learning algorithm
		var mlAlgorithm = new MachineLearningTtlTuningAlgorithm();
		var plugin = new SelfTuningCachePlugin(options, mlAlgorithm);

		using var cache = new FusionCache(new FusionCacheOptions());
		cache.AddPlugin(plugin);

		Console.WriteLine("Created cache with Machine Learning TTL algorithm");

		await DemoLearningEvolution(cache, plugin, mlAlgorithm);

		Console.WriteLine("\n Machine Learning demo completed!");
	}

	static async Task DemoLearningEvolution(IFusionCache cache, SelfTuningCachePlugin plugin, MachineLearningTtlTuningAlgorithm mlAlgorithm)
	{
		Console.WriteLine("\n Demo: Machine Learning Evolution");
		Console.WriteLine("===================================");

		var key = "ml-learning-key";
		var iterationCount = 15;

		Console.WriteLine($"Simulating {iterationCount} learning iterations...\n");

		for (int iteration = 1; iteration <= iterationCount; iteration++)
		{
			Console.WriteLine($"--- Iteration {iteration} ---");

			// Simulate cache operations with varying patterns
			if (iteration <= 5)
			{
				// Initial phase: establish baseline
				SimulateCacheOperations(cache, key, hitRate: 0.6, 5);
			}
			else if (iteration <= 10)
			{
				// Learning phase: better hit rates
				SimulateCacheOperations(cache, key, hitRate: 0.8, 8);
			}
			else
			{
				// Optimization phase: excellent hit rates
				SimulateCacheOperations(cache, key, hitRate: 0.95, 10);
			}

			// Record some cost to influence learning
			var cost = new CacheEntryCost
			{
				ComputationTimeMs = 100 + (iteration * 10), // Increasing cost over time
				MemorySizeBytes = 1024 * iteration,
				MonetaryCost = 0.01m * iteration
			};
			plugin.RecordCost(key, cost);

			// Get current metrics and recommendation
			var metrics = plugin.GetMetrics(key);
			var recommendedTtl = plugin.GetRecommendedTtl(key);
			var learningStats = mlAlgorithm.GetLearningStatistics(key);

			if (metrics != null && recommendedTtl.HasValue && learningStats != null)
			{
				Console.WriteLine($"  Hit Rate: {metrics.HitRate:P1} | Cost: {metrics.AverageCost:F2}");
				Console.WriteLine($"  Current TTL: {metrics.CurrentTtl.TotalSeconds:F0}s");
				Console.WriteLine($"  Recommended TTL: {recommendedTtl.Value.TotalSeconds:F0}s");
				Console.WriteLine($"  Q-Value: {learningStats.QValue:F3} | Reward: {learningStats.LastReward:F3}");
				Console.WriteLine($"  Exploration Rate: {learningStats.ExplorationRate:P1}");

				var change = recommendedTtl.Value.TotalSeconds / metrics.CurrentTtl.TotalSeconds;
				var trend = change > 1.05 ? " INCREASE" : change < 0.95 ? " DECREASE" : " STABLE";
				Console.WriteLine($"  Trend: {trend} ({change:F2}x)");

				// Apply the recommendation for next iteration
				if (Math.Abs(change - 1.0) > 0.05) // Only apply if significant change
				{
					metrics.CurrentTtl = recommendedTtl.Value;
				}
			}
			else
			{
				Console.WriteLine("  Collecting initial data...");
			}

			Console.WriteLine();

			// Small delay to simulate time passing
			await Task.Delay(100);
		}

		// Show final learning state
		var finalStats = mlAlgorithm.GetLearningStatistics(key);
		var finalMetrics = plugin.GetMetrics(key);

		if (finalStats != null && finalMetrics != null)
		{
			Console.WriteLine(" Final Learning Results:");
			Console.WriteLine($"  Total Adjustments: {finalStats.AdjustmentCount}");
			Console.WriteLine($"  Final Q-Value: {finalStats.QValue:F3}");
			Console.WriteLine($"  Final Exploration Rate: {finalStats.ExplorationRate:P2}");
			Console.WriteLine($"  Final Hit Rate: {finalMetrics.HitRate:P1}");
			Console.WriteLine($"  Algorithm learned to optimize TTL based on performance feedback!");
		}
	}

	static void SimulateCacheOperations(IFusionCache cache, string key, double hitRate, int operationCount)
	{
		var random = new Random();

		for (int i = 0; i < operationCount; i++)
		{
			if (random.NextDouble() <= hitRate)
			{
				// Simulate cache hit by getting existing value
				try
				{
					cache.GetOrSet(key, _ => "Cached Value");
				}
				catch
				{
					// First access might be a miss
					cache.GetOrSet(key, _ => "Initial Value");
				}
			}
			else
			{
				// Simulate cache miss by using a different key or forcing refresh
				cache.GetOrSet($"{key}-miss-{i}", _ => $"Miss Value {i}");
			}
		}
	}
}
