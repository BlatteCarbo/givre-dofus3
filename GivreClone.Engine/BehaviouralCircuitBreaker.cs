using System;
using System.Collections.Generic;

namespace GivreClone.Engine;

/// <summary>
/// Behavioural Circuit Breaker & Human Jitter service to prevent 24/7 automated detection.
/// Features:
/// - Periodic micro-pause: random 5-15 seconds every 2 hours of activity.
/// - Cooling pause: if more than 5 consecutive fights each lasted under 30 seconds, pause 30-60 seconds.
/// - Human jitter: Box-Muller gaussian and Poisson distributions for inter-action delays with a hard floor (never below 50ms).
/// - Idle on map during pauses (no reconnect spam or click bursts).
/// </summary>
public sealed class BehaviouralCircuitBreaker
{
    private readonly object _lock = new();
    private long _sessionStartTick;
    private long _lastMicroPauseTick;
    private readonly long _microPauseIntervalMs;
    private readonly double _ultraFastThresholdSec;
    private readonly int _maxUltraFastCombats;
    private readonly List<double> _recentFightDurations = new();
    private readonly Random _random;

    public BehaviouralCircuitBreaker(
        long microPauseIntervalMs = 2 * 3600 * 1000L, // 2 hours
        double ultraFastThresholdSec = 30.0,
        int maxUltraFastCombats = 5,
        Random? random = null,
        long? initialTick = null)
    {
        long start = initialTick ?? Environment.TickCount64;
        _sessionStartTick = start;
        _lastMicroPauseTick = start;
        _microPauseIntervalMs = microPauseIntervalMs;
        _ultraFastThresholdSec = ultraFastThresholdSec;
        _maxUltraFastCombats = maxUltraFastCombats;
        _random = random ?? Random.Shared;
    }

    /// <summary>
    /// Computes a human-like jitter delay using Box-Muller gaussian and Poisson distribution, with a hard floor (never below 50ms).
    /// </summary>
    public int GetJitteredDelayMs(double meanMs = 350.0, double stdDevMs = 80.0, double poissonLambda = 5.0)
    {
        double u1 = Math.Max(1.0e-7, _random.NextDouble());
        double u2 = _random.NextDouble();
        double z0 = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        double gaussianSample = meanMs + z0 * stdDevMs;

        // Poisson sample approximation
        int k = 0;
        double p = 1.0;
        double L = Math.Exp(-Math.Clamp(poissonLambda, 0.1, 50.0));
        while (true)
        {
            k++;
            p *= _random.NextDouble();
            if (p <= L)
            {
                k = Math.Max(0, k - 1);
                break;
            }
            if (k > 100) break;
        }
        double poissonOffset = k * 10.0;

        double total = gaussianSample + poissonOffset;
        return (int)Math.Max(50.0, total);
    }

    /// <summary>
    /// Checks and executes periodic 2h micro-pause or combat cooling pause if criteria are met.
    /// Returns the pause duration in milliseconds if a pause occurred, or 0 otherwise.
    /// </summary>
    public int CheckAndApplyPauses(Action<int> sleepFn, Action<string> logFn, long? currentTick = null)
    {
        long now = currentTick ?? Environment.TickCount64;
        lock (_lock)
        {
            // 1. Check 2h periodic micro-pause (5-15 seconds)
            if (now - _lastMicroPauseTick >= _microPauseIntervalMs)
            {
                int pauseSec = _random.Next(5, 16); // 5 to 15 seconds
                int pauseMs = pauseSec * 1000;
                logFn($"[circuit-breaker] Periodic 2h micro-pause triggered. Staying idle on map for {pauseSec}s.");
                sleepFn(pauseMs);
                _lastMicroPauseTick = currentTick ?? Environment.TickCount64;
                return pauseMs;
            }

            // 2. Check cooling pause if >= 5 consecutive fights < 30s
            if (_recentFightDurations.Count >= _maxUltraFastCombats)
            {
                int coolingSec = _random.Next(30, 61); // 30 to 60 seconds
                int coolingMs = coolingSec * 1000;
                logFn($"[circuit-breaker] Safety cooling pause: {_recentFightDurations.Count} consecutive ultra-fast combats (< {_ultraFastThresholdSec}s). Cooling for {coolingSec}s idle.");
                sleepFn(coolingMs);
                _recentFightDurations.Clear();
                return coolingMs;
            }
        }
        return 0;
    }

    /// <summary>
    /// Records a completed combat duration in seconds.
    /// </summary>
    public void RecordCombatDuration(double durationSec)
    {
        lock (_lock)
        {
            if (durationSec < _ultraFastThresholdSec)
            {
                _recentFightDurations.Add(durationSec);
            }
            else
            {
                // Reset ultra-fast streak if a normal/slow combat occurred
                _recentFightDurations.Clear();
            }
        }
    }

    /// <summary>
    /// Resets tracking state.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _sessionStartTick = Environment.TickCount64;
            _lastMicroPauseTick = _sessionStartTick;
            _recentFightDurations.Clear();
        }
    }
}
