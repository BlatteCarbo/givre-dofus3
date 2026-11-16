using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Supervisor;

/// <summary>Modèle de script de superviseur proposé dans l'éditeur.</summary>
public sealed record SupervisorTemplate(string Id, string Title, string Code);

/// <summary>
/// Modèles de superviseur (d'après les exemples du portail). Chaque modèle doit se charger sans erreur de syntaxe dans l'hôte
/// (vérifié par un test). L'équipe (« Solo puis groupe ») n'est pas proposée : le module teams: n'est pas porté dans ce lot.
/// </summary>
public static class SupervisorTemplates
{
    public const string WatchId = "veille", AccountsId = "comptes", EmptyId = "vide";

    public static readonly IReadOnlyList<SupervisorTemplate> All = new SupervisorTemplate[]
    {
        new(EmptyId, "Squelette vide", """
            -- Superviseur : ne pilote aucun perso, surveille les bots et leur donne des ordres.
            function onBotEvent(bot, evenement, infos)
                -- evenement : connected, disconnected, offline, crashed, levelUp, death, podsFull, scriptStarted, scriptStopped, moderator...
            end

            function move()
                delay(5000)
            end
            """),
        new(WatchId, "Veille 24/24 + arrêt d'urgence sur modérateur", """
            -- Superviseur : veille 24/24 + arrêt d'urgence sur modérateur
            WEBHOOK = ""

            function onBotEvent(bot, evenement, infos)
                if evenement == "moderator" then
                    bots:stopAllScripts()
                    if WEBHOOK ~= "" then sendWebhook(WEBHOOK, "Modérateur sur " .. bot.name) end
                elseif (evenement == "disconnected" or evenement == "crashed") and infos.reason ~= "stopped" then
                    if WEBHOOK ~= "" then sendWebhook(WEBHOOK, bot.account .. " : " .. evenement) end
                    bots:reconnect(bot.account)
                end
            end

            function move()
                delay(5000)
            end
            """),
        new(AccountsId, "Ajouter des comptes depuis un fichier", """
            -- Superviseur : ajoute les comptes d'un fichier puis lance leur script
            WEBHOOK = ""

            function onBotEvent(bot, evenement, infos)
                if evenement == "loginWaiting" and WEBHOOK ~= "" then
                    sendWebhook(WEBHOOK, bot.name .. " attend une connexion à la main (" .. infos.reason .. ")")
                end
            end

            function move()
                local comptes = accounts:importFile("C:\\Givre\\comptes.txt", "C:\\Givre\\proxys.txt")
                for _, c in ipairs(comptes) do
                    accounts:setMode(c.id, "socket")
                    bots:connect(c.id)
                    if bots:waitReady(c.id, 600) then bots:startScript(c.id, "Farm Incarnam") end
                end
                return false
            end
            """),
    };

    public static SupervisorTemplate? Get(string id) => All.FirstOrDefault(t => t.Id == id);
}
