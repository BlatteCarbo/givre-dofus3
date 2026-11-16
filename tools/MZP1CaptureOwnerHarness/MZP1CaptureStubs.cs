// Harness-only substitutes: never create a socket or inject anything.
namespace GivreCore.Mitm
{
    public sealed class TcpProxyServer
    {
        public int Started { get; private set; }
        public int Stopped { get; private set; }
        public void Start() => Started++;
        public void Stop() => Stopped++;
    }
}

namespace GivreCore.Injection
{
    public static class DllInjector
    {
        public static int Invocations { get; private set; }
        public static bool Inject(int pid, string hookPath)
        {
            Invocations++;
            throw new InvalidOperationException("Injection forbidden in test harness.");
        }
    }
}
