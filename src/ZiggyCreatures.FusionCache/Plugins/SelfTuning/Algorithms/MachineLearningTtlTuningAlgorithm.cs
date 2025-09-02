using ZiggyCreatures.Caching.Fusion.Plugins.SelfTuning.Models;

namespace ZiggyCreatures.Caching.Fusion.Plugins.SelfTuning.Algorithms;

/// <summary>
/// Machine learning inspired TTL tuning algorithm that uses reinforcement learning principles.
/// This algorithm learns from past adjustments and their outcomes to improve future recommendations.
/// </summary>
public class MachineLearningTtlTuningAlgorithm : ITtlTuningAlgorithm
{
	private readonly Dictionary<string, LearningState> _learningStates = new();
	private readonly object _lock = new();

	private class LearningState
	{
		public double LastReward { get; set; }
		public TimeSpan LastRecommendation { get; set; }
		public double LearningRate { get; set; } = 0.1;
		public double ExplorationRate { get; set; } = 0.1;
		public double QValue { get; set; } = 0.5; // Q-value for reinforcement learning
		public int AdjustmentCount { get; set; }
	}

	/// <summary>
	/// Calculates the recommended TTL using machine learning principles.
	/// </summary>
	/// <param name="metrics">The cache entry metrics.</param>
	/// <param name="options">The self-tuning options.</param>
	/// <returns>The recommended TTL.</returns>
	public TimeSpan CalculateRecommendedTtl(CacheEntryMetrics metrics, SelfTuningOptions options)
	{
		if (metrics.TotalAccesses < options.MinAccessesForTuning)
		{
			return options.BaseTtl;
		}

		lock (_lock)
		{
			var learningState = GetOrCreateLearningState(metrics.Key);

			// Calculate reward based on current performance
			var reward = CalculateReward(metrics, options);

			// Update Q-value using Q-learning formula: Q = Q + α[R + γQ' - Q]
			// Simplified version without future state since we're dealing with immediate rewards
			learningState.QValue += learningState.LearningRate * (reward - learningState.QValue);
			learningState.LastReward = reward;

			// Decide action using ε-greedy strategy
			var shouldExplore = new Random().NextDouble() < learningState.ExplorationRate;

			TimeSpan recommendedTtl;
			if (shouldExplore)
			{
				// Exploration: try a random adjustment
				recommendedTtl = ExploreAction(metrics, options);
			}
			else
			{
				// Exploitation: use learned knowledge
				recommendedTtl = ExploitAction(metrics, options, learningState);
			}

			// Decay exploration rate over time (less exploration as we learn more)
			learningState.ExplorationRate = Math.Max(0.01, learningState.ExplorationRate * 0.995);
			learningState.AdjustmentCount++;
			learningState.LastRecommendation = recommendedTtl;

			return ClampTtl(recommendedTtl, options);
		}
	}

	/// <summary>
	/// Calculates reward based on cache performance metrics.
	/// </summary>
	private double CalculateReward(CacheEntryMetrics metrics, SelfTuningOptions options)
	{
		// Multi-objective reward function considering:
		// 1. Hit rate performance (primary objective)
		// 2. Cost efficiency (secondary objective)
		// 3. Access frequency (tertiary objective)

		var hitRateReward = CalculateHitRateReward(metrics.HitRate, options.TargetHitRate);
		var costReward = CalculateCostReward(metrics.AverageCost);
		var frequencyReward = CalculateFrequencyReward(metrics.GetAccessFrequencyPerHour());

		// Weighted combination of rewards
		return (hitRateReward * 0.6) + (costReward * 0.3) + (frequencyReward * 0.1);
	}

	/// <summary>
	/// Calculates reward based on hit rate performance.
	/// </summary>
	private double CalculateHitRateReward(double hitRate, double targetHitRate)
	{
		// Reward function that peaks at target hit rate
		var distance = Math.Abs(hitRate - targetHitRate);
		return Math.Max(0, 1.0 - (distance / targetHitRate));
	}

	/// <summary>
	/// Calculates reward based on cost efficiency.
	/// </summary>
	private double CalculateCostReward(double averageCost)
	{
		// Lower costs get higher rewards, but with diminishing returns
		return 1.0 / (1.0 + averageCost * 0.1);
	}

