using System.IO;
using System.Security.Cryptography;
using Xunit;

namespace GivreClone.Tests;

public sealed class MitmTurboSourceTests
{
    [Fact]
    public void Movement_tracks_fast_replies_before_transport_dispatch()
    {
        var source = Source("GivreClone.App/Services/LaunchService.Combat.Turn.cs");
        int send = source.IndexOf("if (!MoveTo(pid, forgeMap, src, to,", StringComparison.Ordinal);
        int request = source.IndexOf("long moveRequestAt = NoteTurboRequestTx", StringComparison.Ordinal);
        int refused = source.IndexOf("int refused0 = _moveRefused.GetValueOrDefault(pid)", StringComparison.Ordinal);
        Assert.True(request >= 0 && request < send);
        Assert.True(refused >= 0 && refused < send);
        Assert.Contains("_moveRefused.GetValueOrDefault(pid) > refused0", source);
    }

    private static string Source(string relative)
        => File.ReadAllText(Path.Combine(MappingScan.RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void TurboPreset_IsMitmOnly_AndKeepsLegacyEnumOrdinals()
    {
        var profile = Source("GivreClone.App/Services/CombatProfile.cs");
        var combat = Source("GivreClone.App/Services/LaunchService.Combat.cs");

        Assert.Contains("Instant = 0", profile);
        Assert.Contains("Fast = 1", profile);
        Assert.Contains("Slow = 2", profile);
        Assert.Contains("Turbo = 3", profile);

        Assert.Contains("profile.Speed == CombatSpeed.Turbo && !IsSocketPid(pid)", combat);
        Assert.Contains("profile.NoAnim || turboMitm", combat);
        Assert.Contains("profile.Collapse || turboMitm", combat);
        Assert.Contains("profile.InstantMove || turboMitm", combat);
        Assert.Contains("CombatVisualModPolicy.Effective", combat);
        Assert.Contains("CombatVisualModPolicy.HasPinnedStandaloneSkipper", combat);
        Assert.Contains("LicenseService.Perm.NoAnim", combat);
    }

    [Fact]
    public void TurboTelemetry_CorrelatesSequenceAck_WithoutForgingAck()
    {
        var combat = Source("GivreClone.App/Services/LaunchService.Combat.cs");
        var packets = Source("GivreClone.App/Services/LaunchService.Packets.cs");
        var turn = Source("GivreClone.App/Services/LaunchService.Combat.Turn.cs");

        Assert.Contains("_turboLastSelfSeqEnd", combat);
        Assert.Contains("_turboLastAck", combat);
        Assert.Contains("ResetTurboMetrics", combat);
        Assert.Contains("NoteTurboRequestTx", turn);
        Assert.Contains("move_request_to_server_", turn);

        Assert.Contains("p.ClientToServer && p.Name == \"GameActionAcknowledgementRequest\"", packets);
        Assert.Contains("seq.ActionId == ackAction", packets);
        Assert.Contains("sequence_end_to_ack_ms=", packets);
        Assert.Contains("request_to_sequence_end_ms=", packets);

        // Le chemin MITM observe l'ACK du client visible ; il ne doit pas introduire ici une forge d'ACK parallèle.
        Assert.DoesNotContain("BuildGameActionAcknowledgement", packets);
        Assert.DoesNotContain("Time.timeScale", combat + packets + turn);
    }

    [Fact]
    public void TurboEngine_WaitsOwnSequenceAndMatchingVisibleAck_BeforeNextAction()
    {
        var combat = Source("GivreClone.App/Services/LaunchService.Combat.cs");
        var packets = Source("GivreClone.App/Services/LaunchService.Packets.cs");
        var turn = Source("GivreClone.App/Services/LaunchService.Combat.Turn.cs");
        var policy = Source("GivreClone.App/Services/TurboFightPolicy.cs");

        Assert.Contains("WaitTurboActionBarrier", combat);
        // The action-id and sequence-timestamp checks were extracted into the
        // tested pure policy. The engine must call it rather than duplicate it.
        Assert.Contains("TurboFightPolicy.BarrierClosed(requestAt, seq,", combat);
        Assert.Contains("ack.ActionId == seq.ActionId", policy);
        Assert.Contains("seq.At < requestAt", policy);
        Assert.Contains("_myTurnOpen.GetValueOrDefault(pid)", combat);
        Assert.Contains("BARRIERE NON FERMEE", combat);
        Assert.Contains("p.ClientToServer && p.Name == \"GameActionAcknowledgementRequest\"", packets);
        Assert.Contains("author == SelfActorId(p.Pid)", packets);
        Assert.Contains("PulseTurboBarrier(p.Pid)", packets);

        Assert.Equal(7, turn.Split("await WaitAfterCast(castRequestAt)").Length - 1);   // 7 : + la branche « case adjacente »
        Assert.Contains("!await WaitTurboActionBarrierWithGrace(pid, moveRequestAt)", turn);
        // La seconde chance reste la MÊME barrière autoritaire (délai court d'abord, puis grâce bornée).
        // Sonde courte (séquence ouverte ?) puis délai complet seulement si le serveur a accepté l'action.
        Assert.Contains("WaitTurboActionBarrier(pid, requestAt, probeMs)", combat);
        Assert.Contains("if (!TurboSequenceStartedSince(pid, requestAt)) return false;", combat);
        Assert.Contains("WaitTurboActionBarrier(pid, requestAt, fullMs - probeMs)", combat);
        Assert.Contains("WaitTurboActionBarrier(pid, requestAt, TurboBarrierGraceMs)", combat);
        // Turbo (MITM et Socket) : plus aucun délai fixe avant la fin de tour, la barrière de séquence suffit.
        Assert.Contains("if (profile.Speed != CombatSpeed.Turbo)", turn);
        Assert.DoesNotContain("BuildGameActionAcknowledgement", combat + packets + turn);
    }

    [Fact]
    public void TurboLua_UsesTheSameAuthoritativeBarrier_AndNoFinalSleep()
    {
        var script = Source("GivreClone.App/Services/LaunchService.Combat.Script.cs");

        Assert.Contains("SendCast(pid, cell, spellId, out var turboRequestAt)", script);
        Assert.Contains("WaitTurboActionBarrierWithGrace(pid, turboRequestAt).GetAwaiter().GetResult()", script);
        Assert.Contains("NoteTurboRequestTx(pid, $\"script-move:{src}->{dest}\")", script);
        Assert.Contains("await WaitTurboActionBarrierWithGrace(pid, turboRequestAt)", script);
        Assert.Contains("if (speed != CombatSpeed.Turbo || IsSocketPid(pid)) await Task.Delay(delay, ct)", script);
        Assert.Contains("CloseScriptTurnAfterTurboBarrierFailure", script);
        Assert.Contains("aucun retry autorisé", script);
        Assert.DoesNotContain("BuildGameActionAcknowledgement", script);
    }

    [Fact]
    public void NativeFallback_IsPerPid_HashPinned_AndNeverInstallsVersionDll()
    {
        var runtime = Source("GivreClone.App/Services/NativeTurboRuntime.cs");
        var combat = Source("GivreClone.App/Services/LaunchService.Combat.cs");
        var project = Source("GivreClone.App/GivreClone.App.csproj");

        Assert.Contains("RuntimeVersion = \"v0.4.0-host+skipper-1.0.8+speedhack-1.0.3\"", runtime);
        Assert.Contains("BootstrapModuleName = \"GivreTurboBootstrap.dll\"", runtime);
        Assert.Contains("HostModuleName = \"DofusNativeHost.dll\"", runtime);
        Assert.Contains("SkipperModId = \"bubble.dofus3.combat-animation-skipper\"", runtime);
        Assert.Contains("SpeedHackModId = \"mizan.dofus3.combat-speedhack\"", runtime);
        Assert.Contains("Environment.SpecialFolder.LocalApplicationData", runtime);
        // Le staging local a été déplacé vers la politique pure qui sépare
        // clone et produit d'origine. Ne plus exiger l'ancien chemin direct.
        Assert.Contains("MZP25NativeModPaths.Turbo(pid", runtime);
        var paths = Source("GivreClone.App/Services/MZP25NativeModPaths.cs");
        Assert.Contains("localClone", paths);
        Assert.Contains("\"NativeTurbo\", pid.ToString", paths);
        Assert.Contains("Directory.CreateDirectory(Path.Combine(root, \"Dofus_Data\"))", runtime);
        Assert.Contains("Path.Combine(root, \"NativeMods\", \"control\", pid.ToString", runtime);
        Assert.Contains("EnsureLoadedAsync(pid, desired, control, skipperTarget, SkipperModId", runtime);
        Assert.Contains("EnsureLoadedAsync(pid, desired, control, speedHackTarget, SpeedHackModId", runtime);
        Assert.Contains("ControlAsync(control, \"LOAD\\t\" + target)", runtime);
        Assert.Contains("RequestState((int)pid, nativeFallback, turboMitm, itemIds:", combat);
        Assert.Contains("LegacyModsTookOwnership", runtime);
        Assert.Contains("GivreMods reprend le no-animation, skipper fallback retiré", runtime);
        Assert.Contains("ProcessStartTicks", runtime);
        Assert.Contains("Generation", runtime);
        Assert.Contains("EnsureCurrent(pid, desired)", runtime);
        Assert.Contains("process.Handle", runtime);
        Assert.Contains("InjectBootstrapOwned", runtime);
        Assert.Contains("EnsureUnloadedAsync", runtime);
        Assert.Contains("STATUS", runtime);
        Assert.Contains("combat-speedhack.lease", runtime);
        Assert.Contains("combat-speedhack.restore-request", runtime);
        Assert.Contains("combat-speedhack.restored", runtime);
        Assert.Contains("RestoreAckMatches", runtime);
        Assert.Contains("EnsureUnloadedAsync(pid, desired, control, SkipperModId, \"skipper\")", runtime);
        Assert.Contains("EnsureUnloadedAsync(pid, desired, control, SpeedHackModId, \"speedhack\")", runtime);
        Assert.DoesNotContain("Path.Combine(root, \"version.dll\")", runtime, System.StringComparison.OrdinalIgnoreCase);

        Assert.Contains("bool nativeFallback = presentation.CanRequestNoAnim && (profile.NoAnim || turboMitm)", combat);
        Assert.Contains("CombatVisualModPolicy.Evaluate", combat);
        Assert.Contains("NativeTurboRuntime.RequestState", combat);
        Assert.Contains("WaitForLegacyModsAsync", runtime);
        Assert.Contains("NativeModules.IsLoaded(pid, \"GivreMods.dll\")", runtime);
        Assert.Contains("NativeTurboRuntime\\DofusNativeBootstrap.dll", project);
        Assert.DoesNotContain("Link=\"version.dll\"", project, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NativeSpeedHack_IsFightScoped_AndRestoresExactBaseline()
    {
        var source = Source("native/GivreTurboRuntime/v0.4.0-skipper1.0.5-speedhack1.0.3/GivreCombatSpeedHack.rs");

        Assert.Contains("const COMBAT_MULTIPLIER: f32 = 8.0", source);
        Assert.Contains("Core.Services.Fight", source);
        Assert.Contains("InputChecker", source);
        Assert.DoesNotContain("FightEntitiesService", source);
        Assert.DoesNotContain("find_fight_state_field", source);
        Assert.Contains("UnityEngine.CoreModule.dll", source);
        Assert.Contains("get_timeScale", source);
        Assert.Contains("set_timeScale", source);
        Assert.Contains("UnityEngine.Time::set_timeScale(System.Single)", source);
        Assert.Contains("write_time_scale_direct", source);
        Assert.Contains("refusing to arm x8 without a direct restore fallback", source);
        Assert.Contains("LEASE_MAX_AGE", source);
        Assert.Contains("combat-speedhack.lease", source);
        Assert.Contains("combat-speedhack.restore-request", source);
        Assert.Contains("combat-speedhack.restored", source);
        Assert.Contains("restore_request_token", source);
        Assert.Contains("last_restored_token", source);
        Assert.Contains("if !active", source);
        Assert.Contains("restore_baseline(state, &mut speed, \"fight inactive\")", source);
        Assert.Contains("restore_baseline(state, &mut speed, \"mod unload\")", source);
        Assert.Contains("speed.baseline = Some(baseline)", source);
    }

    [Fact]
    public void NativeSpeedHack_RestoreAckToken_IsUniqueAcrossManagedRestarts()
    {
        var runtime = Source("GivreClone.App/Services/NativeTurboRuntime.cs");

        Assert.Contains("RestoreSessionNonce = Guid.NewGuid().ToString(\"N\")", runtime);
        Assert.Contains("{desired.ProcessStartTicks}:{desired.Generation}:{RestoreSessionNonce}", runtime);
        Assert.DoesNotContain("=> $\"{desired.ProcessStartTicks}:{desired.Generation}\";", runtime);
    }

    [Fact]
    public void VendoredNativeRuntime_HashesMatchPinnedRelease()
    {
        var root = MappingScan.RepoRoot();
        var dir = Path.Combine(root, "native", "GivreTurboRuntime", "v0.4.0-skipper1.0.5-speedhack1.0.3");

        Assert.Equal("9b9c5f96dbf762ea7fb9d5af395d4f22622781da0b2b53ffa25c20c6c50ea75e",
            Sha256(Path.Combine(root, "native", "GivreTurboRuntime", "ours-bootstrap-retry", "DofusNativeBootstrap.dll")));
        Assert.Equal("22d2c6743c23d48f80bc0a9e114ba13bcfb07587470aedb188a9fdba8846ec1b",
            Sha256(Path.Combine(dir, "DofusNativeHost.dll")));
        Assert.Equal("a71084fdd4a7eaf0085944280423c31a9d352fa1566a71331f1212f745c124a3",
            Sha256(Path.Combine(dir, "DofusCombatAnimationSkipper.dll")));
        Assert.Equal("895a195a6551efb4a1085e500b745676ce8bf44a0967426de3e14abd5d1a0a24",
            Sha256(Path.Combine(dir, "DofusCombatSpeedHack.dll")));
    }

    [Fact]
    public void ActiveCombatSkipperBinary_MatchesRuntimePin_AndHasCombatRecovery()
    {
        var binary = Path.Combine(MappingScan.RepoRoot(), "native", "GivreTurboRuntime",
            "CombatSkipper", "DofusCombatAnimationSkipper.dll");
        var runtime = Source("GivreClone.App/Services/NativeTurboRuntime.cs");
        var skipper = Source("native/CombatSkipper/src/lib.rs");

        Assert.Contains($"internal const string SkipperSha256 = \"{Sha256(binary)}\"", runtime);
        Assert.Contains("is_combat_active(input_checker_active, service_active)", skipper);
        Assert.Contains("combat transition OFF; normal animation timing restored", skipper);
        Assert.Contains("refusing ambiguous hooks", skipper);
        Assert.Contains("MAX_TRACKED_ANIMATIONS", skipper);
        Assert.Contains("track_animation(&mut animations", skipper);
    }

    [Fact]
    public void BootstrapInjection_FailsClosedOnDeniedPermit()
    {
        var runtime = Source("GivreClone.App/Services/NativeTurboRuntime.cs");
        var denied = runtime.IndexOf("if (!MZP25InjectionPermit.TryRegister(pid, bootstrapTarget))", System.StringComparison.Ordinal);
        var throwAt = runtime.IndexOf("throw new UnauthorizedAccessException(\"permis d'injection native refusé\")", System.StringComparison.Ordinal);
        var injection = runtime.IndexOf("GivreCore.Injection.DllInjector.Inject(pid, bootstrapTarget)", System.StringComparison.Ordinal);
        Assert.True(denied >= 0 && throwAt > denied && injection > throwAt);
        Assert.Contains("catch (UnauthorizedAccessException) { throw; }", runtime);
    }

    [Fact]
    public void ActiveTacticalBinary_MatchesRuntimePin_AndRequiresAttributedClassAndSignatures()
    {
        var binary = Path.Combine(MappingScan.RepoRoot(), "native", "GivreTurboRuntime",
            "TacticalAutostart", "DofusNativeTactical.dll");
        var runtime = Source("GivreClone.App/Services/NativeTurboRuntime.cs");
        var tactical = Source("native/dofus3-native-host/mods/native-tactical/src/lib.rs");

        Assert.Contains($"internal const string TacticalSha256 = \"{Sha256(binary)}\"", runtime);
        Assert.Contains("enabled: true", tactical);
        Assert.Contains("GetForegroundWindow()", tactical);
        Assert.Contains("GetWindowThreadProcessId", tactical);
        Assert.Contains("required(Some(runtime.class(c\"Core.dll\", c\"Core.Rendering.Sprites.Interactive\", c\"TacticalDebugCell\")))", tactical);
        Assert.Contains("runtime.instance_method(tactical_cell_class, c\"qxl\", &[c\"System.Int32\"])", tactical);
        Assert.Contains("resolve_tactical_apply_kind(runtime, tactical_cell_class)?", tactical);
    }

    [Fact]
    public void ActiveItemOverlay_HashPinned_AndDoesNotJournalRawUiText()
    {
        var binary = Path.Combine(MappingScan.RepoRoot(), "native", "GivreTurboRuntime",
            "ItemIdOverlay", "GivreItemIdOverlay.dll");
        var runtime = Source("GivreClone.App/Services/NativeTurboRuntime.cs");
        var overlay = Source("native/ItemIdOverlay/src/lib.rs");

        Assert.Contains($"internal const string OverlaySha256 = \"{Sha256(binary)}\"", runtime);
        Assert.Contains("table::with_format", overlay);
        Assert.Contains("settings::Settings::load", overlay);
        Assert.DoesNotContain("texte vu {text:?}", overlay);
        Assert.DoesNotContain("st.sampled", overlay);
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
