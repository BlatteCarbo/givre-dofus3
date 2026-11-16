using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

// Apparence du personnage joué : lue dans la liste des personnages (à la connexion) puis tenue à jour par ContextRefreshEntityLookEvent (changement d'équipement / de skin).
public sealed partial class LaunchService
{
    private readonly ConcurrentDictionary<uint, EntityLookInfo> _selfLook = new();
    private readonly ConcurrentDictionary<uint, IReadOnlyDictionary<long, EntityLookInfo>> _listLooks = new();
    private bool _lookTracking;

    /// <summary>Position sans valeur pour les stats et l'affichage : carte d'INSTANCE de combat (le perso est en combat) ou carte inconnue du monde annoncée en (0, 0) (havre-sac, instance). Ne pas l'enregistrer évite que (0,0) fausse la heatmap.</summary>
    public bool IsMeaninglessPosition(uint pid, long mapId, int? x, int? y)
        => IsInFight(pid) || (x == 0 && y == 0 && !WorldGraphKnows((int)mapId));
    private readonly ConcurrentDictionary<uint, IReadOnlyDictionary<int, int>> _selfChars = new();

    /// <summary>Caractéristiques connues du perso de ce bot (id -> total), lues dans CharacterCharacteristicsEvent ; vide tant qu'aucune fiche n'a été vue.</summary>
    public IReadOnlyDictionary<int, int> SelfCharacteristics(uint pid) => EngineControlled(pid) is not null
        ? SelfStats(pid)?.Characteristics ?? System.Collections.Immutable.ImmutableDictionary<int, int>.Empty
        : _selfChars.TryGetValue(pid, out var m) ? m : new Dictionary<int, int>();

    /// <summary>Dés de base d'un sort (min, max, élément) au niveau appris par ce bot ; null si le sort n'inflige pas de dégâts élémentaires.</summary>
    public SpellDamageInfo? SpellDamageOf(uint pid, int spellId) => _gameNames.GetSpellDamage(spellId, LearnedSpellLevel(pid, spellId));
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<int, IReadOnlyList<InventoryReader.ItemEffect>>> _setBonus = new();

    /// <summary>Bonus de panoplie actuellement actifs (lignes d'infobulle « 20 Vitalité » et malus), tels que le serveur les annonce (SetUpdateEvent).</summary>
    public IReadOnlyList<(string Text, bool Negative)> SetBonusLines(uint pid)
    {
        var lines = new List<(string, bool)>();
        if (!_setBonus.TryGetValue(pid, out var sets)) return lines;
        foreach (var fx in sets.Values)
            foreach (var e in fx)
            {
                var def = _gameNames.Effect(e.ActionId);
                if (def is { ShowOnItem: false }) continue;
                string text = ItemEffectText.Format(e.ActionId, _gameNames.EffectTemplate(e.ActionId), ItemEffectText.Params(e.Value, e.Min, e.Max, e.Const, e.Text, null));
                lines.Add((text, ItemEffectText.IsNegative(def?.BonusType ?? 0, e.Value, text)));
            }
        return lines;
    }

    /// <summary>Le look du perso joué par ce bot vient de changer (équipement, skin) ou d'être connu.</summary>
    public event Action<uint, EntityLookInfo>? SelfLookChanged;

    /// <summary>Dernier look connu du perso de ce bot (null tant qu'aucun paquet de look n'a été vu).</summary>
    public EntityLookInfo? SelfLook(uint pid) => _selfLook.TryGetValue(pid, out var l) ? l : null;

    /// <summary>Look original du perso de ce bot issu de la sélection (CharacterListEvent).</summary>
    public EntityLookInfo? BaseCharacterLook(uint pid)
    {
        long id = CharacterId(pid);
        return id != 0 && _listLooks.TryGetValue(pid, out var looks) && looks.TryGetValue(id, out var baseLook) ? baseLook : null;
    }

