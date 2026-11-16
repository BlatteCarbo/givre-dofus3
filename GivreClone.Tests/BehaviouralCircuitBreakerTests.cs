using System;
using System.Collections.Generic;
using GivreClone.Engine;
using Xunit;

namespace GivreClone.Tests;

public class BehaviouralCircuitBreakerTests
{
    [Fact]
    public void Micro_pause_triggers_after_2_hours()
    {
        long intervalMs = 1000; // 1 second for test
        var cb = new BehaviouralCircuitBreaker(microPauseIntervalMs: intervalMs, ultraFastThresholdSec: 30.0, maxUltraFastCombats: 5, initialTick: 100);
        
        int sleepCalled = 0;
        var logs = new List<string>();

        // Initially no pause at tick 100
        int paused = cb.CheckAndApplyPauses(ms => sleepCalled += ms, msg => logs.Add(msg), currentTick: 100);
        Assert.Equal(0, paused);
        Assert.Equal(0, sleepCalled);

        // Advance past 2 hours (tick 1200, delta = 1100 >= 1000)
        paused = cb.CheckAndApplyPauses(ms => sleepCalled += ms, msg => logs.Add(msg), currentTick: 1200);
        Assert.True(paused >= 5000 && paused <= 15000); // 5-15 seconds
        Assert.Equal(paused, sleepCalled);
        Assert.Contains(logs, l => l.Contains("Periodic 2h micro-pause"));
    }

    [Fact]
    public void Cooling_pause_triggers_after_5_fast_fights()
    {
        var cb = new BehaviouralCircuitBreaker(microPauseIntervalMs: TimeSpan.FromHours(2).Ticks, ultraFastThresholdSec: 30.0, maxUltraFastCombats: 5, initialTick: 0);
        
        int sleepCalled = 0;
        var logs = new List<string>();

        // Record 4 fast fights (< 30s)
        for (int i = 0; i < 4; i++)
        {
            cb.RecordCombatDuration(10.0);
            int paused = cb.CheckAndApplyPauses(ms => sleepCalled += ms, msg => logs.Add(msg), currentTick: 0);
            Assert.Equal(0, paused);
        }
        Assert.Equal(0, sleepCalled);

        // Record 5th fast fight
        cb.RecordCombatDuration(15.0);
        int coolingPaused = cb.CheckAndApplyPauses(ms => sleepCalled += ms, msg => logs.Add(msg), currentTick: 0);
        Assert.True(coolingPaused >= 30000 && coolingPaused <= 60000); // 30-60 seconds
        Assert.Equal(coolingPaused, sleepCalled);
        Assert.Contains(logs, l => l.Contains("Safety cooling pause"));
    }

    [Fact]
    public void Jitter_floor_never_drops_below_50ms()
    {
        var cb = new BehaviouralCircuitBreaker();
        for (int i = 0; i < 1000; i++)
        {
            int delay = cb.GetJitteredDelayMs(meanMs: 100.0, stdDevMs: 200.0, poissonLambda: 1.0);
            Assert.True(delay >= 50, $"Jitter delay {delay}ms fell below 50ms floor!");
        }
    }
}
