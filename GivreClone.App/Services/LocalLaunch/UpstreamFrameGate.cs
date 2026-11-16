using System;
using System.Collections.Generic;
using GivreCore.Protobuf;

namespace GivreClone.App.Services.LocalLaunch;

/// <summary>
/// Filtre de trames CLIENT -> SERVEUR (cadrage varint) : laisse passer tout octet inchangé, sauf les trames que le décideur
/// juge déjà accusées PAR LE BOT (Turbo : le client visible ne doit pas les accuser une 2e fois). Fail-open : au moindre doute
/// (cadrage invalide, tampon anormal, exception du décideur) les octets sont transmis tels quels et le filtre se désarme.
/// </summary>
public sealed class UpstreamFrameGate
{
    private List<byte> _pending = new();
    private bool _disarmed;
    public long Dropped { get; private set; }

    /// <summary>Retourne les octets à transmettre au serveur pour ce segment (éventuellement vide si une trame est incomplète).</summary>
    public byte[] Process(uint pid, byte[] chunk, Func<uint, byte[], bool> shouldDrop)
    {
        if (_disarmed) return chunk;
        try
        {
            _pending.AddRange(chunk);
            var frames = RawProtobuf.ConsumeFrames(_pending.ToArray(), out var rest);
            _pending = new List<byte>(rest);
            if (frames.Count == 0) return Array.Empty<byte>();
            var output = new List<byte>(chunk.Length);
            foreach (var frame in frames)
            {
                bool drop;
                try { drop = shouldDrop(pid, frame); } catch { drop = false; }
                if (drop) { Dropped++; continue; }
                output.AddRange(RawProtobuf.EncodeVarint((ulong)frame.Length));
                output.AddRange(frame);
            }
            return output.ToArray();
        }
        catch
        {
            _disarmed = true;
            var all = _pending.ToArray();
            _pending.Clear();
            return all;
        }
    }
}
