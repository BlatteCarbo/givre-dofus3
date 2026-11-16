# PacketBus competing-operation test synchronization

Full managed run bash_231 completed exit1:4811 total,4808 passed,one failed,
2 existing skips,4m27s. The only failure was the existing competing PacketBus
test's3s TimeoutException, also observed in bash_222. Parent read PacketBus
and the entire race test before changing the fixture; no bus defect was proven.

The race test previously queued both Publish and Cancel through Task.Run while
its20ms timeout was already active. The operations could remain unscheduled
under suite load, so the outer3s bound could fail before either operation ran.
The earlier isolated run passed; this is a scheduling-risk finding, not proof
that every previous timeout had that cause.

The fixture now uses dedicated LongRunning workers and a three-party Barrier.
Both operations reach the barrier before release, with3s bounded rendezvous.
The waiter is still registered before release. All150 iterations,20ms bus
timeout,3s completion bounds and exact message/cancellation/timeout assertions
remain. Production PacketBus is unchanged; no failures or warnings suppressed.

bash_232/mon_H6HWVAM6ZH7C2C0G completed exit0:16 passed,zero failed/skipped,
815ms, including all PacketBus cases and both recursive-level shield rows.
Full managed validation is subscribed as bash_233/mon_SVVKPMB5783N5MXH with
packetbus-barrier-full.trx. Completed exit1:4811 total,4808 passed,one failed,
2 existing skips,4m11s. PacketBus passed in this full run. The sole failure
was LocalHandshakeRelayTests.Game_observer_completes_before_forwarding_even_without_turbo_and_exceptions_still_forward
(throws:true), with TaskCanceledException. No relay change was made.
After testhost11516 exited, bash_235 ran the unchanged relay class alone:
8 passed,zero failed/skipped,141ms,exit0,relay-observer-isolated.trx.
This establishes load-sensitive behavior but does not prove its root cause.
The complete suite is not declared green and deployment remains pending.

Parent rejected the st_01a1af89 diagnostic report after source comparison.
It describes EchoServer/RegisterGameObserver/PRE-OBSERVE/ReceiveAsync code
absent from the current test, and claims NoDelay is absent. Current production
LocalHandshakeRelay.cs105 sets client.NoDelay=true and131 constructs upstream
with NoDelay=true. Current test sends server-to-client byte42, blocks OnData
on an explicit release signal and then awaits the client read. Its exception
catch does not call the logger alleged by the report. TRX4011-4013 contains
TaskCanceledException and the test method only, no source line. Consequently
the exact failed await remains unknown; no speculative network fix is applied.

bash_236/mon_WA349FENZQVQAB43 subsequently completed exit0 with212 passing
tests, combining LocalHandshakeRelayTests,CombatTurnPlannerTests and
CombatPlannerResolverTests. The observer theory rows passed in66ms and8ms.
This targeted planner-load reproduction did not reproduce the full-suite
cancellation. It narrows the evidence: planner tests alone are insufficient
to trigger it. Root cause remains unproven; no production relay change.

Later full restored-source run bash_242 completed exit0 with4809 passed,
zero failed,2 existing skips (4811 total),3m45s. PacketBus and the unchanged
relay tests passed together in companion-owner-restored-full.trx. This is a
clean full-run receipt, not proof that the earlier relay cancellation's
unestablished cause has been fixed.
