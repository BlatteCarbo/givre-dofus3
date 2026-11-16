using System;
using System.Text;

namespace GivreClone.App.Services.Relay;

/// <summary>Nom d'hôte (SNI) d'un ClientHello TLS, ou null. Sert aux connexions HTTPS du client Dofus, qu'on ne voit que par IP.</summary>
public static class TlsSni
{
    public static string? Parse(ReadOnlySpan<byte> b)
    {
        try
        {
            // enregistrement [0x16][ver 2][len 2] ; poignée [0x01][len 3][ver 2][random 32][session][suites][compression][extensions]
            if (b.Length < 5 + 4 + 2 + 32 + 1 || b[0] != 0x16 || b[5] != 0x01) return null;
            int i = 5 + 4 + 2 + 32;
            i += 1 + b[i];                                   // session id
            i += 2 + ((b[i] << 8) | b[i + 1]);               // suites
            i += 1 + b[i];                                   // compression
            int end = i + 2 + ((b[i] << 8) | b[i + 1]);
            i += 2;
            end = Math.Min(end, b.Length);
            while (i + 4 <= end)
            {
                int type = (b[i] << 8) | b[i + 1], len = (b[i + 2] << 8) | b[i + 3];
                i += 4;
                if (type == 0x0000 && i + 5 <= end)
                {
                    // server_name : [liste 2][type 1 = host][longueur 2][nom]
                    int nlen = (b[i + 3] << 8) | b[i + 4];
                    if (b[i + 2] != 0 || i + 5 + nlen > end) return null;
                    string name = Encoding.ASCII.GetString(b.Slice(i + 5, nlen));
                    return name.Length > 0 && Uri.CheckHostName(name) == UriHostNameType.Dns ? name : null;
                }
                i += len;
            }
        }
        catch (IndexOutOfRangeException) { /* ClientHello tronqué */ }
        return null;
    }
}
