// Les DEUX sources de production Bank/Inventory sont liées au projet de tests.
// Seules leurs dépendances (transport, licence, carte, attente réseau) sont simulées ici.
// Aucun client, socket, compte, pipe ni instance de l'application n'est créé.
using System.Collections.Concurrent;
using GivreClone.Protocol;
using GivreCore.Protobuf;

namespace GivreClone.App.Services
{
    public sealed record DecodedPacket(uint Pid, bool ClientToServer, string Code, string? Name,
        IReadOnlyList<ProtoField> Payload, byte[] RawFrame);

    internal static class LicenseService
    {
        internal enum Perm { Gather }
        private static readonly AsyncLocal<System.Runtime.CompilerServices.StrongBox<bool>?> GatherPermission = new();
        internal static bool GatherAllowed
        {
            get => GatherPermission.Value?.Value ?? true;
            set
            {
                if (GatherPermission.Value is { } permission) permission.Value = value;
                else GatherPermission.Value = new(value);
            }
        }
        internal static bool HasPermission(Perm _) => GatherAllowed;
    }

    internal sealed class AppConfig
    {
        internal static AppConfig Current { get; } = new();
        internal string BankDeposit => "all";
    }

    public sealed partial class LaunchService
    {
        public bool ItemCanBank(uint pid, int uid) => true;
        public sealed class FakeRelay
        {
            public bool Accept { get; set; } = true;
            public int Sends { get; private set; }
            public Action? OnSend { get; set; }
            public bool Inject(uint pid, byte[] packet) { Sends++; OnSend?.Invoke(); return Accept; }
        }

        public FakeRelay Relay { get; } = new();
        public List<DecodedPacket> Responses { get; } = new();
        public Action? BeforeWaitReturn { get; set; }
        public long Map { get; set; } = 123;
        private FakeRelay RelayForInject(bool capExempt = false) => Relay;
        public string ItemDisplayName(long gid) => gid.ToString();
        public List<string[]> RequestedNames { get; } = new();
        public long? CurrentMap(uint _) => Map;
        private void ShowInjected(uint pid, byte[] packet) { }
        private static readonly ProtocolCatalog Catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
        private int? FieldOf(string message, string field) => Catalog.FieldNumber(message, field);
        private int FNum(string message, string field) => FieldOf(message, field) ?? -1;
        private readonly ConcurrentDictionary<uint, ConcurrentDictionary<int, int>> _jobLevels = new();
        private readonly ConcurrentDictionary<uint, ConcurrentDictionary<int, JobProgressSnapshot>> _jobProgress = new();
        private readonly ConcurrentDictionary<uint, IReadOnlyList<ProtoField>> _lastNpcDialog = new();
        private readonly ConcurrentDictionary<uint, long> _bankKamas = new();
        private readonly ConcurrentDictionary<uint, bool> _chestSession = new();
        public long BankKamas(uint pid) => _bankKamas.GetValueOrDefault(pid, -1);
        public long Wallet { get; set; } = 1000;
        public long Kamas(uint _) => Wallet;
        private int? FirstNpcReplyId(IReadOnlyList<ProtoField> _) => 1;
        private IReadOnlyList<int> AllNpcReplyIds(IReadOnlyList<ProtoField> _) => new[] { 1 };
        private List<InventoryStore.Item> BagStacks(uint pid, long gid) => Inventory.Items(pid)
            .Where(i => i.Gid == gid && i.Position == 63 && i.Qty > 0).ToList();
        public event Action<DecodedPacket>? PacketDecoded;
        // Poids/pods reel du sac : la garde de depot exige une BAISSE effective du poids (sinon le depot n'est pas confirme).
        private readonly ConcurrentDictionary<uint, (int Cur, int Max)> _lastPods = new();
        public event Action<uint, int, int>? PodsUpdated;
        public void SetPods(uint pid, int cur, int max)
        {
            _lastPods[pid] = (cur, max);
            PodsUpdated?.Invoke(pid, cur, max);
        }
        public void Emit(DecodedPacket packet) { NoteBankSession(packet); PacketDecoded?.Invoke(packet); }
        public void OpenBank(uint pid, long kamas = 100) { _storageOpenMap[pid] = Map; _bankKamas[pid] = kamas; }
        public void MarkChest(uint pid) => _chestSession[pid] = true;
        public IReadOnlyList<InventoryReader.ItemEffect> ItemEffects(uint pid, int uid) => Array.Empty<InventoryReader.ItemEffect>();
        public IReadOnlyList<int> EquipPositions(long gid) => gid == 14964 ? new[] { 28 } : new[] { 0, 2, 5 };
        public bool IsInFight(uint _) => false;
        public bool UseItemUid(uint pid, int uid) => Relay.Inject(pid, new byte[] { 1 });
        public bool SendObjectSetPosition(uint pid, int uid, int position) => Relay.Inject(pid, new byte[] { 1 });

        public Task<DecodedPacket?> SendAndWaitForCodeAsync(uint pid, Func<bool> send, string[] names,
            int timeoutMs, CancellationToken ct = default) => SendAndWaitForCodeAsync(pid, send, names, timeoutMs, null, ct);

        public Task<DecodedPacket?> SendAndWaitForCodeAsync(uint pid, Func<bool> send, string[] names,
            int timeoutMs, Func<DecodedPacket, bool>? accept, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested) return Task.FromResult<DecodedPacket?>(null);
            RequestedNames.Add(names);
            if (!send()) return Task.FromResult<DecodedPacket?>(null);
            var response = Responses.FirstOrDefault(p => p.Pid == pid && !p.ClientToServer &&
                names.Contains(p.Name) && (accept?.Invoke(p) ?? true));
            if (response is not null) { NoteBankSession(response); PacketDecoded?.Invoke(response); }
            BeforeWaitReturn?.Invoke();
            return Task.FromResult(response);
        }
    }
}

namespace GivreClone.App.Services.Transport { } // namespace de transport réel, inutilisé par les sources liées
