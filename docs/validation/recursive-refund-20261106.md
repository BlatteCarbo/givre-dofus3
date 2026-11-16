# Incomplete recursive utility cannot finance follow-up attacks

The static admission gate recognizes action1160,but its referenced descriptor
can still be absent at runtime. Resolver reports the missing sub-spell while
retaining already resolved parent AP gains. Before the fix,those gains could
finance an attack,with only an uncertainty penalty for the unknown activation.

Extended the existing refund regression with immediate1160 referencing absent
spell999999 grade1. Parent costs1AP and refunds2;starting6AP previously allowed
the subsequent7AP attack for50 damage. bash_268 proves causal failure:
1 failed(new missing-child case),2 passed(existing immediate/deferred cases),
252ms,exit1. Receipt:recursive-refund-red.trx.

After resolving a candidate,planner rejects incomplete resolution with no
modeled enemy damage. This preserves the established partial-direct-damage
contract while refusing speculative utility gains. No invented child effect,
fallback descriptor or game formula was added. Focused validation
bash_269/mon_MBZWZ2T80QSJXD3Z passed262 tests,0 failures,in14s,exit0.
Receipt:recursive-refund-focused.trx. Full suite subscribed as
bash_270/mon_F19V02J8QKCWYPEP completed exit0:4810 passed,0 failed,
2 existing skips,total4812,in4m48s. Receipt:recursive-refund-full.trx.

Artifacts:givre-recursive-refund-20261106,isolated from both running apphosts.
No live deployment or combat acceptance yet. This is not complete class coverage.
