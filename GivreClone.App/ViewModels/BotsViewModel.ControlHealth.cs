using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using GivreClone.App.Services;
using GivreClone.App.Services.Diagnostics;
using GivreClone.App.Services.Mcp;

namespace GivreClone.App.ViewModels;

public partial class BotsViewModel
{
    /// <summary>Read-only MCP diagnostic based on live observed facts and explicit unknowns.</summary>
    internal JsonNode ControlHealth(JsonElement args)
    {
        var (identity, bot) = ControlIdentity(args);
        uint pid = (uint)Math.Max(0, bot.Pid);
        var launch = Stores.Launch;
        var world = pid > 0 ? Stores.World.Get(pid) : null;
        var down = pid > 0 ? launch.LinkDownOf(pid) : null;
        var source = pid > 0 ? launch.SocketSourceAuthorityOf(pid) : null;
        var uidResponse = pid > 0 ? launch.SocketUidEnvelopeEvidenceOf(pid) : null;
        var mitmSource = pid > 0 ? launch.LocalMitmSourceAuthorityOf(pid) : null;
        var result = BotReadiness.Evaluate(new BotReadiness.Input(
            bot.Status, bot.Pid, bot.MapId, bot.InFight, bot.ScriptRunning,
            pid > 0 && launch.IsLinkSuspended(pid),
            world is null ? null : world.SelfCellConfirmed,
            ProtocolVerified: null,
            ServerAcknowledgementVerified: null,
            HdvSellReview: pid > 0 && launch.HdvSellLastDisposition(pid) == HdvSellDisposition.NeedsReview,
            HdvRemoveReview: pid > 0 && launch.HdvRemoveLastDisposition(pid) == HdvSellDisposition.NeedsReview,
            HdvRepriceReview: pid > 0 && launch.HdvRepriceReviewPending(pid),
            HdvBuyReview: pid > 0 && launch.HdvBuyReviewPending(pid),
            // The raw disconnect string may include transport/user data. Readiness only needs evidence of a disconnect.
            DisconnectReason: down is null ? null : "Game link interruption observed"));
        return new JsonObject
        {
            ["bot"] = bot.Name,
            ["verdict"] = result.Verdict,
            ["mayAssumeGameplayConfirmed"] = false,
            ["accountIdentityVerified"] = !string.IsNullOrWhiteSpace(identity.Login),
            ["accountProtected"] = string.IsNullOrWhiteSpace(identity.Login) ? null
                : McpGuards.IsBlocked(identity.Login, AppConfig.Current.McpBlockedLogins ?? McpGuards.DefaultBlockedLogins),
            ["clientVersionVerified"] = false,
            ["protocolVerified"] = null,
            ["serverAcknowledgementVerified"] = null,
            ["socketEnvelopeUidV20"] = uidResponse is { } uid
                ? new JsonObject
                {
                    ["status"] = "source-bound-envelope-observation-only",
                    ["postFlushPositiveUidRequests"] = uid.PostFlushRequestsWithUniqueUid,
                    ["matchedResponseUidEnvelopes"] = uid.MatchingResponseEnvelopes,
                    ["unmatchedResponseUidEnvelopes"] = uid.UnmatchedResponseEnvelopes,
                    ["ambiguousOrReusedUids"] = uid.AmbiguousOrReusedUids,
                    ["positiveUidResponseObserved"] = uid.PositiveUidResponseObserved,
                    ["gameplayOutcomeVerified"] = false,
                    ["correlatedGameplayAck"] = false,
                    ["canAuthorizeGameplay"] = false,
                    ["blockReason"] = uid.BlockReason
                }
                : new JsonObject
                {
                    ["status"] = "not-observed",
                    ["positiveUidResponseObserved"] = false,
                    ["gameplayOutcomeVerified"] = false,
                    ["correlatedGameplayAck"] = false,
                    ["canAuthorizeGameplay"] = false
                },
            ["socketAuthority"] = source is { } scoped
                ? new JsonObject
                {
                    ["status"] = "local-source-observed-only",
                    ["runEpoch"] = scoped.RunEpoch,
                    ["engineWritesAfterFlush"] = scoped.EngineWritesCompleted,
                    ["serverFramedChunks"] = scoped.ServerFramesObserved,
                    ["serverWireBytes"] = scoped.ServerWireBytes,
                    ["correlatedServerAck"] = false,
                    ["canAuthorizeGameplay"] = false,
                    ["blockReason"] = scoped.BlockReason,
                }
                : new JsonObject
                {
                    ["status"] = "not-observed",
                    ["correlatedServerAck"] = false,
                    ["canAuthorizeGameplay"] = false,
                    ["blockReason"] = "Aucune source Socket actuelle vérifiée dans le canal consultatif.",
                },
            ["localMitmAuthority"] = mitmSource is { } tunnel
                ? new JsonObject
                {
                    ["status"] = "local-tunnel-source-observed-only",
                    ["runEpoch"] = tunnel.RunEpoch,
                    ["serverFramedChunks"] = tunnel.ServerFramesObserved,
                    ["serverWireBytes"] = tunnel.ServerWireBytes,
                    ["correlatedServerAck"] = false,
                    ["canAuthorizeGameplay"] = false,
                    ["blockReason"] = tunnel.BlockReason,
                }
                : new JsonObject
                {
                    ["status"] = "not-observed",
                    ["correlatedServerAck"] = false,
                    ["canAuthorizeGameplay"] = false,
                },
            ["checks"] = new JsonArray(result.Checks.Select(c => (JsonNode?)new JsonObject
            {
                ["id"] = c.Id, ["state"] = c.State,
                ["detail"] = c.Detail, ["nextAction"] = c.NextAction
            }).ToArray()),
            ["note"] = "Le diagnostic n'effectue aucun envoi. Version/protocole et ACK restent non vérifiés sans preuve indépendante."
        };
    }
}
