using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GivreClone.App.ViewModels;

public sealed record HdvUidOperatorRow(uint Pid, string Stage, string BagUid, string ListingUid,
    string Disposition, string Evidence, string Diagnostic)
{
    private const string Number = "[0-9]{1,10}";
    private const string Uid = "(?:[0-9]{1,10}|\\?)";
    private const string Event = "(?:timeout|other|ExchangeBidHouseItemAddedEvent|ObjectQuantityEvent|ObjectsQuantityEvent|ObjectDeletedEvent|ObjectsDeletedEvent|ExchangeErrorEvent|ObjectErrorEvent|ExchangeBidHouseItemRemovedEvent|ExchangeBidHouseInListRemovedEvent)";
    private const string Result = "(?:Confirmed|NeedsReview|Refused)";
    private static readonly (string Stage, Regex Pattern)[] Patterns =
    {
        Make("Vente · demande", $@"vente demande uid-sac=(?<bag>{Number}) quantite={Number} inject=(?:OK|ECHEC) \(aucun ACK impliqué\)\."),
        Make("Vente · candidat", $@"annonce reçue uid-lot=(?<lot>{Uid}) \(candidat, non corrélé\)\."),
        Make("Vente · résultat", $@"vente uid-sac=(?<bag>{Number}) disposition=(?<result>{Result}) sell_id=(?<lot>{Uid}) event={Event}\."),
        Make("Reprix · candidat", $@"reprice ancien sell_id=(?<lot>{Number}) uid-lot-annonce=(?<candidate>{Uid}) \(réponse non corrélée à la demande\)\."),
        Make("Retrait · demande", $@"retrait demande sell_id=(?<lot>{Number}) quantite={Number} inject=(?:OK|ECHEC) lien-sac-verifie=(?:True|False) \(aucun ACK impliqué\)\."),
        Make("Retrait · résultat", $@"retrait reponse event={Event} sell_id-demande=(?<lot>{Number}) disposition=(?<result>{Result}) lien-sac-verifie=(?:True|False)\."),
    };

    private static (string, Regex) Make(string stage, string body) =>
        (stage, new Regex($@"\A\[hdv\] pid (?<pid>{Number}) : {body}\z",
            RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromMilliseconds(50)));

    public static HdvUidOperatorRow? Parse(string message)
    {
        if (message.Length > 700 || !message.StartsWith("[hdv] pid ", StringComparison.Ordinal)) return null;
        foreach (var (stage, pattern) in Patterns)
        {
            var match = pattern.Match(message);
            if (!match.Success || !uint.TryParse(match.Groups["pid"].Value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out uint pid) || pid == 0) continue;
            string Field(string key) => match.Groups[key].Success ? match.Groups[key].Value : "?";
            string disposition = Field("result");
            string evidence = disposition switch
            {
                "Confirmed" => "Corrélation déclarée par le moteur ; validation en jeu requise.",
                "NeedsReview" => "À vérifier dans le jeu ; ne pas répéter l'opération.",
                "Refused" => "Refus déclaré ; aucun succès établi.",
                _ => "Observation seule ; aucun ACK établi."
            };
            if (stage == "Reprix · candidat") evidence = $"Nouveau lot candidat : {Field("candidate")} ; réponse non corrélée.";
            return new(pid, stage, Field("bag"), Field("lot"), disposition, evidence, message);
        }
        return null;
    }
}
