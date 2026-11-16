// Test-only model of TcpProxyServer: no listening socket and no process.
namespace GivreCore.Mitm
{
    public sealed partial class TcpProxyServer
    {
        public event Action<CapturedMessage>? MessageCaptured;
        public void FeedFixture(CapturedMessage capture) => MessageCaptured?.Invoke(capture);
    }
}

namespace GivreClone.Engine.Accounts
{
    // Real AccountManager requires private DPAPI/account-store I/O. Do not use
    // it for a test which does not touch identity/account operations.
    public sealed class AccountManager;
}
