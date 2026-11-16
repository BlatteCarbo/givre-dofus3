# Native tactical cleanup acknowledgment receipt

Date: 2026-11-04. Scope: native tactical cleanup only; offline validation.

## Correction

Renderer restoration retains GC roots when the liveness invocation, bool unboxing, or enabled setter fails. Overlay destruction retains its root on liveness or destruction failure. Map identity and layer roots remain owned while map cleanup is pending. A subsequent valid map lease retries pending cleanup before activation can overwrite its resources. Addressable release failure retains the prefab root and prevents the matching cleanup ACK. Successful void invocations returning null are accepted; IL2CPP exceptions are not. Already-destroyed Unity objects can release their roots without invoking setters or Destroy.

DNM_Tick still returns before any Unity call if lease::read rejects the lease. No deployment, injection, or game process was used.

## Regression evidence

The native ABI fixture drives the production lease-tick handler, writes real ACK files in isolated temporary directories, and injects IL2CPP exceptions through runtime_invoke. It covers Object.op_Implicit, Object.Destroy, Renderer.set_enabled, and reflected AddressableUtility release failures. Every failure withholds the ACK and retains necessary ownership; removing the exception allows retry to restore the original true and false renderer states and write the exact lease token. A separate case covers legitimately destroyed Unity objects. No sleeps or source/prose assertions were added.

A targeted mutation changed release_prefab to report success after release failure. Command: `cargo test -p dofus-native-tactical cleanup_tests::cleanup_ack_requires_successful_restore_destroy_liveness_and_release`. Exit 101, assertion `failure 4 falsely acknowledged`. The mutation was restored before the final successful test/build run. The complete historical pre-fix implementation was not restored because this shared workspace contains existing changes.

## Final commands

Working directory: `native/dofus3-native-host`.

- `cargo test -p dofus-native-tactical`: exit 0; 19 passed, 0 failed, 0 ignored.
- `cargo build -p dofus-native-tactical`: exit 0.
- `cargo clippy -p dofus-native-tactical --all-targets -- -D warnings`: exit 101; existing `clippy::needless_return` in process_build at `src/lib.rs:993`. The original inspected code already contained that return. It was left outside this correction.
- `cargo +nightly miri test -p dofus-native-tactical`: exit 101; installed Cargo reports `no such command: +nightly` and requests invoking through rustup. `cargo miri --version` also reports `no such command: miri` (exit 101). Miri/unsafe interpreter validation remains unavailable.

An initial fixture run failed because its get_class stub returned null before reflection array allocation; the fixture was corrected to resolve its fake Object class. One combined command used PowerShell syntax in the Bash-backed tool and exited 2 before running checks; the final commands above used Bash syntax.

## Limits and review

This is offline ABI-fixture evidence, not proof of live Unity behavior. Destroy success means the Unity Destroy invocation returned without exception, matching the existing asynchronous destruction contract. No new unsafe production operation or dependency was added. Test-only ABI callbacks use initialized host tables and documented pointer reads; the fixture does not execute real Unity code. Existing large lib.rs structure was preserved instead of undertaking an unrelated refactor. Existing changes in the allowed subtree were preserved; no git commands or commits were used.
