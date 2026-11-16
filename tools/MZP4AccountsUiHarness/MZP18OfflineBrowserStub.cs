using System;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services;

// The harness is exclusively HEADLESS/offline; no browser or network is ever
// permitted. Production WebAuth implementation is tested in the MZP18 harness.
public static class MZP18IsolatedWebAuthRunner
{
    public enum State { Succeeded, Cancelled, Failed, Unavailable }
    public sealed record Result(State State, string Reason);
    public static Task<Result> RunInteractiveAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException("No live browser in offline UI harness");
}
