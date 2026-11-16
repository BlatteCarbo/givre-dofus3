using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

// Même sérialisation que l'app (source-generated, AOT) : AppJsonContext n'est pas lié ici (il traîne tous les stores).
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(PlanningFile))]
internal sealed partial class PlanningTestJsonContext : JsonSerializerContext { }

/// <summary>Assignation plage -> script du planning (bug « à la reconnexion, les plages repassent sur "Script de la plage" ») :
/// l'assignation ne change QUE sur un choix explicite, survit au renommage / à la sérialisation, et un script supprimé
/// laisse un état « introuvable » (rien lancé) au lieu d'effacer l'assignation ou de lancer un autre trajet.</summary>
public class PlanningAssignmentTests
{
    // Planning du rapport utilisateur : 3 scripts en bibliothèque, 3 plages du samedi, chacune avec SON script.
    private static CharPlanning Persoun()
    {
        var cp = new CharPlanning { Enabled = true, AutoConnect = true, Script = "-- repli figé" };
        foreach (var n in new[] { "Potion-Souvenir", "Otomai", "100% PDM 100 GIVRE" })
            cp.Scripts.Add(new PlanningScript { Name = n, Body = "-- corps " + n });
        cp.Slots.Add(new PlanningSlot { Name = "Fishing", Script = "100% PDM 100 GIVRE", Days = 32, StartMin = 540, EndMin = 660 });
        cp.Slots.Add(new PlanningSlot { Name = "Otomai", Script = "Otomai", Days = 32, StartMin = 390, EndMin = 540 });
        cp.Slots.Add(new PlanningSlot { Name = "Souvenir", Script = "Potion-Souvenir", Days = 32, StartMin = 660, EndMin = 900 });
        return cp;
    }

    private static string[] Assigned(CharPlanning cp) => cp.Slots.Select(s => s.Script).ToArray();

    [Fact]
    public void Each_slot_resolves_its_own_assigned_script()
    {
        var cp = Persoun();
        for (int i = 0; i < cp.Slots.Count; i++)
        {
            var pick = PlanningEval.ResolveSlotScript(cp, i, editorScript: "-- éditeur");
            Assert.Equal(SlotScriptSource.Slot, pick.Source);
            Assert.Equal(cp.Slots[i].Script, pick.Name);
            Assert.Equal("-- corps " + cp.Slots[i].Script, pick.Body);
        }
    }

