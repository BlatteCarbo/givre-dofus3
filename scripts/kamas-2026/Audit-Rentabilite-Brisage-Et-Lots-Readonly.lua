-- Audit-Rentabilite-Brisage-Et-Lots-Readonly.lua
-- SCRIPT STRICTEMENT CONSULTATIF (lecture seule) :
--   * n'envoie AUCUN ordre d'achat ni de vente a l'HDV, ne fabrique RIEN (aucun craft), ne deplace pas le personnage,
--     ne touche ni a la banque ni a l'inventaire ;
--   * lit seulement l'etat courant (pods, kamas, sac, prix moyens du serveur deja en cache) et affiche dans la console
--     une liste de controle actionnable, la formule d'arbitrage par lots et le tableau des poids de runes.
-- Les prix de lots 1 / 10 / 100 doivent etre releves A LA MAIN dans l'HDV puis saisis dans LOT_PRICES.

-- ======================================================================
-- CONFIGURATION
-- ======================================================================
PODS_MAX = 85                 -- seuil de pods a partir duquel la liste de controle demande un depot en banque
MAX_CONSECUTIVE_DEFEATS = 3   -- non utilise (script sans combat), conserve pour uniformite des scripts du pack
BANK_MAP = 192415750          -- non utilise (aucun deplacement), conserve pour uniformite des scripts du pack
MAX_ROWS = 20                 -- lignes maximum du detail du sac (entier 1..100)
LOT_DISCOUNT = 0.98           -- condition d'arbitrage : Prix(100) / 10 < Prix(10) * LOT_DISCOUNT
HDV_TAX = 0.02                -- taxe de mise en vente, pour memoire dans l'estimation de marge
FOCUS_FACTOR = 0.60           -- facteur applique au poids dans la colonne "focus" du tableau (a ajuster)
MIN_COEFFICIENT_PERCENT = 100 -- coefficient de brisage en dessous duquel on ne brise pas, par prudence

-- Prix releves a la main : { nom, prix pour 1, prix pour 10, prix pour 100 }. Laisser vide si inconnu.
LOT_PRICES = {
    -- { "Ortie", 12, 110, 1000 },
}

