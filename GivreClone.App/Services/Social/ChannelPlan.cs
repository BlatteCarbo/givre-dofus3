using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GivreCore.Protobuf;

namespace GivreClone.App.Services.Social;

/// <summary>
/// Abonnement lu dans SubscribeMultipleChannelRequest. Les cases du panneau filtrent l'affichage local.
/// L'envoi vers le jeu est refusé en MITM tant que l'utilisateur n'a pas autorisé le changement des cases.
/// </summary>
public static class ChannelPlan
{
    public const string BoxesStay = "Les cases du jeu restent inchangées.";
    public const string BoxesWillChangeOnAsk = "L'abonnement à tous les canaux modifiera les cases du jeu au moment où vous le demandez.";
    public const string BoxesWillChange = "Demande envoyée. Les cases du jeu vont changer.";
    public const string SubscribeReady = "Trame prête. Le parent doit l'injecter.";
    public const string SubscribeSent = "Demande envoyée.";

    /// <summary>Actifs à la connexion, ligne 62449. Ordre du journal, pas un tri.</summary>
    public static readonly int[] LoginActive = { 0, 1, 2, 3, 4, 10, 12, 13, 11, 16, 18, 19 };

    /// <summary>Coupés à la connexion, même trame. 5 et 6 sont réactivés plus tard (lignes 68950 et 68956).</summary>
    public static readonly int[] LoginDisabled = { 5, 6, 8, 9, 15, 17 };

    /// <summary>Tous les canaux vus dans ces listes : actifs, puis coupés absents des actifs.</summary>
    public static readonly int[] AllChannels = { 0, 1, 2, 3, 4, 10, 12, 13, 11, 16, 18, 19, 5, 6, 8, 9, 15, 17 };

    public static bool MaySend(bool mitm, bool allowGameCheckboxChange) => !mitm || allowGameCheckboxChange;

    public static bool Shows(int channel, IReadOnlySet<int> selected) => selected.Contains(channel);

    public static (int[] Active, int[] Disabled) Read(IReadOnlyList<ProtoField> payload, Func<string, string, int?>? map)
    {
        int active = SocialFields.N(map, "SubscribeMultipleChannelRequest", "active_channels", 1);
        int disabled = SocialFields.N(map, "SubscribeMultipleChannelRequest", "disabled_channels", 2);
        return (SocialFields.RawBytes(payload, active), SocialFields.RawBytes(payload, disabled));
    }

    public static string Label(int id) => id switch
    {
        0 => "Général",
        1 => "Groupe",
        2 => "Guilde",
        3 => "Alliance",
        4 => "Équipe",
        5 => "Commerce",
        6 => "Recrutement",
        8 => "Administration",
        9 => "Informations",
        10 => "Privé",
        11 => "Communauté",
        12 => "Kolizeum",
        _ => "Canal " + id.ToString(CultureInfo.InvariantCulture),
    };

    public static string Summary(IReadOnlyList<int> active, IReadOnlyList<int> disabled)
    {
        static string Join(IReadOnlyList<int> ids) => ids.Count == 0 ? "aucun" : string.Join(", ", ids.Select(Label));
        return "Canaux reçus : " + Join(active) + ". Coupés : " + Join(disabled) + ".";
    }
}
