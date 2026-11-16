using System;
using System.Collections.Generic;
using GivreClone.Engine;

namespace CircuitBreakerTest;

class Program
{
    static void Main()
    {
        Console.WriteLine("Running BehaviouralCircuitBreaker Unit Tests...");

        // Test 1: Micro-pause after 2h (simulated interval)
        {
            long intervalMs = 1000;
            var cb = new BehaviouralCircuitBreaker(microPauseIntervalMs: intervalMs, ultraFastThresholdSec: 30.0, maxUltraFastCombats: 5, initialTick: 0);
            int sleepCalled = 0;
            var logs = new List<string>();

            // Initially no pause at tick 0
            int paused = cb.CheckAndApplyPauses(ms => sleepCalled += ms, msg => logs.Add(msg), currentTick: 0);
            if (paused != 0 || sleepCalled != 0) throw new Exception("Test 1 failed: initial pause occurred unexpectedly.");

            // Advance past 2 hours (tick 1200, delta = 1200 >= 1000)
            paused = cb.CheckAndApplyPauses(ms => sleepCalled += ms, msg => logs.Add(msg), currentTick: 1200);
            if (paused < 5000 || paused > 15000) throw new Exception($"Test 1 failed: micro-pause duration {paused} out of range 5000-15000.");
            if (paused != sleepCalled) throw new Exception("Test 1 failed: sleep function not called with pause duration.");
            Console.WriteLine("✓ Micro-pause test passed.");
        }

        // Test 2: Cooling pause after 5 fast fights
        {
            var cb = new BehaviouralCircuitBreaker(microPauseIntervalMs: TimeSpan.FromHours(2).Ticks, ultraFastThresholdSec: 30.0, maxUltraFastCombats: 5, initialTick: 0);
            int sleepCalled = 0;
            var logs = new List<string>();

            for (int i = 0; i < 4; i++)
            {
                cb.RecordCombatDuration(10.0);
                int paused = cb.CheckAndApplyPauses(ms => sleepCalled += ms, msg => logs.Add(msg), currentTick: 0);
                if (paused != 0) throw new Exception($"Test 2 failed: pause triggered too early at fight {i+1}.");
            }

            cb.RecordCombatDuration(15.0);
            int coolingPaused = cb.CheckAndApplyPauses(ms => sleepCalled += ms, msg => logs.Add(msg), currentTick: 0);
            if (coolingPaused < 30000 || coolingPaused > 60000) throw new Exception($"Test 2 failed: cooling pause duration {coolingPaused} out of range 30000-60000.");
            if (coolingPaused != sleepCalled) throw new Exception("Test 2 failed: sleep function not called with cooling duration.");
            Console.WriteLine("✓ Cooling pause test passed.");
        }

        // Test 3: Jitter floor (never below 50ms)
        {
            var cb = new BehaviouralCircuitBreaker();
            for (int i = 0; i < 1000; i++)
            {
                int delay = cb.GetJitteredDelayMs(meanMs: 100.0, stdDevMs: 200.0, poissonLambda: 1.0);
                if (delay < 50) throw new Exception($"Test 3 failed: jitter delay {delay}ms fell below 50ms floor!");
            }
            Console.WriteLine("✓ Jitter floor test passed.");
        }

        Console.WriteLine("ALL BEHAVIOURAL CIRCUIT BREAKER TESTS PASSED (3/3).");
    }
}
