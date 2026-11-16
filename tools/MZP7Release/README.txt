MZP7 / reconstructed Givre C# providers / 16 October 2026

This is NOT the original GivreClone 1.3.1, and it is NOT a full playable game bot.
Do not drop these DLLs into the installed/running GivreClone directory.

GivreCore.dll : reconstructed protobuf, strictly gated TEST relay and
single-use-authorization native injector. The injector/relay were not
validated against a live game client. They refuse the default untrusted path.
GivreLauncher.Core.dll : reconstructed SOCKS5, HTTP CONNECT and scoped
transport logging only. Missing Ankama/Zaap/auth and legitimate launcher.
GivreClone.Protocol.dll : actual legacy source compiling against reconstructed
GivreCore. The old generated Dofus schema is not 3.7.10.10 certified.
GivreClone.Engine.dll : actual legacy source compiling against reconstructed
GivreCore. Authentication, ACK and gameplay are not certified.

The original GivreClone.App still does not build; this is a set of compiled
libraries rather than a runnable application. See source bundle and
GivreShareWork/docs/MZP7_PRIME_HANDOFF_20261016.md in the shared workspace.

The MZP6 independent offline Avalonia Pro WinExe previously published under
GivreShareWork/artifacts remains the runnable TEST UI, not the full bot.
Original installed GivreClone was not touched, closed, or injected.
