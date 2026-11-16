using System;
using System.Collections.Concurrent;
using GivreClone.App.Services.Chat;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

// Journal de discussion par bot (tous canaux + nos MP envoyés), lu dans les paquets : aucune émission.
public sealed partial class LaunchService
{
    private readonly ConcurrentDictionary<uint, ChatBuffer> _chat = new();

    /// <summary>Nouveau message de discussion pour ce bot.</summary>
    public event Action<uint, ChatEntry>? ChatReceived;

    public ChatBuffer ChatOf(uint pid) => _chat.GetOrAdd(pid, _ => new ChatBuffer());

    private void OnChatPacket(DecodedPacket p)
    {
        if (p.ClientToServer || p.Name is null) return;
        try
        {
            if (p.Name == "ChatChannelMessageEvent")
            {
                int chF = LookField("ChatChannelMessageEvent", "channel") ?? -1, snF = LookField("ChatChannelMessageEvent", "sender_name") ?? -1, ctF = LookField("ChatChannelMessageEvent", "content") ?? -1;
                string content = ctF < 0 ? "" : PayloadReader.Str(p.Payload, ctF) ?? "";
                if (content.Length == 0) return;
                string sender = snF < 0 ? "" : PayloadReader.Str(p.Payload, snF) ?? "";
                int channel = chF < 0 ? -1 : (int)PayloadReader.Int(p.Payload, chF, 0);
                ChatReceived?.Invoke(p.Pid, ChatOf(p.Pid).Add(channel, sender, content));
            }
            else if (p.Name == "ChatPrivateCopyMessageEvent")
            {
                int tF = LookField("ChatPrivateCopyMessageEvent", "target_name") ?? -1, cF = LookField("ChatPrivateCopyMessageEvent", "content") ?? -1;
                string content = cF < 0 ? "" : PayloadReader.Str(p.Payload, cF) ?? "";
                if (content.Length == 0) return;
                ChatReceived?.Invoke(p.Pid, ChatOf(p.Pid).Add(ChatBuffer.PrivateChannel, tF < 0 ? "" : PayloadReader.Str(p.Payload, tF) ?? "", content, outgoing: true));
            }
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException) { /* message illisible : ignoré */ }
    }
}