	/// <summary>
	/// Calculates reward based on access frequency.
	/// </summary>
	private double CalculateFrequencyReward(double accessFrequency)
	{
		// Moderate frequency gets highest reward (not too high, not too low)
		var optimalFrequency = 5.0; // 5 accesses per hour
		var distance = Math.Abs(accessFrequency - optimalFrequency);
		return Math.Max(0, 1.0 - (distance / optimalFrequency));
	}

	/// <summary>
	/// Exploration action: try a random adjustment.
	/// </summary>
	private TimeSpan ExploreAction(CacheEntryMetrics metrics, SelfTuningOptions options)
	{
		var random = new Random();
		var factor = 0.5 + random.NextDouble(); // Random factor between 0.5 and 1.5
		var baseTtl = metrics.CurrentTtl.TotalMilliseconds;
		return TimeSpan.FromMilliseconds(baseTtl * factor);
	}

	/// <summary>
	/// Exploitation action: use learned knowledge to make adjustment.
	/// </summary>
	private TimeSpan ExploitAction(CacheEntryMetrics metrics, SelfTuningOptions options, LearningState learningState)
	{
		// Use Q-value to determine adjustment direction and magnitude
		var adjustmentFactor = 1.0 + ((learningState.QValue - 0.5) * 0.5); // Maps Q-value to adjustment factor

		// Apply additional heuristics based on metrics
		if (metrics.HitRate > options.TargetHitRate)
		{
			adjustmentFactor *= 1.1; // Increase TTL if hit rate is good
		}
		else
		{
			adjustmentFactor *= 0.9; // Decrease TTL if hit rate is poor
		}

		// Consider cost in adjustment
		if (metrics.AverageCost > 1.0)
		{
			adjustmentFactor *= 1.05; // Slightly increase TTL for expensive operations
		}

		var baseTtl = metrics.CurrentTtl.TotalMilliseconds;
		return TimeSpan.FromMilliseconds(baseTtl * adjustmentFactor);
	}

	/// <summary>
	/// Gets or creates learning state for a cache key.
	/// </summary>
	private LearningState GetOrCreateLearningState(string key)
	{
		if (!_learningStates.TryGetValue(key, out var state))
		{
			state = new LearningState();
			_learningStates[key] = state;
		}
		return state;
	}

	/// <summary>
	/// Clamps TTL to configured bounds.
	/// </summary>
	private TimeSpan ClampTtl(TimeSpan ttl, SelfTuningOptions options)
	{
		if (ttl < options.MinTtl)
			return options.MinTtl;
		if (ttl > options.MaxTtl)
			return options.MaxTtl;
		return ttl;
	}

	/// <summary>
	/// Gets learning statistics for monitoring purposes.
	/// </summary>
	/// <param name="key">The cache key.</param>
	/// <returns>Learning statistics for the key, or null if not found.</returns>
	public LearningStatistics? GetLearningStatistics(string key)
	{
		lock (_lock)
		{
			if (_learningStates.TryGetValue(key, out var state))
			{
				return new LearningStatistics
				{
					QValue = state.QValue,
					LastReward = state.LastReward,
					ExplorationRate = state.ExplorationRate,
					AdjustmentCount = state.AdjustmentCount,
					LastRecommendation = state.LastRecommendation
				};
			}
			return null;
		}
	}

	/// <summary>
	/// Statistics about the learning progress for a cache key.
	/// </summary>
	public class LearningStatistics
	{
		/// <summary>
		/// The current Q-value representing the learned quality of TTL adjustments.
		/// </summary>
		public double QValue { get; set; }

		/// <summary>
		/// The reward received from the last TTL adjustment.
		/// </summary>
		public double LastReward { get; set; }

		/// <summary>
		/// The current exploration rate (probability of trying random adjustments).
		/// </summary>
		public double ExplorationRate { get; set; }

		/// <summary>
		/// The total number of TTL adjustments made for this cache key.
		/// </summary>
		public int AdjustmentCount { get; set; }

		/// <summary>
		/// The last TTL recommendation made by the algorithm.
		/// </summary>
		public TimeSpan LastRecommendation { get; set; }
	}
}
