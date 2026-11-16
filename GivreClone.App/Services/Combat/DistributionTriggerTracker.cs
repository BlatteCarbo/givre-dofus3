using System.Collections.Generic;
using System.Linq;
using GivreClone.Protocol;

namespace GivreClone.App.Services.Combat;

/// <summary>Observed Distribution, Resonance and Sram reveal instances; no activity is inferred from cast requests.</summary>
public sealed class DistributionTriggerTracker
{
    private readonly Dictionary<(long Actor, int Instance), RawTriggeredSubcastObservation> _instances = new();
    private readonly Dictionary<(long Actor, int Instance), RawTimedDamageObservation> _timed = new();

    public void ObserveTimed(RawTimedDamageObservation buff, int round)
    {
        lock (_instances)
        {
            if (buff.ActorId is not long actor || buff.NetworkInstanceId is not int instance || instance < 0) return;
            _timed.Remove((actor, instance));
            bool known = buff.SourceSpellId == 14594 && buff.ActionId == 99 && buff.Trigger == "TE"
                    && buff.SourceGrade is >= 1 and <= 3
                || buff.SourceSpellId == 29098 && buff.ActionId is 96 or 2998 && buff.Trigger == "TB"
                    && buff.SourceGrade is >= 1 and <= 4;
            if (!known || buff.SourceId is null
                || buff.Value is not >= 0 || buff.Start is not { Round: -1, Order: -1 }
                || buff.End is not { Round: int end, Order: null } || end <= round) return;
            _timed[(actor, instance)] = buff;
        }
    }

    public IReadOnlyList<RawTimedDamageObservation> ActiveTimed(long source, int round)
    {
        lock (_instances)
        {
            foreach (var key in _timed.Where(p => p.Value.End!.Value.Round <= round).Select(p => p.Key).ToArray())
                _timed.Remove(key);
            return _timed.Values.Where(p => p.SourceId == source).ToArray();
        }
    }

    public void Observe(RawTriggeredSubcastObservation buff, int round)
    {
        lock (_instances)
        {
            if (buff.ActorId is not long actor || buff.NetworkInstanceId is not int instance || instance < 0) return;
            _instances.Remove((actor, instance));
            if (buff.SourceSpellId == 21978 && buff.SubSpellId == 21979 && buff.SubSpellGrade == 1
                && buff.ActionId == 792 && buff.Trigger == "IOFF" && buff.SourceId == actor
                && buff.Start is { Round: -1, Order: -1 } && buff.End is { Round: -1, Order: null })
            {
                _instances[(actor, instance)] = buff;
                return;
            }
            // Only the captured immediate-start, round-only expiry contract is admitted.
            bool knownSpell = buff.SourceSpellId == 14577 && buff.SubSpellId == 14578
                    && buff.SubSpellGrade is >= 1 and <= 3
                || buff.SourceSpellId == 14611 && buff.SubSpellId == 31020 && buff.SubSpellGrade == 1;
            if (!knownSpell || buff.ActionId != 1160 || buff.SourceId is null
                || buff.TriggerGroupId is null || buff.Trigger is not ("D" or "XD")
                || buff.Start is not { Round: -1, Order: -1 }
                || buff.End is not { Round: int end, Order: null } || end <= round) return;
            _instances[(actor, instance)] = buff;
        }
    }

    public void Remove(RawStateRemoveObservation removal)
    {
        lock (_instances)
            if (removal.Actor is long actor && removal.Instance is int instance)
            {
                _instances.Remove((actor, instance));
                _timed.Remove((actor, instance));
            }
    }

    public IReadOnlyList<RawTriggeredSubcastObservation> Active(long source, int round)
    {
        lock (_instances)
        {
            foreach (var key in _instances.Where(pair => pair.Value.End!.Value.Round is >= 0 and var end && end <= round)
                .Select(pair => pair.Key).ToArray()) _instances.Remove(key);
            return _instances.Values.Where(buff => buff.SourceId == source)
                .GroupBy(buff => (buff.ActorId, buff.SourceId, buff.SourceSpellId, buff.TriggerGroupId, buff.SubSpellGrade))
                .Select(group => group.First()).ToArray();
        }
    }

    public void Clear()
    {
        lock (_instances) { _instances.Clear(); _timed.Clear(); }
    }
}
