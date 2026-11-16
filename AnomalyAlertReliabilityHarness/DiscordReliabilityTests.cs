using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services;

internal static class DiscordReliabilityTests
{
    internal static async Task RateLimitRetry()
    {
        var stub = new ScriptedHandler();
        stub.Push(() => RateLimited("1.25"));
        stub.Push(() => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var client = new HttpClient(stub);
        var delays = new List<TimeSpan>();
        var warnings = new List<string>();
        bool ok = await WebhookService.PostWithClientAsync(
            "https://discord.com/api/webhooks/id/PRIVATE_TOKEN", "alert", client,
            warnings.Add, d => { delays.Add(d); return Task.CompletedTask; });
        Assert(ok && stub.RequestCount == 2, "A 429 followed by 204 should succeed after one retry");
        Assert(delays.SequenceEqual(new[] { TimeSpan.FromMilliseconds(1250) }), "Retry-After delta respected");
        Assert(warnings.Count == 0, "Successful retry should not emit a false failure");

        var tooLong = new ScriptedHandler();
        tooLong.Push(() => RateLimited("60"));
        using var second = new HttpClient(tooLong);
        delays.Clear();
        ok = await WebhookService.PostWithClientAsync(
            "https://discord.com/api/webhooks/id/PRIVATE_TOKEN", "alert", second,
            warnings.Add, d => { delays.Add(d); return Task.CompletedTask; });
        Assert(!ok && tooLong.RequestCount == 1 && delays.Count == 0,
            "Long server Retry-After must not be ignored or retried immediately");
        Assert(warnings.All(w => !w.Contains("PRIVATE_TOKEN", StringComparison.Ordinal)),
            "Sensitive URL cannot appear in error messages");

        var repeated = new ScriptedHandler();
        repeated.Push(() => RateLimited("0"));
        repeated.Push(() => RateLimited("0"));
        using var third = new HttpClient(repeated);
        ok = await WebhookService.PostWithClientAsync(
            "https://discord.com/api/webhooks/id/PRIVATE_TOKEN", "alert", third,
            warnings.Add, _ => Task.CompletedTask);
        Assert(!ok && repeated.RequestCount == 2, "Two 429 responses must not loop indefinitely");
    }

    internal static async Task NoFalseAck()
    {
        var redirects = new ScriptedHandler();
        redirects.Push(() => new HttpResponseMessage(HttpStatusCode.TemporaryRedirect)
        {
            Headers = { Location = new Uri("https://attacker.invalid/steal-token") }
        });
        using var client = new HttpClient(redirects);
        var warnings = new List<string>();
        bool ok = await WebhookService.PostWithClientAsync(
            "https://discord.com/api/webhooks/id/PRIVATE_TOKEN", "alert", client, warnings.Add);
        Assert(!ok && redirects.RequestCount == 1, "Redirect response is not a delivery ACK");

        var exception = new ScriptedHandler();
        exception.Push(() => throw new HttpRequestException(
            "https://discord.com/api/webhooks/id/PRIVATE_TOKEN connection failed"));
        using var uncertainClient = new HttpClient(exception);
        ok = await WebhookService.PostWithClientAsync(
            "https://discord.com/api/webhooks/id/PRIVATE_TOKEN", "alert", uncertainClient, warnings.Add);
        Assert(!ok && exception.RequestCount == 1, "Unknown transport outcome is not retried or acknowledged");
        Assert(warnings.All(w => !w.Contains("PRIVATE_TOKEN", StringComparison.Ordinal)),
            "Never interpolate HttpRequestException.Message containing a token");

        int previousCount = redirects.RequestCount;
        ok = await WebhookService.PostWithClientAsync(
            "https://discord.com/api/webhooks/id/PRIVATE_TOKEN#fragment", "alert", client, warnings.Add);
        Assert(!ok && redirects.RequestCount == previousCount, "URL with fragment is refused");
        ok = await WebhookService.PostWithClientAsync(
            "https://discord.com/api/webhooks/id/PRIVATE_TOKEN", new string('x', 2001), client, warnings.Add);
        Assert(!ok && redirects.RequestCount == previousCount, "Oversized Discord message is refused locally");
    }

    private static HttpResponseMessage RateLimited(string seconds)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation("Retry-After", seconds);
        return response;
    }

    private static void Assert(bool condition, string explanation)
    {
        if (!condition) throw new InvalidOperationException(explanation);
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new();
        internal int RequestCount { get; private set; }
        internal void Push(Func<HttpResponseMessage> response) => _responses.Enqueue(response);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (_responses.Count == 0) throw new InvalidOperationException("Unexpected extra HTTP request");
            return Task.FromResult(_responses.Dequeue()());
        }
    }
}