-- Poids de runes (rune simple), tels que publies par les guides de forgemagie (voir README).
-- weight = poids d'une rune simple ; ra = poids d'une rune Ra (ou nil si elle n'existe pas)
RUNE_WEIGHTS = {
    { label = "Ga PA",              weight = 100 },
    { label = "Ga PM",              weight = 90 },
    { label = "Po (Portee)",        weight = 51 },
    { label = "Invocation",         weight = 30 },
    { label = "Dommages",           weight = 20 },
    { label = "Critique",           weight = 10 },
    { label = "Sagesse",            weight = 3,    ra = 30 },
    { label = "Prospection",        weight = 3 },
    { label = "Puissance",          weight = 2,    ra = 20 },
    { label = "Fo / Ine / Cha / Age", weight = 1,  ra = 10 },
    { label = "Vitalite (Vi)",      weight = 0.25, ra = 2.5 },
    { label = "Initiative",         weight = 0.1,  ra = 1 },
}

-- ======================================================================
-- MOTEUR LECTURE SEULE
-- ======================================================================
local TAG = "Audit lecture seule"
local ran = false

local function log(message)
    print("[" .. TAG .. "] " .. message)
end

-- Condition de lot : acheter le lot de 100 seulement si son prix par tranche de 10 est inferieur a Prix(10) * LOT_DISCOUNT.
-- Retourne vrai/faux et la marge brute theorique (avant taxe) par tranche de 10, ou nil si une donnee manque.
function batchArbitrage(price10, price100)
    if not price10 or not price100 or price10 <= 0 or price100 <= 0 then return nil end
    local perTen = price100 / 10
    local limit = price10 * LOT_DISCOUNT
    local margin = price10 * (1 - HDV_TAX) - perTen
    return perTen < limit, perTen, limit, margin
end

-- Poids de la colonne focus pour une rune donnee.
function focusWeight(weight)
    return weight * FOCUS_FACTOR
end

local function formatNumber(value)
    if value == math.floor(value) then return tostring(math.floor(value)) end
    return string.format("%.2f", value)
end

local function printRuneTable()
    log("--- Poids des runes (focus x" .. formatNumber(FOCUS_FACTOR) .. ") ---")
    log("PA / PM / PO / Ra Vi / stats : rune | poids | poids focus | Ra")
    for _, rune in ipairs(RUNE_WEIGHTS) do
        local ra = rune.ra and formatNumber(rune.ra) or "-"
        log("RUNE " .. rune.label .. " | poids=" .. formatNumber(rune.weight)
            .. " | focus=" .. formatNumber(focusWeight(rune.weight)) .. " | Ra=" .. ra)
    end
end

local function printLotFormula()
    log("--- Arbitrage par lots ---")
    log("FORMULE acheter le lot de 100 si Prix(100) / 10 < Prix(10) * " .. formatNumber(LOT_DISCOUNT))
    log("FORMULE marge brute par 10 = Prix(10) * (1 - " .. formatNumber(HDV_TAX) .. ") - Prix(100) / 10")
    if #LOT_PRICES == 0 then
        log("LOTS aucun prix saisi dans LOT_PRICES : relever les prix 1 / 10 / 100 a la main dans l'HDV")
        return
    end
    for _, row in ipairs(LOT_PRICES) do
        local ok, perTen, limit, margin = batchArbitrage(row[3], row[4])
        if ok == nil then
            log("LOT " .. tostring(row[1]) .. " : prix incomplets, ignore")
        else
            log("LOT " .. tostring(row[1]) .. " : Prix(100)/10=" .. formatNumber(perTen) .. " seuil=" .. formatNumber(limit)
                .. " marge/10=" .. formatNumber(margin) .. " -> " .. (ok and "OPPORTUNITE (a verifier a la main)" or "pas d'ecart exploitable"))
        end
    end
end

local function inspectState()
    local server = getServerName()
    if server == "" then server = "inconnu" end
    local pods, podsMax, percent = getPods(), getPodsMax(), getPodsPercent()
    local kamas = getKamas()
    log("--- Etat courant ---")
    log("PERSO " .. tostring(getCharacterName()) .. " niveau=" .. tostring(getCharacterLevel()) .. " serveur=" .. server)
    log("PODS " .. pods .. "/" .. podsMax .. " (" .. percent .. "%) kamas=" .. kamas)
    local groups, order = {}, {}
    local stacks, quantity = 0, 0
    for _, item in ipairs(inventory:items()) do
        if item.pos == 63 then
            stacks = stacks + 1
            quantity = quantity + item.qty
            local row = groups[item.gid]
            if not row then
                row = { gid = item.gid, name = item.name, qty = 0 }
                groups[item.gid] = row
                order[#order + 1] = row
            end
            row.qty = row.qty + item.qty
        end
    end
    table.sort(order, function(a, b) return a.gid < b.gid end)
    local known, unknown = 0, 0
    for index, row in ipairs(order) do
        local price = 0
        if server ~= "inconnu" then price = inventory:getAveragePrice(row.gid) end
        if price > 0 then known = known + price * row.qty else unknown = unknown + 1 end
        if index <= MAX_ROWS then
            log("SAC gid=" .. row.gid .. " " .. tostring(row.name) .. " x" .. row.qty
                .. " prix moyen=" .. (price > 0 and tostring(price) or "inconnu"))
        end
    end
    log("SAC piles=" .. stacks .. " quantite=" .. quantity .. " types=" .. #order
        .. " valeur connue=" .. known .. " lignes sans prix=" .. unknown)
    return percent, kamas, #order, unknown
end

local function printChecklist(percent, kamas, types, unknown)
    log("--- Liste de controle ---")
    local n = 0
    local function step(text)
        n = n + 1
        log("A FAIRE " .. n .. ". " .. text)
    end
    if percent >= PODS_MAX then
        step("pods a " .. percent .. "% (seuil " .. PODS_MAX .. "%) : deposer le sac en banque avant tout brisage ou lot")
    else
        step("pods a " .. percent .. "% : marge disponible pour transporter des lots")
    end
    if kamas >= 0 and kamas < 100000 then
        step("kamas bas (" .. kamas .. ") : prevoir les frais de banque et la taxe HDV de " .. formatNumber(HDV_TAX * 100) .. "%")
    end
    if unknown > 0 then
        step(unknown .. " type(s) d'objet sans prix moyen en cache : ouvrir l'HDV et relever les prix avant de decider")
    end
    step("brisage : casser UN objet test et lire le coefficient du serveur ; ne pas en casser en serie sous " .. MIN_COEFFICIENT_PERCENT .. "%")
    step("comparer la valeur des runes (Ga PA, Ga PM, Po, Ra Vi) au cout d'achat ou de craft de l'objet, taxe HDV incluse")
    step("lots : relever Prix(1), Prix(10), Prix(100) puis les saisir dans LOT_PRICES et relancer ce script")
    step("garder des limites ecrites (budget par session, nombre d'essais) avant d'agir a la main")
    log("RAPPEL ce script n'a envoye aucun ordre : ni achat, ni vente, ni craft, ni deplacement")
end

function move()
    if ran then return false end
    ran = true
    assert(MAX_ROWS >= 1 and MAX_ROWS <= 100 and MAX_ROWS == math.floor(MAX_ROWS), "MAX_ROWS doit etre un entier 1..100")
    log("debut de l'audit consultatif")
    local percent, kamas, types, unknown = inspectState()
    printChecklist(percent, kamas, types, unknown)
    printLotFormula()
    printRuneTable()
    printSuccess("[" .. TAG .. "] audit termine : aucune action de jeu executee")
    return false
end