    /// <summary>Démarre l'écoute des paquets de look (idempotent). Aucun paquet n'est émis : lecture seule.</summary>
    public void StartLookTracking()
    {
        StartAntiAfk();
        if (_lookTracking) return;
        _lookTracking = true;
        PacketDecoded += OnLookPacket;
        PacketDecoded += OnChatPacket;   // journal de discussion (tous canaux)
        PacketDecoded += OnHdvLearnPacket;   // hôteliers d'HDV appris à l'usage
    }

    private int? LookField(string message, string field) => _catalog?.FieldNumber(message, field);

    private void OnLookPacket(DecodedPacket p)
    {
        if (p.ClientToServer || p.Name is null) return;
        try
        {
            if (p.Name == "CharacterListEvent")
            {
                _setBonus.TryRemove(p.Pid, out _);   // nouvelle session de personnages : les bonus de panoplie sont renvoyés à l'entrée en jeu
                var looks = EntityLookReader.ReadCharacterList(p.Payload, LookField);
                if (looks.Count == 0) return;
                _listLooks[p.Pid] = looks;
                PublishListLook(p.Pid);
            }
            else if (p.Name == "SetUpdateEvent" && InventoryReader.ReadSetUpdate(p.Payload) is { } su)
            {
                var d = _setBonus.GetOrAdd(p.Pid, _ => new ConcurrentDictionary<int, IReadOnlyList<InventoryReader.ItemEffect>>());
                if (su.Effects.Count == 0) d.TryRemove(su.SetId, out _); else d[su.SetId] = su.Effects;
            }
            else if (p.Name == "CharacterCharacteristicsEvent" && FightStatsReader.ReadSelfCharacteristicMap(p.Payload) is { } chars)
            {
                _selfChars[p.Pid] = chars;
                if (FightStatsReader.ReadSelfCharacteristicBases(p.Payload) is { } bases)
                    NoteStatSheet(p.Pid, bases, chars.TryGetValue(3, out int remaining) ? remaining : -1);
            }
            else if (p.Name == "CharacterSelectionEvent") { ResetStatSheet(p.Pid); _selfChars.TryRemove(p.Pid, out _); PublishListLook(p.Pid); }   // le perso choisi est connu à cet instant
            else if (p.Name is "MapComplementaryInformationEvent" or "GameRolePlayShowActorsEvent"
                && LookField(p.Name, "actors") is int actorsField
                && EntityLookReader.ReadMapSelf(p.Payload, actorsField, CharacterId(p.Pid), LookField) is { } mapLook)
            {
                _selfLook[p.Pid] = mapLook;
                SelfLookChanged?.Invoke(p.Pid, mapLook);
            }
            else if (p.Name == "ContextRefreshEntityLookEvent" && EntityLookReader.ReadRefresh(p.Payload, LookField) is { } r && r.EntityId != 0 && r.EntityId == CharacterId(p.Pid))
            {
                _selfLook[p.Pid] = r.Look;
                SelfLookChanged?.Invoke(p.Pid, r.Look);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or KeyNotFoundException) { /* look illisible : on garde l'ancien */ }
    }

    private void ForgetCharacterSheet(uint pid, bool preserveListLooks = false)
    {
        ResetStatSheet(pid);
        _selfChars.TryRemove(pid, out _);
        _setBonus.TryRemove(pid, out _);
        _selfLook.TryRemove(pid, out _);
        if (!preserveListLooks) _listLooks.TryRemove(pid, out _);
    }

    private void PublishListLook(uint pid)
    {
        long id = CharacterId(pid);
        if (id != 0 && _listLooks.TryGetValue(pid, out var looks) && looks.TryGetValue(id, out var look) && !(_selfLook.TryGetValue(pid, out var cur) && cur == look))
        {
            _selfLook[pid] = look;
            if (look.BonesId == 0) GivreLauncher.Core.Transport.TransportLog.Line($"[look] squelette absent du paquet : skins=[{string.Join(",", look.Skins)}] couleurs=[{string.Join(",", look.Colors.Select(c => c.Index + "=" + c.Rgb))}] échelles=[{string.Join(",", look.Scales)}] -> rendu avec le squelette 1 par défaut");
            SelfLookChanged?.Invoke(pid, look);
        }
    }
}