    [Fact]
    public void Slot_already_active_after_a_reconnection_starts_its_assigned_script()
    {
        // Samedi 14:38 (capture du rapport) : la plage « Souvenir » (11:00-15:00) est EN COURS -> son script, pas le repli.
        var cp = Persoun();
        var now = new System.DateTime(2026, 9, 26, 14, 38, 0);
        Assert.Equal(System.DayOfWeek.Saturday, now.DayOfWeek);
        int slot = PlanningEval.ActiveSlotIndex(cp, now, "Persoun");
        Assert.Equal(2, slot);
        var pick = PlanningEval.ResolveSlotScript(cp, slot, null);
        Assert.Equal(SlotScriptSource.Slot, pick.Source);
        Assert.Equal("-- corps Potion-Souvenir", pick.Body);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Transient_empty_pick_never_clears_the_assignment(string? pick)
    {
        // Ce qu'écrit une ComboBox dont la liste des choix est reconstruite (Load d'un autre perso / "?") : ignoré.
        var cp = Persoun();
        foreach (var s in cp.Slots) Assert.False(PlanningEdit.AssignScript(s, pick));
        Assert.Equal(new[] { "100% PDM 100 GIVRE", "Otomai", "Potion-Souvenir" }, Assigned(cp));
    }

    [Fact]
    public void Explicit_pick_changes_the_assignment()
    {
        var cp = Persoun();
        Assert.True(PlanningEdit.AssignScript(cp.Slots[0], "Otomai"));
        Assert.False(PlanningEdit.AssignScript(cp.Slots[0], "Otomai"));   // même valeur : rien à sauver
        Assert.Equal("Otomai", cp.Slots[0].Script);
        Assert.Equal("-- corps Otomai", PlanningEval.SlotBody(cp, 0));
    }

    [Fact]
    public void Rename_follows_the_script_to_its_slots()
    {
        var cp = Persoun();
        var oto = cp.Scripts[1];
        Assert.Equal("Otomai2", PlanningEdit.RenameScript(cp, oto, "Otomai2"));
        Assert.Equal("Otomai2", oto.Name);
        Assert.Equal(new[] { "100% PDM 100 GIVRE", "Otomai2", "Potion-Souvenir" }, Assigned(cp));
        Assert.Equal(SlotScriptSource.Slot, PlanningEval.ResolveSlotScript(cp, 1, null).Source);
    }

    [Fact]
    public void Rename_to_empty_is_refused_and_trims_and_dedupes()
    {
        var cp = Persoun();
        var oto = cp.Scripts[1];
        Assert.Equal("Otomai", PlanningEdit.RenameScript(cp, oto, "   "));        // vide : refusé
        Assert.Equal("Otomai", oto.Name);
        Assert.Equal("Otomai", cp.Slots[1].Script);
        Assert.Equal("Oto", PlanningEdit.RenameScript(cp, oto, "  Oto "));          // espaces rognés
        Assert.Equal("Potion-Souvenir′", PlanningEdit.RenameScript(cp, oto, "Potion-Souvenir"));   // doublon suffixé
        Assert.Equal("Potion-Souvenir′", cp.Slots[1].Script);
        Assert.Equal("Potion-Souvenir", cp.Slots[2].Script);                         // l'autre plage n'a pas bougé
    }

    [Fact]
    public void Rename_of_an_unnamed_script_never_binds_unassigned_slots()
    {
        var cp = Persoun();
        cp.Slots.Add(new PlanningSlot { Name = "Libre", Script = "", Days = 1, StartMin = 0, EndMin = 60 });
        var unnamed = new PlanningScript { Name = "", Body = "-- x" };
        cp.Scripts.Add(unnamed);
        Assert.Equal("Nouveau", PlanningEdit.RenameScript(cp, unnamed, "Nouveau"));
        Assert.Equal("", cp.Slots[3].Script);
    }

    [Fact]
    public void Removed_script_leaves_a_missing_state_and_starts_nothing()
    {
        var cp = Persoun();
        Assert.True(PlanningEdit.RemoveScript(cp, cp.Scripts[1]));   // supprime « Otomai »
        Assert.Equal("Otomai", cp.Slots[1].Script);                   // assignation CONSERVÉE
        Assert.Equal("Otomai", PlanningEval.MissingScript(cp, cp.Slots[1]));
        Assert.Null(PlanningEval.MissingScript(cp, cp.Slots[0]));
        var pick = PlanningEval.ResolveSlotScript(cp, 1, editorScript: "-- éditeur");
        Assert.Equal(SlotScriptSource.Missing, pick.Source);           // ni cp.Script ni l'éditeur à la place
        Assert.Null(pick.Body);
        Assert.Equal("Otomai", pick.Name);

        // Rajouter un script de ce nom répare la plage.
        cp.Scripts.Add(new PlanningScript { Name = "Otomai", Body = "-- corps Otomai v2" });
        Assert.Null(PlanningEval.MissingScript(cp, cp.Slots[1]));
        Assert.Equal("-- corps Otomai v2", PlanningEval.ResolveSlotScript(cp, 1, null).Body);
    }

    [Fact]
    public void Unassigned_slot_falls_back_to_frozen_then_editor_then_nothing()
    {
        var cp = Persoun();
        cp.Slots[0].Script = "";
        var p = PlanningEval.ResolveSlotScript(cp, 0, "-- éditeur");
        Assert.Equal(SlotScriptSource.Fallback, p.Source);
        Assert.Equal("-- repli figé", p.Body);
        cp.Script = "";
        p = PlanningEval.ResolveSlotScript(cp, 0, "-- éditeur");
        Assert.Equal(SlotScriptSource.Editor, p.Source);
        Assert.Equal("-- éditeur", p.Body);
        p = PlanningEval.ResolveSlotScript(cp, 0, null);
        Assert.Equal(SlotScriptSource.None, p.Source);
        Assert.Null(p.Body);
        Assert.Equal(SlotScriptSource.None, PlanningEval.ResolveSlotScript(cp, -1, null).Source);   // hors plage
    }

    [Fact]
    public void Unique_script_name_suffixes_until_free()
    {
        var cp = Persoun();
        Assert.Equal("Libre", PlanningEdit.UniqueScriptName(cp, "Libre"));
        Assert.Equal("Otomai′", PlanningEdit.UniqueScriptName(cp, "Otomai"));
        Assert.Equal("Otomai", PlanningEdit.UniqueScriptName(cp, "Otomai", except: cp.Scripts[1]));   // lui-même
    }

    // ---- D2 : un nom GÉNÉRÉ ne reprend jamais une plage « script introuvable » ; un nom SAISI la rétablit (journalisé) ----

    private static string Fmt(int n) => "Script " + n;

    [Fact]
    public void Generated_default_name_skips_names_still_assigned_to_slots()
    {
        var cp = Persoun();
        cp.Scripts.Add(new PlanningScript { Name = "Script 4", Body = "-- 4" });
        cp.Slots[1].Script = "Script 5";                                     // orphelin : « Script 5 » supprimé de la bibliothèque
        Assert.Equal("Script 5", PlanningEval.MissingScript(cp, cp.Slots[1]));
        string nm = PlanningEdit.DefaultScriptName(cp, Fmt);                  // Count+1 = 5 -> pris par la plage -> 6
        Assert.Equal("Script 6", nm);
        Assert.True(PlanningEdit.IsNameFree(cp, nm));
        Assert.False(PlanningEdit.IsNameFree(cp, "Script 5"));
        Assert.False(PlanningEdit.IsNameFree(cp, "Script 4"));
        Assert.Equal("Script 5", PlanningEval.MissingScript(cp, cp.Slots[1]));   // la plage reste « introuvable »
    }

    [Fact]
    public void Default_name_with_a_broken_format_still_ends_free_everywhere()
    {
        var cp = Persoun();
        cp.Slots[0].Script = "Script";
        cp.Scripts.Add(new PlanningScript { Name = "Script′", Body = "-- x" });
        Assert.Equal("Script′′", PlanningEdit.DefaultScriptName(cp, _ => "Script"));
    }

    [Fact]
    public void Typed_name_relinks_orphaned_slots_and_reports_them()
    {
        var cp = Persoun();
        PlanningEdit.RemoveScript(cp, cp.Scripts[1]);                        // « Otomai » supprimé : plage introuvable
        var orphans = PlanningEdit.OrphanedSlots(cp, "Otomai");
        Assert.Single(orphans);
        Assert.Same(cp.Slots[1], orphans[0]);
        Assert.Empty(PlanningEdit.OrphanedSlots(cp, "Potion-Souvenir"));    // présent : pas orphelin
        Assert.Empty(PlanningEdit.OrphanedSlots(cp, "  "));
    }

    [Fact]
    public void Rename_onto_an_orphaned_name_relinks_and_reports()
    {
        var cp = Persoun();
        PlanningEdit.RemoveScript(cp, cp.Scripts[1]);                        // « Otomai » supprimé
        var extra = new PlanningScript { Name = "Script 4", Body = "-- nouveau trajet" };
        cp.Scripts.Add(extra);
        string kept = PlanningEdit.RenameScript(cp, extra, "Otomai", out var relinked);
        Assert.Equal("Otomai", kept);
        Assert.Single(relinked);
        Assert.Same(cp.Slots[1], relinked[0]);
        Assert.Equal("-- nouveau trajet", PlanningEval.ResolveSlotScript(cp, 1, null).Body);
        // Renommage ordinaire : rien de rétabli.
        PlanningEdit.RenameScript(cp, cp.Scripts[0], "Souvenir v2", out var none);
        Assert.Empty(none);
        Assert.Equal("Souvenir v2", cp.Slots[2].Script);
    }

    // ---- D3 : le MODÈLE d'exemple de l'éditeur n'est jamais le repli du planning ----

    private const string Template = "-- Script GivreClone (Lua / MoonSharp). Deux modeles au choix :\nfunction move()\nend\n";

    [Fact]
    public void Template_frozen_as_fallback_counts_as_empty()
    {
        var cp = Persoun();
        cp.Slots[0].Script = "";
        cp.Script = Template;                                                 // déjà sur disque (ancienne version)
        var p = PlanningEval.ResolveSlotScript(cp, 0, null, Template);
        Assert.Equal(SlotScriptSource.None, p.Source);
        Assert.Null(p.Body);
        cp.Script = Template.Replace("\n", "\r\n");                           // recopié en CRLF : toujours le modèle
        Assert.Equal(SlotScriptSource.None, PlanningEval.ResolveSlotScript(cp, 0, null, Template).Source);
        Assert.Equal(SlotScriptSource.None, PlanningEval.ResolveSlotScript(cp, 0, Template, Template).Source);   // éditeur = modèle
        Assert.Equal(SlotScriptSource.Editor, PlanningEval.ResolveSlotScript(cp, 0, "-- mon trajet", Template).Source);
        cp.Script = Template + "-- édité\n";                                  // modèle modifié par l'utilisateur : son script
        Assert.Equal(SlotScriptSource.Fallback, PlanningEval.ResolveSlotScript(cp, 0, null, Template).Source);
    }

    [Fact]
    public void Same_script_ignores_line_endings_and_edges_only()
    {
        Assert.True(PlanningEval.SameScript("a\nb\n", "a\r\nb"));
        Assert.False(PlanningEval.SameScript("a\nb", "a\nc"));
        Assert.False(PlanningEval.SameScript(null, null));
        Assert.False(PlanningEval.SameScript("", ""));
    }

    // ---- D4 : « Retirer » désassigne explicitement une plage ----

    [Fact]
    public void Clear_script_unassigns_and_falls_back_to_the_frozen_script()
    {
        var cp = Persoun();
        Assert.True(PlanningEdit.ClearScript(cp.Slots[0]));
        Assert.False(PlanningEdit.ClearScript(cp.Slots[0]));                  // déjà vide : rien à sauver
        Assert.Equal("", cp.Slots[0].Script);
        var p = PlanningEval.ResolveSlotScript(cp, 0, "-- éditeur", Template);
        Assert.Equal(SlotScriptSource.Fallback, p.Source);
        Assert.Equal("-- repli figé", p.Body);
        cp.Script = Template;                                                 // repli = modèle : jamais lancé
        Assert.Equal(SlotScriptSource.None, PlanningEval.ResolveSlotScript(cp, 0, null, Template).Source);
    }

    [Fact]
    public void Clear_script_repairs_a_missing_assignment()
    {
        var cp = Persoun();
        PlanningEdit.RemoveScript(cp, cp.Scripts[1]);
        Assert.Equal(SlotScriptSource.Missing, PlanningEval.ResolveSlotScript(cp, 1, null).Source);
        Assert.True(PlanningEdit.ClearScript(cp.Slots[1]));
        Assert.Null(PlanningEval.MissingScript(cp, cp.Slots[1]));
        Assert.Equal(SlotScriptSource.Fallback, PlanningEval.ResolveSlotScript(cp, 1, null).Source);
    }

    [Fact]
    public void Assignments_survive_a_save_load_round_trip()
    {
        var file = new PlanningFile();
        file.Plannings["Persoun"] = Persoun();
        file.Plannings["?"] = new CharPlanning();   // clé de repli (bot pas encore identifié) : sans effet sur Persoun
        string json = JsonSerializer.Serialize(file, typeof(PlanningFile), PlanningTestJsonContext.Default);
        var back = (PlanningFile?)JsonSerializer.Deserialize(json, typeof(PlanningFile), PlanningTestJsonContext.Default);
        Assert.NotNull(back);
        var cp = back!.Plannings["Persoun"];
        Assert.Equal(new[] { "100% PDM 100 GIVRE", "Otomai", "Potion-Souvenir" }, Assigned(cp));
        Assert.Equal(new[] { "Potion-Souvenir", "Otomai", "100% PDM 100 GIVRE" }, cp.Scripts.Select(s => s.Name).ToArray());
        for (int i = 0; i < cp.Slots.Count; i++) Assert.Equal(SlotScriptSource.Slot, PlanningEval.ResolveSlotScript(cp, i, null).Source);
    }

    [Fact]
    public void Reconnect_like_sequence_of_transient_writes_keeps_the_saved_file_identical()
    {
        // Rejoue, côté modèle, ce que produisait l'éditeur à la reconnexion (Load(null) puis Load(Persoun)) : des null
        // écrits par les ComboBox des anciennes lignes. Le fichier sauvé ensuite doit être STRICTEMENT identique.
        var file = new PlanningFile();
        file.Plannings["Persoun"] = Persoun();
        string before = JsonSerializer.Serialize(file, typeof(PlanningFile), PlanningTestJsonContext.Default);
        foreach (var s in file.Plannings["Persoun"].Slots) { PlanningEdit.AssignScript(s, null); PlanningEdit.AssignScript(s, ""); }
        string after = JsonSerializer.Serialize(file, typeof(PlanningFile), PlanningTestJsonContext.Default);
        Assert.Equal(before, after);
    }
}
