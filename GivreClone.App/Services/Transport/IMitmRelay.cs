using System;

namespace GivreClone.App.Services.Transport;

/// <summary>Ce que <see cref="MitmTransport"/> attend d'un relais MITM : démarrage, injection vers le serveur, tap des octets et des fermetures.</summary>
public interface IMitmRelay
{
    void Start();
    bool Inject(uint pid, byte[] framedToServer);
    Action<uint, bool, byte[]>? OnData { get; set; }
    Action<uint, bool, bool>? OnClosed { get; set; }
}
