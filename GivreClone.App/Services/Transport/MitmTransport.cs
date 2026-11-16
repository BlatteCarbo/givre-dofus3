using System;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services.Transport;

/// <summary>
/// Mode MITM : enveloppe le relais <see cref="HandshakeRelay"/> (GivreLauncher.Core) sans en changer le comportement.
/// Le hook natif GivreKama redirige les connect() de Dofus.exe vers ce relais, qui splice vers le serveur réel.
/// </summary>
public sealed class MitmTransport : IGameTransport
{
    private readonly IMitmRelay _relay;

    public MitmTransport(HandshakeRelay relay) => _relay = new HandshakeRelayAdapter(relay);
    /// <summary>Relais de l'édition locale (destination réelle, pas de contrainte « boucle locale »).</summary>
    public MitmTransport(IMitmRelay relay) => _relay = relay;

    private sealed class HandshakeRelayAdapter(HandshakeRelay r) : IMitmRelay
    {
        public void Start() => r.Start();
        public bool Inject(uint pid, byte[] framedToServer) => r.Inject(pid, framedToServer);
        public Action<uint, bool, byte[]>? OnData { get => r.OnData; set => r.OnData = value; }
        public Action<uint, bool, bool>? OnClosed { get => r.OnClosed; set => r.OnClosed = value; }
    }

    public string Name => "mitm";

    public bool Inject(uint pid, byte[] framedToServer) => _relay.Inject(pid, framedToServer);

    public Action<uint, bool, byte[]>? OnData
    {
        get => _relay.OnData;
        set => _relay.OnData = value;
    }

    public Action<uint, bool, bool>? OnClosed
    {
        get => _relay.OnClosed;
        set => _relay.OnClosed = value;
    }

    public void Start() => _relay.Start();
}
