--[[
    MizanBot Mineur 1-200 AUTO + Discord Notifier — Adaptive v2
    ------------------------------------------------
    Integration directe :
      - suivi Mineur jobId=24
      - webhook Discord sur level-up metier
      - level-up personnage configurable
      - infos perso/serveur/script/position/pods/kamas/stats
      - aucun deuxieme move() ajoute
]]

--[[
    Mizan Discord Notifier
    ---------------------
    Addon partageable pour MizanBot.

    Installation recommandee :
      1) Placer ce fichier dans scripts/lib/mizan_discord_notifier.lua
      2) Dans ton script principal :
            local Webhook = dofile("mizan_discord_notifier")
      3) Appeler Webhook.tick() au debut de move() (et bank() si besoin)
      4) Optionnel : appeler Webhook.onStopped(reason) dans stopped(reason)

    IMPORTANT : ne partage JAMAIS ton URL de webhook Discord.
]]

local Webhook = {}

Webhook.config = {
    -- OBLIGATOIRE
    WEBHOOK_URL = "",

    -- Affichage Discord
    WEBHOOK_NAME = "Mizan • Mineur Intelligence",
    WEBHOOK_AVATAR_URL = "",
    SCRIPT_NAME = "Mineur 1-200 AUTO",
    FOOTER_TEXT = "MizanBot • Adaptive Miner v2",

    -- Evenements
    EVENT_JOB_LEVEL_UP = true,
    EVENT_CHARACTER_LEVEL_UP = true,
    EVENT_SCRIPT_START = false,
    EVENT_SCRIPT_STOP = true,
    EVENT_INCIDENT = true,
    INCIDENT_COOLDOWN_SEC = 90,

    -- Surveillance
    CHECK_INTERVAL_SEC = 1,
    TRACK_JOBS = { 24 },       -- vide = tous. Ex: { 2, 28, "Paysan" }
    JOB_MILESTONES = {},   -- vide = chaque level metier. Ex: { 20, 40, 60, 80, 100, 150, 200 }
    CHARACTER_MILESTONES = {}, -- vide = chaque level personnage
    NOTIFY_MISSED_LEVELS_ON_RESTART = false,
    PERSIST_LEVELS = true,
    STATE_NAMESPACE = "mizan_webhook_levels_v1",

    -- Image
    -- "auto"      : image metier personnalisee > image perso > symbole de classe DofusDB
    -- "job"       : image metier personnalisee seulement
    -- "character" : image perso personnalisee sinon symbole de classe
    -- "class"     : symbole de classe DofusDB
    -- "none"      : aucune image
    IMAGE_MODE = "auto",
    DOFUSDB_IMAGE_STYLE = "head", -- "head" (portrait classe/sexe) ou "symbol"
    CHARACTER_IMAGE_URL = "",
    JOB_IMAGE_URLS = {
        -- [2] = "https://ton-cdn.fr/bucheron.png",
        -- [28] = "https://ton-cdn.fr/paysan.png",
    },

    -- Details affiches
    SHOW_ACCOUNT_ALIAS = true,
    SHOW_SCRIPT = true,
    SHOW_POSITION = true,
    SHOW_AREA = true,
    SHOW_PODS = true,
    SHOW_KAMAS = true,
    SHOW_SESSION_STATS = true,
    SHOW_RUNTIME = true,
    SHOW_STATUS = true,
    SHOW_CHARACTER_LEVEL = true,
    SHOW_CLASS = true,
    SHOW_MINER_TELEMETRY = true,

    -- Pings optionnels
    MENTION = "",                  -- ex: "<@123456789>" ou "<@&ROLE_ID>"
    PING_ON_LEVEL_200 = false,

    -- Console
    VERBOSE = true,
}

local C = Webhook.config
local initialized = false
local startupSent = false
local lastCheckAt = 0
local sessionStartedAt = os.time()
local lastJobs = {}
local lastCharacterLevel = nil

local COLOR_JOB = 5763719       -- Discord green
local COLOR_CHARACTER = 5793266 -- blurple
local COLOR_START = 3447003
local COLOR_STOP = 10070709
local COLOR_TEST = 10181046
local COLOR_INCIDENT = 15158332
local COLOR_BANK = 15844367

local function safeCall(fn, fallback)
    local ok, value = pcall(fn)
    if ok and value ~= nil then return value end
    return fallback
end

local function tostr(v, fallback)
    if v == nil then return fallback or "?" end
    local s = tostring(v)
    if s == "" then return fallback or "?" end
    return s
end

local function jsonEscape(value)
    local s = tostring(value or "")
    s = s:gsub("\\", "\\\\")
    s = s:gsub('"', '\\"')
    s = s:gsub("\b", "\\b")
    s = s:gsub("\f", "\\f")
    s = s:gsub("\n", "\\n")
    s = s:gsub("\r", "\\r")
    s = s:gsub("\t", "\\t")
    return s
end

local function jq(value)
    return '"' .. jsonEscape(value) .. '"'
end

local function fmtInt(n)
    n = tonumber(n)
    if not n or n < 0 then return "?" end
    local s = tostring(math.floor(n))
    local rev = s:reverse():gsub("(%d%d%d)", "%1 ")
    return rev:reverse():gsub("^ ", "")
end

local function fmtDuration(seconds)
    seconds = math.max(0, tonumber(seconds) or 0)
    local h = math.floor(seconds / 3600)
    local m = math.floor((seconds % 3600) / 60)
    local s = math.floor(seconds % 60)
    if h > 0 then return string.format("%dh %02dm %02ds", h, m, s) end
    if m > 0 then return string.format("%dm %02ds", m, s) end
    return string.format("%ds", s)
end

local function fmtRate(n)
    n = tonumber(n) or 0
    if n >= 1000 then return string.format("%.1fk", n / 1000) end
    return tostring(math.floor(n + 0.5))
end

local function levelProgressBar(level, maxLevel)
    level = math.max(0, tonumber(level) or 0)
    maxLevel = math.max(1, tonumber(maxLevel) or 200)
    local pct = math.min(100, math.max(0, (level / maxLevel) * 100))
    local blocks = math.floor((pct / 100) * 12 + 0.5)
    return string.rep("█", blocks) .. string.rep("░", 12 - blocks) .. " " .. tostring(math.floor(pct + 0.5)) .. "%"
end

local function contains(list, value, valueName)
    if not list or #list == 0 then return true end
    local targetName = string.lower(tostring(valueName or ""))
    for _, v in ipairs(list) do
        if tonumber(v) ~= nil and tonumber(v) == tonumber(value) then return true end
        if string.lower(tostring(v)) == targetName then return true end
    end
    return false
end

local function isMilestone(list, level)
    if not list or #list == 0 then return true end
    for _, v in ipairs(list) do
        if tonumber(v) == tonumber(level) then return true end
    end
    return false
end

local function charInfo()
    local name = safeCall(function() return character:name() end, "")
    if name == "" then name = safeCall(function() return getCharacterName() end, "?") end

    local level = safeCall(function() return character:level() end, 0)
    if level == 0 then level = safeCall(function() return getCharacterLevel() end, 0) end

    local breedId = safeCall(function() return character:breed() end, 0)
    local breedName = safeCall(function() return character:breedName() end, "")
    local server = safeCall(function() return character:server() end, "")
    if server == "" then server = safeCall(function() return getServerName() end, "?") end

    local sex = safeCall(function() return character:sex() end, false)

    local alias = safeCall(function() return global:getAlias() end, "")
    if alias == "" then alias = safeCall(function() return global:username() end, "") end

    return {
        name = tostr(name),
        level = tonumber(level) or 0,
        breedId = tonumber(breedId) or 0,
        breedName = tostr(breedName, "Classe inconnue"),
        sex = sex == true,
        server = tostr(server),
        alias = tostr(alias, "?"),
    }
end

local function classImageUrl(info)
    local breedId = tonumber(info.breedId) or 0
    if breedId <= 0 then return "" end

    if string.lower(tostring(C.DOFUSDB_IMAGE_STYLE or "head")) == "symbol" then
        return "https://api.dofusdb.fr/img/breeds/symbol_" .. tostring(breedId) .. ".png"
    end

    -- DofusDB : male head = breedId * 10 ; female head = breedId * 10 + 1.
    local headId = breedId * 10 + (info.sex and 1 or 0)
    return "https://api.dofusdb.fr/img/heads/SmallHead_" .. tostring(headId) .. ".png"
end

local function eventImage(jobId, info)
    local mode = string.lower(tostring(C.IMAGE_MODE or "auto"))
    if mode == "none" then return "" end

    local jobUrl = C.JOB_IMAGE_URLS and C.JOB_IMAGE_URLS[tonumber(jobId) or -1] or ""
    local charUrl = tostring(C.CHARACTER_IMAGE_URL or "")
    local classUrl = classImageUrl(info)

    if mode == "job" then return jobUrl or "" end
    if mode == "character" then
        if charUrl ~= "" then return charUrl end
        return classUrl
    end
    if mode == "class" then return classUrl end

    -- auto
    if jobUrl and jobUrl ~= "" then return jobUrl end
    if charUrl ~= "" then return charUrl end
    return classUrl
end

local function stateKey(kind, id)
    return tostring(C.STATE_NAMESPACE or "mizan_webhook") .. "." .. tostring(kind) .. "." .. tostring(id or "main")
end

local function memGetNumber(key)
    if not C.PERSIST_LEVELS then return nil end
    return safeCall(function()
        local raw = memory:get(key)
        if raw == nil then return nil end
        return tonumber(raw)
    end, nil)
end

local function memSetNumber(key, value)
    if not C.PERSIST_LEVELS then return end
    safeCall(function() return memory:set(key, tonumber(value) or 0) end, false)
end

local function console(message, level)
    if not C.VERBOSE then return end
    safeCall(function()
        printMessage("[Discord] " .. tostring(message), level or "grey")
        return true
    end, false)
end

local function makeField(name, value, inline)
    return {
        name = tostring(name or "?"),
        value = tostring(value or "?"),
        inline = inline == true,
    }
end

local function fieldJson(f)
    return "{" ..
        '"name":' .. jq(f.name) .. "," ..
        '"value":' .. jq(f.value) .. "," ..
        '"inline":' .. (f.inline and "true" or "false") ..
    "}"
end

local function buildPayload(title, description, color, fields, thumbnailUrl, content)
    local chunks = {}
    chunks[#chunks + 1] = "{"

    if C.WEBHOOK_NAME and C.WEBHOOK_NAME ~= "" then
        chunks[#chunks + 1] = '"username":' .. jq(C.WEBHOOK_NAME) .. ","
    end
    if C.WEBHOOK_AVATAR_URL and C.WEBHOOK_AVATAR_URL ~= "" then
        chunks[#chunks + 1] = '"avatar_url":' .. jq(C.WEBHOOK_AVATAR_URL) .. ","
    end
    if content and content ~= "" then
        chunks[#chunks + 1] = '"content":' .. jq(content) .. ","
    end

    chunks[#chunks + 1] = '"embeds":[{'
    chunks[#chunks + 1] = '"title":' .. jq(title) .. ","
    chunks[#chunks + 1] = '"description":' .. jq(description) .. ","
    chunks[#chunks + 1] = '"color":' .. tostring(tonumber(color) or COLOR_TEST) .. ","
    chunks[#chunks + 1] = '"timestamp":' .. jq(os.date("!%Y-%m-%dT%H:%M:%SZ")) .. ","

    local info = charInfo()
    chunks[#chunks + 1] = '"author":{"name":' .. jq(info.name .. " • " .. info.server) .. '},'

    if thumbnailUrl and thumbnailUrl ~= "" then
        chunks[#chunks + 1] = '"thumbnail":{"url":' .. jq(thumbnailUrl) .. '},'
    end

    chunks[#chunks + 1] = '"fields":['
    for i, f in ipairs(fields or {}) do
        if i > 1 then chunks[#chunks + 1] = "," end
        chunks[#chunks + 1] = fieldJson(f)
    end
    chunks[#chunks + 1] = "],"

    chunks[#chunks + 1] = '"footer":{"text":' .. jq(C.FOOTER_TEXT or "MizanBot") .. "}"
    chunks[#chunks + 1] = "}]}"
    return table.concat(chunks)
end

local function webhookUrl()
    local url = tostring(C.WEBHOOK_URL or "")
    if url == "" or url:find("CHANGE_ME", 1, true) then return "" end
    -- wait=true donne une reponse Discord exploitable au lieu d'un 204 aveugle.
    if url:find("?", 1, true) then return url .. "&wait=true" end
    return url .. "?wait=true"
end

local function postEmbed(title, description, color, fields, thumbnailUrl, content)
    local url = webhookUrl()
    if url == "" then
        console("WEBHOOK_URL non configuree", "warning")
        return false
    end

    local body = buildPayload(title, description, color, fields, thumbnailUrl, content)
    local response = safeCall(function()
        return http:post(url, body, { ["Content-Type"] = "application/json" })
    end, nil)

    if response and response.ok then
        console("Webhook envoye (HTTP " .. tostring(response.status or "?") .. ")", "green")
        return true
    end

    local status = response and response.status or "?"
    local err = response and response.error or "erreur inconnue"
    console("Echec webhook (HTTP " .. tostring(status) .. ", " .. tostring(err) .. ")", "red")
    return false
end

local function locationText()
    local pos = safeCall(function() return getCurrentPos() end, "")
    local mapId = safeCall(function() return getMapId() end, 0)
    if pos == "" then pos = "?" end
    return "`[" .. tostring(pos) .. "]` • map `" .. tostring(mapId) .. "`"
end

local function areaText()
    local sub = safeCall(function() return map:currentSubArea() end, "")
    local area = safeCall(function() return map:currentArea() end, "")
    if area == "" and sub == "" then return "?" end
    if area ~= "" and sub ~= "" then return area .. " • " .. sub end
    return area ~= "" and area or sub
end

local function podsText()
    local pods = safeCall(function() return getPods() end, -1)
    local maxPods = safeCall(function() return getPodsMax() end, -1)
    local pct = safeCall(function() return getPodsPercent() end, -1)
    if tonumber(pods) == nil or tonumber(pods) < 0 then return "?" end
    local text = fmtInt(pods) .. " / " .. fmtInt(maxPods)
    if tonumber(pct) and tonumber(pct) >= 0 then text = text .. " (" .. tostring(math.floor(pct + 0.5)) .. "%)" end
    return text
end

local function statsText()
    local h = safeCall(function() return stats:harvests() end, 0)
    local r = safeCall(function() return stats:resources() end, 0)
    local f = safeCall(function() return stats:fights() end, 0)
    local w = safeCall(function() return stats:won() end, 0)
    return fmtInt(h) .. " recoltes • " .. fmtInt(r) .. " ressources • " .. fmtInt(w) .. "/" .. fmtInt(f) .. " combats"
end

local function commonFields(info)
    local fields = {}

    if C.SHOW_CHARACTER_LEVEL or C.SHOW_CLASS then
        local parts = {}
        if C.SHOW_CLASS then parts[#parts + 1] = info.breedName end
        if C.SHOW_CHARACTER_LEVEL then parts[#parts + 1] = "niv. " .. tostring(info.level) end
        fields[#fields + 1] = makeField("👤 Personnage", "**" .. info.name .. "** • " .. table.concat(parts, " • "), true)
    end

    fields[#fields + 1] = makeField("🌐 Serveur", info.server, true)

    if C.SHOW_ACCOUNT_ALIAS then
        fields[#fields + 1] = makeField("🏷️ Compte", info.alias, true)
    end

    if C.SHOW_SCRIPT then
        fields[#fields + 1] = makeField("📜 Script", tostring(C.SCRIPT_NAME or "?"), true)
    end

    if C.SHOW_POSITION then
        fields[#fields + 1] = makeField("🗺️ Position", locationText(), true)
    end

    if C.SHOW_AREA then
        fields[#fields + 1] = makeField("📍 Zone", areaText(), true)
    end

    if C.SHOW_PODS then
        fields[#fields + 1] = makeField("🎒 Pods", podsText(), true)
    end

    if C.SHOW_KAMAS then
        local kamas = safeCall(function() return getKamas() end, -1)
        fields[#fields + 1] = makeField("💰 Kamas", fmtInt(kamas), true)
    end

    if C.SHOW_STATUS then
        local status = safeCall(function() return getStatus() end, "?")
        fields[#fields + 1] = makeField("🔄 Etat", tostring(status), true)
    end

    if C.SHOW_RUNTIME then
        fields[#fields + 1] = makeField("⏱️ Runtime", fmtDuration(os.time() - sessionStartedAt), true)
    end

    if C.SHOW_SESSION_STATS then
        fields[#fields + 1] = makeField("📊 Session", statsText(), false)
    end

    return fields
end

local function pingContent(level)
    if not C.PING_ON_LEVEL_200 then return "" end
    if tonumber(level) ~= 200 then return "" end
    return tostring(C.MENTION or "")
end

local function sendJobLevel(jobId, oldLevel, newLevel)
    local info = charInfo()
    local jobName = safeCall(function() return job:name(jobId) end, "Metier " .. tostring(jobId))
    local fields = commonFields(info)

    table.insert(fields, 1, makeField("🛠️ Metier", "**" .. tostring(jobName) .. "**", true))
    table.insert(fields, 2, makeField("⬆️ Niveau", "`" .. tostring(oldLevel) .. "` → **`" .. tostring(newLevel) .. "`**", true))
    if tonumber(newLevel) - tonumber(oldLevel) > 1 then
        table.insert(fields, 3, makeField("⚡ Progression", "+" .. tostring(tonumber(newLevel) - tonumber(oldLevel)) .. " niveaux detectes", true))
    end

    local minerCtx = nil
    if tonumber(jobId) == 24 and C.SHOW_MINER_TELEMETRY and type(Webhook.minerContextProvider) == "function" then
        minerCtx = safeCall(function() return Webhook.minerContextProvider() end, nil)
    end
    if minerCtx then
        table.insert(fields, 3, makeField("📈 Progression 1–200", "`" .. levelProgressBar(newLevel, 200) .. "`", false))
        table.insert(fields, 4, makeField("⏱️ Depuis le niveau precedent", fmtDuration(minerCtx.sinceLevelSec or 0), true))
        table.insert(fields, 5, makeField("⛏️ Minerais / ressources", fmtInt(minerCtx.sinceLevelResources or 0), true))
        table.insert(fields, 6, makeField("💎 Valeur estimee", fmtInt(minerCtx.sinceLevelValue or 0) .. " k", true))
        table.insert(fields, 7, makeField("⚡ Rendement session", fmtRate(minerCtx.resourcesPerHour or 0) .. " /h", true))
        table.insert(fields, 8, makeField("💰 Valeur session", fmtInt(minerCtx.sessionValue or 0) .. " k", true))
        table.insert(fields, 9, makeField("🗺️ Mine actuelle", tostring(minerCtx.currentRoute or "?"), true))
        table.insert(fields, 10, makeField("🏆 Meilleure mine", tostring(minerCtx.bestRoute or "En apprentissage"), true))
    end

    local title = "⛏️ Niveau Mineur atteint !"
    if tonumber(jobId) ~= 24 then title = "🛠️ Niveau de metier atteint !" end
    local description = "**" .. info.name .. "** vient de passer **" .. tostring(jobName) .. " niveau " .. tostring(newLevel) .. "**."
    console(tostring(jobName) .. " : niveau " .. tostring(oldLevel) .. " -> " .. tostring(newLevel), "green")
    if C.LOCAL_NOTIFICATION_ON_LEVEL_UP then
        safeCall(function() return notify(tostring(jobName) .. " niveau " .. tostring(newLevel), true) end, false)
    end
    local sent = postEmbed(title, description, COLOR_JOB, fields, eventImage(jobId, info), pingContent(newLevel))
    if sent and tonumber(jobId) == 24 and type(Webhook.minerOnLevelSent) == "function" then
        safeCall(function() Webhook.minerOnLevelSent(oldLevel, newLevel) return true end, false)
    end
    return sent
end

local function sendCharacterLevel(oldLevel, newLevel)
    local info = charInfo()
    local fields = commonFields(info)
    table.insert(fields, 1, makeField("✨ Niveau personnage", "`" .. tostring(oldLevel) .. "` → **`" .. tostring(newLevel) .. "`**", false))

    local title = "✨ Niveau personnage !"
    local description = "**" .. info.name .. "** est maintenant **niveau " .. tostring(newLevel) .. "**."
    console("Personnage : niveau " .. tostring(oldLevel) .. " -> " .. tostring(newLevel), "green")
    if C.LOCAL_NOTIFICATION_ON_LEVEL_UP then
        safeCall(function() return notify(info.name .. " niveau " .. tostring(newLevel), true) end, false)
    end
    return postEmbed(title, description, COLOR_CHARACTER, fields, eventImage(nil, info), pingContent(newLevel))
end

local function sendStart()
    local info = charInfo()
    local fields = commonFields(info)
    local jobs = safeCall(function() return job:all() end, {})
    local jobParts = {}
    for id, lvl in pairs(jobs or {}) do
        jobParts[#jobParts + 1] = tostring(safeCall(function() return job:name(id) end, id)) .. " " .. tostring(lvl)
    end
    table.sort(jobParts)
    if #jobParts > 0 then
        fields[#fields + 1] = makeField("🧰 Metiers connus", table.concat(jobParts, " • "), false)
    end
    fields[#fields + 1] = makeField("▶️ Etat", "Script demarre", true)

    return postEmbed(
        "🚀 Script demarre",
        "Le suivi Discord est actif pour **" .. info.name .. "**.",
        COLOR_START,
        fields,
        eventImage(nil, info),
        ""
    )
end

function Webhook.onStopped(reason)
    if not C.EVENT_SCRIPT_STOP then return false end
    local info = charInfo()
    local fields = commonFields(info)
    table.insert(fields, 1, makeField("⏹️ Raison", tostring(reason or "inconnue"), true))
    table.insert(fields, 2, makeField("⏱️ Duree", fmtDuration(os.time() - sessionStartedAt), true))

    return postEmbed(
        "⏹️ Script arrete",
        "Le script de **" .. info.name .. "** s'est arrete.",
        COLOR_STOP,
        fields,
        eventImage(nil, info),
        ""
    )
end

local incidentLastSent = {}

function Webhook.sendIncident(kind, message, details)
    if not C.EVENT_INCIDENT then return false end
    kind = tostring(kind or "incident")
    local now = os.time()
    local cooldown = math.max(0, tonumber(C.INCIDENT_COOLDOWN_SEC) or 90)
    if incidentLastSent[kind] and (now - incidentLastSent[kind]) < cooldown then return false end
    incidentLastSent[kind] = now

    local info = charInfo()
    local fields = commonFields(info)
    table.insert(fields, 1, makeField("🚨 Incident", "**" .. kind .. "**", true))
    table.insert(fields, 2, makeField("🧭 Diagnostic", tostring(message or "?"), false))
    for _, d in ipairs(details or {}) do
        fields[#fields + 1] = makeField(d.name or "Detail", d.value or "?", d.inline == true)
    end
    return postEmbed(
        "🚨 Mineur AUTO — incident",
        "Une anomalie a declenche la logique de recovery.",
        COLOR_INCIDENT,
        fields,
        eventImage(nil, info),
        ""
    )
end

function Webhook.sendBankSummary(summary)
    if not C.EVENT_BANK_SUMMARY then return false end
    summary = summary or {}
    local info = charInfo()
    local fields = commonFields(info)
    table.insert(fields, 1, makeField("🏦 Banque", "Passage #" .. tostring(summary.bankCount or "?"), true))
    table.insert(fields, 2, makeField("⛏️ Depuis la derniere banque", fmtInt(summary.resources or 0), true))
    table.insert(fields, 3, makeField("💎 Valeur estimee", fmtInt(summary.value or 0) .. " k", true))
    table.insert(fields, 4, makeField("⚡ Rendement", fmtRate(summary.resourcesPerHour or 0) .. " /h", true))
    table.insert(fields, 5, makeField("⏱️ Duree", fmtDuration(summary.durationSec or 0), true))
    table.insert(fields, 6, makeField("🗺️ Route precedente", tostring(summary.previousRoute or "?"), true))
    table.insert(fields, 7, makeField("↩️ Reprise", tostring(summary.resumeRoute or "selection adaptative"), true))
    table.insert(fields, 8, makeField("🏆 Meilleure mine", tostring(summary.bestRoute or "En apprentissage"), true))
    if summary.topItems and summary.topItems ~= "" then
        table.insert(fields, 9, makeField("📦 Recolte detaillee", summary.topItems, false))
    end
    return postEmbed(
        "🏦 Banque terminee",
        "Le sac a ete vide et la session Mineur continue.",
        COLOR_BANK,
        fields,
        eventImage(nil, info),
        ""
    )
end

function Webhook.sendTest()
    local info = charInfo()
    local fields = commonFields(info)
    fields[#fields + 1] = makeField("✅ Test", "Le webhook fonctionne correctement.", false)
    return postEmbed(
        "✅ Test webhook Mizan",
        "Configuration Discord valide pour **" .. info.name .. "**.",
        COLOR_TEST,
        fields,
        eventImage(nil, info),
        ""
    )
end

function Webhook.listJobs()
    local jobs = safeCall(function() return job:all() end, {})
    printMessage("[Discord] Metiers detectes :", "info")
    for id, lvl in pairs(jobs or {}) do
        local name = safeCall(function() return job:name(id) end, "?")
        printMessage("  ID " .. tostring(id) .. " • " .. tostring(name) .. " • niveau " .. tostring(lvl), "grey")
    end
end

local function initializeState()
    local info = charInfo()
    if info.name == "?" or info.name == "" then return false end

    local jobs = safeCall(function() return job:all() end, {})
    for id, lvl in pairs(jobs or {}) do
        local numeric = tonumber(lvl) or 0
        lastJobs[id] = numeric

        local key = stateKey("job", id)
        local persisted = memGetNumber(key)
        if persisted ~= nil and C.NOTIFY_MISSED_LEVELS_ON_RESTART and numeric > persisted then
            local name = safeCall(function() return job:name(id) end, "")
            if contains(C.TRACK_JOBS, id, name) and isMilestone(C.JOB_MILESTONES, numeric) and C.EVENT_JOB_LEVEL_UP then
                sendJobLevel(id, persisted, numeric)
            end
        end
        memSetNumber(key, numeric)
    end

    lastCharacterLevel = info.level
    local charKey = stateKey("character", "level")
    local persistedChar = memGetNumber(charKey)
    if persistedChar ~= nil and C.NOTIFY_MISSED_LEVELS_ON_RESTART and info.level > persistedChar and C.EVENT_CHARACTER_LEVEL_UP then
        sendCharacterLevel(persistedChar, info.level)
    end
    memSetNumber(charKey, info.level)

    initialized = true
    console("Suivi initialise pour " .. info.name .. " (" .. tostring(info.server) .. ")", "green")

    if C.EVENT_SCRIPT_START and not startupSent then
        startupSent = true
        sendStart()
    end

    return true
end

function Webhook.tick()
    if not C.EVENT_JOB_LEVEL_UP and not C.EVENT_CHARACTER_LEVEL_UP and not C.EVENT_SCRIPT_START then return end

    local now = os.time()
    local interval = math.max(1, tonumber(C.CHECK_INTERVAL_SEC) or 1)
    if lastCheckAt ~= 0 and (now - lastCheckAt) < interval then return end
    lastCheckAt = now

    if not initialized then
        initializeState()
        return
    end

    local jobs = safeCall(function() return job:all() end, {})
    for id, currentLevel in pairs(jobs or {}) do
        local current = tonumber(currentLevel) or 0
        local previous = tonumber(lastJobs[id])
        local name = safeCall(function() return job:name(id) end, "")

        if previous == nil then
            lastJobs[id] = current
            memSetNumber(stateKey("job", id), current)
        elseif current > previous then
            lastJobs[id] = current
            memSetNumber(stateKey("job", id), current)
            if C.EVENT_JOB_LEVEL_UP and contains(C.TRACK_JOBS, id, name) and isMilestone(C.JOB_MILESTONES, current) then
                sendJobLevel(id, previous, current)
            end
        elseif current < previous then
            -- Donnee rafraichie / reconnexion : on rebascule simplement la baseline.
            lastJobs[id] = current
            memSetNumber(stateKey("job", id), current)
        end
    end

    if C.EVENT_CHARACTER_LEVEL_UP then
        local level = safeCall(function() return character:level() end, 0)
        level = tonumber(level) or 0
        if lastCharacterLevel == nil then
            lastCharacterLevel = level
        elseif level > lastCharacterLevel then
            local old = lastCharacterLevel
            lastCharacterLevel = level
            memSetNumber(stateKey("character", "level"), level)
            if isMilestone(C.CHARACTER_MILESTONES, level) then sendCharacterLevel(old, level) end
        elseif level > 0 and level < lastCharacterLevel then
            lastCharacterLevel = level
            memSetNumber(stateKey("character", "level"), level)
        end
    end
end

-- ======================= SCRIPT MINEUR =======================

-- MizanBot READY | Mineur 1-200 AUTO | API 2026-09
-- Niveau Mineur lu automatiquement via getJobLevel(24).
-- MINER_LEVEL_OVERRIDE > 0 sert uniquement pour un test manuel ponctuel.

MAX_PODS = 90
ELEMENTS_TO_GATHER = {}
MIN_MONSTERS = 1
MAX_MONSTERS = 8

MINER_LEVEL_OVERRIDE = 0
SAFE_RECOVERY = false
ROUTE_RANDOM = true
ECONOMY_MODE = false
SAME_MINE_PASSES = 3
MAX_WALK_ROTATION = 30
ALLOW_DISTANT_ROTATION = true
FORCE_ROUTE = ""
DISABLED_ROUTES = {}
MAX_FAILURES = 3
STUCK_TICKS = 5 -- legacy; le watchdog v2 utilise STUCK_IDLE_SEC

-- Adaptive Miner v2
ADAPTIVE_ROUTE_SELECTION = true
ADAPTIVE_EXPLORATION_RATE = 0.15       -- 15% des rotations testent une autre mine
ADAPTIVE_MIN_SAMPLES = 1              -- chaque mine admissible est testee au moins une fois
ADAPTIVE_VALUE_WEIGHT = 1.0           -- valeur/h ajoutee au score apres division par 10 000
ADAPTIVE_FAILURE_PENALTY = 120
ADAPTIVE_EMPTY_PENALTY = 80
ADAPTIVE_DISTANCE_PENALTY = 1.5
STUCK_IDLE_SEC = 45                   -- aucune progression map/cell/pods/recolte pendant 45 s
TELEMETRY_PERSIST = true
TELEMETRY_NAMESPACE = "miner_adaptive_v2"

local BANK_OUTSIDE = 191104002
local BANK_INSIDE = 192415750
local BANK_DOOR_CELL = 288

local ROUTES = {
    ["Yjupe"] = {
        entryMap=88212250, entryCell=248, entryWorldMap=88212250, entryTarget=97255955, hubMap=0,
        actionBudget=18, exitMap=88212250,
        exitNext={[97255955]=88212250,[97256979]=97255955,[97258003]=97256979,[97259027]=97258003,[97260051]=97259027,[97261075]=97259027},
        walkDistance={["Auderie"]=9,["Estrone"]=48,["GrandReseau"]=15,["Herale"]=27,["Hipouce"]=67,["Hurlement"]=39,["Kobalte"]=31},
        entryPos="", hasExit=true, surface={},
        actions={[97255955]={kind="cell",value=512},[97256979]={kind="cell",value=248},[97258003]={kind="cell",value=228},[97259027]={kind="randomcell",values={267,194}},[97261075]={kind="exit",target=88212250},[97260051]={kind="exit",target=88212250},},
    },
    ["Auderie"] = {
        entryMap=88213267, entryCell=236, entryWorldMap=88213267, entryTarget=97255949, hubMap=0,
        actionBudget=12, exitMap=88213267,
        exitNext={[97255949]=88213267,[97256973]=97255949,[97257997]=97256973,[97259021]=97257997},
        walkDistance={["Estrone"]=53,["GrandReseau"]=10,["Herale"]=22,["Hipouce"]=72,["Hurlement"]=44,["Kobalte"]=26,["Yjupe"]=9},
        entryPos="", hasExit=true, surface={},
        actions={[97255949]={kind="cell",value=376},[97256973]={kind="cell",value=122},[97257997]={kind="cell",value=235},[97259021]={kind="exit",target=88213267},},
    },
    ["Estrone"] = {
        entryMap=171966987, entryCell=397, entryWorldMap=171966987, entryTarget=178785286, hubMap=0,
        actionBudget=12, exitMap=171966987,
        exitNext={[178785286]=171966987,[178785288]=178785286},
        walkDistance={["Auderie"]=61,["GrandReseau"]=64,["Herale"]=54,["Hipouce"]=21,["Hurlement"]=17,["Kobalte"]=68,["Yjupe"]=56},
        entryPos="", hasExit=true, surface={},
        actions={[178785286]={kind="interactive",value=99},[178785288]={kind="exit",target=171966987},},
    },
    ["Hurlement"] = {
        entryMap=171707908, entryCell=166, entryWorldMap=171707908, entryTarget=178784264, hubMap=0,
        actionBudget=12, exitMap=171707908,
        exitNext={[178784264]=171707908},
        walkDistance={["Auderie"]=44,["Estrone"]=17,["GrandReseau"]=54,["Herale"]=66,["Hipouce"]=36,["Kobalte"]=70,["Yjupe"]=39},
        entryPos="", hasExit=true, surface={},
        actions={[178784264]={kind="exit",target=171707908},},
    },
    ["Saharach"] = {
        entryMap=173278720, entryCell=133, entryWorldMap=173278720, entryTarget=173935364, hubMap=0,
        actionBudget=15, exitMap=173278720,
        exitNext={[173277184]=173277696,[173277696]=173278208,[173278208]=173278720,[173539332]=173277184,[173935364]=173278720,[173936388]=173935364,[173937412]=173936388,[173938436]=173937412,[173939460]=173539332},
        walkDistance={},
        entryPos="", hasExit=true, surface={},
        actions={[173935364]={kind="interactive",value=297},[173936388]={kind="interactive",value=450},[173937412]={kind="interactive",value=382},[173938436]={kind="interactive",value=367},[173939460]={kind="exit",target=173278720},},
    },
    ["Herale"] = {
        entryMap=88082692, entryCell=332, entryWorldMap=88082692, entryTarget=97260033, hubMap=0,
        actionBudget=24, exitMap=88082692,
        exitNext={[97255939]=97261057,[97256963]=97255939,[97257987]=97261057,[97259011]=97261057,[97260033]=88082692,[97260035]=97257987,[97261057]=97260033,[97261059]=97260033},
        walkDistance={["Auderie"]=22,["Estrone"]=57,["GrandReseau"]=18,["Hipouce"]=76,["Hurlement"]=58,["Kobalte"]=16,["Yjupe"]=27},
        entryPos="", hasExit=false, surface={},
        actions={[97260033]={kind="cellprev",default=405,from={[97261057]=183}},[97261059]={kind="cell",value=417},[97261057]={kind="cellprev",default=227,from={[97260033]=421,[97259011]=235}},[97259011]={kind="cell",value=276},[97255939]={kind="cell",value=446},[97256963]={kind="cell",value=492},[97257987]={kind="cellprev",default=212,from={[97256963]=492}},[97260035]={kind="cell",value=288},},
    },
    ["Hipouce"] = {
        entryMap=173018629, entryCell=82, entryWorldMap=173018629, entryTarget=178784260, hubMap=0,
        actionBudget=33, exitMap=173018629,
        exitNext={[173017605]=173018117,[173017606]=173017605,[173018117]=173018629,[178782208]=173017606,[178782210]=178782208,[178782214]=178783236,[178782216]=178782214,[178782218]=178782216,[178782220]=178782218,[178783232]=178783236,[178783234]=178783232,[178783236]=178784260,[178784256]=178783232,[178784260]=173018629},
        walkDistance={["Auderie"]=49,["Estrone"]=21,["GrandReseau"]=45,["Herale"]=35,["Hurlement"]=36,["Kobalte"]=49,["Yjupe"]=54},
        entryPos="", hasExit=false, surface={},
        actions={[178784260]={kind="interactive",value=421},[178783236]={kind="interactiveprev",default=138,from={[178784260]=555,[178783232]=309}},[178783232]={kind="interactiveprev",default=200,from={[178783236]=204,[178784256]=406}},[178784256]={kind="interactive",value=505},[178782208]={kind="interactive",value=316},[178782210]={kind="interactive",value=207},[178783234]={kind="interactive",value=203},[178782214]={kind="interactiveprev",default=150,from={[178783236]=507}},[178782216]={kind="interactiveprev",default=122,from={[178782214]=422}},[178782218]={kind="interactiveprev",default=122,from={[178782216]=476}},[178782220]={kind="interactive",value=57},},
    },
    ["Kobalte"] = {
        entryMap=0, entryCell=403, entryWorldMap=88087305, entryTarget=117440512, hubMap=88085249,
        actionBudget=21, exitMap=88085249,
        exitNext={[88085250]=88085249,[88085251]=88085250,[88085763]=88085251,[88086275]=88085763,[88086276]=88086275,[88086277]=88086276,[88086789]=88086277,[88086790]=88086789,[88086791]=88086790,[88086792]=88086791,[88086793]=88086792,[88087305]=88086793,[117440512]=88087305,[117440514]=117443584,[117441536]=117440512,[117441538]=117440514,[117442560]=117441536,[117442562]=117441538,[117443584]=117442560},
        walkDistance={["Auderie"]=30,["Estrone"]=65,["GrandReseau"]=26,["Herale"]=8,["Hipouce"]=84,["Hurlement"]=66,["Yjupe"]=35},
        entryPos="14,14", hasExit=true, surface={["10,22"]="right",["11,22"]="right",["12,22"]="top",["12,21"]="right",["13,21"]="top",["13,20"]="top",["13,19"]="top",["13,18"]="top",["13,17"]="top",["13,16"]="top",["13,15"]="top",["13,14"]="right"},
        actions={[117440512]={kind="interactive",value=222},[117441536]={kind="interactive",value=167},[117442560]={kind="interactive",value=488},[117443584]={kind="interactive",value=221},[117440514]={kind="interactive",value=293},[117441538]={kind="interactive",value=251},[117442562]={kind="exit",target=88085249},},
    },
    ["GrandReseau"] = {
        entryMap=88213774, entryCell=353, entryWorldMap=88213774, entryTarget=97259013, hubMap=0,
        actionBudget=66, exitMap=88213774,
        exitNext={[88080644]=88080645,[88080645]=88080646,[88080646]=88080647,[88080647]=88080648,[88080648]=88080649,[88080649]=88080650,[88080650]=88080651,[88080651]=88080652,[88080652]=88080653,[88080653]=88080654,[88080654]=88212238,[88081156]=88080644,[88081668]=88081156,[88082180]=88081668,[88082692]=88082180,[88212238]=88212750,[88212750]=88213262,[88213262]=88213774,[97255943]=97261061,[97255945]=97261063,[97255947]=97261065,[97256967]=97259013,[97256969]=97255945,[97256971]=97255947,[97257991]=97260037,[97257993]=97260039,[97257995]=97256971,[97259013]=88213774,[97259015]=97261061,[97259017]=97261063,[97259019]=97261065,[97260033]=88082692,[97260037]=97259013,[97260039]=97256967,[97260041]=97255945,[97260043]=97259019,[97261061]=97260037,[97261063]=97260039,[97261065]=97257993,[97261067]=97256971},
        walkDistance={["Auderie"]=10,["Estrone"]=51,["Herale"]=18,["Hipouce"]=70,["Hurlement"]=52,["Kobalte"]=22,["Yjupe"]=15},
        entryPos="", hasExit=false, surface={},
        actions={[97259013]={kind="cellprev",default=258,from={[97260037]=276}},[97260033]={kind="cellprev",default=405,from={[97261057]=183}},[97260037]={kind="cellprev",default=430,from={[97259013]=303,[97257991]=352}},[97257991]={kind="cell",value=464},[97261061]={kind="cellprev",default=458,from={[97260037]=290,[97259015]=284}},[97259015]={kind="cell",value=451},[97255943]={kind="cell",value=403},[97256967]={kind="cellprev",default=518,from={[97259013]=194}},[97260039]={kind="cellprev",default=451,from={[97256967]=241,[97261063]=262}},[97261063]={kind="cellprev",default=459,from={[97260039]=331,[97259017]=296}},[97259017]={kind="cell",value=436},[97255945]={kind="cellprev",default=416,from={[97261063]=332,[97260041]=213}},[97260041]={kind="cell",value=354},[97256969]={kind="cell",value=401},[97257993]={kind="cellprev",default=537,from={[97260039]=122}},[97261065]={kind="cellprev",default=479,from={[97257993]=213,[97255947]=236}},[97255947]={kind="cellprev",default=500,from={[97261065]=199}},[97256971]={kind="cellprev",default=503,from={[97255947]=234,[97261067]=239}},[97261067]={kind="cell",value=521},[97257995]={kind="cell",value=374},[97259019]={kind="cellprev",default=438,from={[97261065]=276}},[97260043]={kind="cell",value=451},},
    },
}
local CELL_DESTINATIONS = {
    [97255939] = {[446]=97256963},
    [97255943] = {[403]=97261061},
    [97255945] = {[213]=97256969, [332]=97260041, [416]=97261063},
    [97255947] = {[199]=97256971, [500]=97261065},
    [97255949] = {[376]=97256973},
    [97255955] = {[512]=97256979},
    [97256963] = {[492]=97257987},
    [97256967] = {[194]=97260039, [518]=97259013},
    [97256969] = {[401]=97255945},
    [97256971] = {[234]=97261067, [239]=97257995, [503]=97255947},
    [97256973] = {[122]=97257997},
    [97256979] = {[248]=97258003},
    [97257987] = {[212]=97261057, [492]=97260035},
    [97257991] = {[464]=97260037},
    [97257993] = {[122]=97261065, [537]=97260039},
    [97257995] = {[374]=97256971},
    [97257997] = {[235]=97259021},
    [97258003] = {[228]=97259027},
    [97259011] = {[276]=97261057},
    [97259013] = {[258]=97260037, [276]=97256967},
    [97259015] = {[451]=97261061},
    [97259017] = {[436]=97261063},
    [97259019] = {[276]=97260043, [438]=97261065},
    [97259027] = {[194]=97260051, [267]=97261075},
    [97260033] = {[183]=97261059, [405]=97261057},
    [97260035] = {[288]=97257987},
    [97260037] = {[303]=97257991, [352]=97261061, [430]=97259013},
    [97260039] = {[241]=97261063, [262]=97257993, [451]=97256967},
    [97260041] = {[354]=97255945},
    [97260043] = {[451]=97259019},
    [97261057] = {[227]=97260033, [235]=97255939, [421]=97259011},
    [97261059] = {[417]=97260033},
    [97261061] = {[284]=97255943, [290]=97259015, [458]=97260037},
    [97261063] = {[296]=97255945, [331]=97259017, [459]=97260039},
    [97261065] = {[213]=97255947, [236]=97259019, [479]=97257993},
    [97261067] = {[521]=97256971},
}

local currentRoute = nil
local previousRoute = nil
local sameMinePasses = 0
local forceSwitch = false
local exitingRoute = nil
local lastMapId = -1
local failureCount = 0
local routeActionCount = 0
local sameSpot = 0
local seenMap = -1
local seenCell = -2
local MINER_LEVEL = 0
local levelWarning = false

-- Telemetrie session / apprentissage des routes.
local telemetryInitialized = false
local routeMetrics = {}
local routeRun = nil
local routeCooldown = {}
local sessionResources = 0
local sessionValue = 0
local sessionItems = {}
local priceCache = {}
local categoryCache = {}
local sessionTelemetryStartedAt = os.time()
local levelBaselineAt = os.time()
local levelBaselineResources = 0
local levelBaselineValue = 0
local bankBaselineAt = os.time()
local bankBaselineResources = 0
local bankBaselineValue = 0
local bankCount = 0
local bankCycle = false
local bankPending = nil
local bankResumeRoute = nil
local lastProgressAt = os.time()
local progressMap = -1
local progressCell = -2
local progressPods = -1
local progressResources = -1

local function tkey(routeName, field)
    return tostring(TELEMETRY_NAMESPACE) .. ".route." .. tostring(routeName) .. "." .. tostring(field)
end

local function memNumber(key, default)
    if not TELEMETRY_PERSIST then return default or 0 end
    return safeCall(function()
        local v = memory:get(key)
        if v == nil then return default or 0 end
        return tonumber(v) or (default or 0)
    end, default or 0)
end

local function memWrite(key, value)
    if not TELEMETRY_PERSIST then return end
    safeCall(function() return memory:set(key, tonumber(value) or 0) end, false)
end

local function ensureRouteMetric(name)
    if not name or name == "" then return nil end
    if routeMetrics[name] then return routeMetrics[name] end
    routeMetrics[name] = {
        runs = memNumber(tkey(name, "runs"), 0),
        seconds = memNumber(tkey(name, "seconds"), 0),
        resources = memNumber(tkey(name, "resources"), 0),
        value = memNumber(tkey(name, "value"), 0),
        failures = memNumber(tkey(name, "failures"), 0),
        emptyMaps = memNumber(tkey(name, "emptyMaps"), 0),
        mapsSeen = memNumber(tkey(name, "mapsSeen"), 0),
    }
    return routeMetrics[name]
end

local function persistRouteMetric(name)
    local m = routeMetrics[name]
    if not m then return end
    for _, field in ipairs({"runs", "seconds", "resources", "value", "failures", "emptyMaps", "mapsSeen"}) do
        memWrite(tkey(name, field), m[field] or 0)
    end
end

local function ensureTelemetry()
    if telemetryInitialized then return end
    for name, _ in pairs(ROUTES) do ensureRouteMetric(name) end
    bankCount = memNumber(tostring(TELEMETRY_NAMESPACE) .. ".bankCount", 0)
    telemetryInitialized = true
end

local function routeRates(name)
    local m = ensureRouteMetric(name)
    if not m or (m.seconds or 0) <= 0 then return 0, 0 end
    local h = m.seconds / 3600
    return (m.resources or 0) / h, (m.value or 0) / h
end

local function routeScore(name, fromRoute)
    local m = ensureRouteMetric(name)
    if not m then return -math.huge end
    local runs = tonumber(m.runs) or 0
    if runs < math.max(1, tonumber(ADAPTIVE_MIN_SAMPLES) or 1) then
        return 100000 - (runs * 1000)
    end
    local resourcesPerHour, valuePerHour = routeRates(name)
    local emptyRate = (m.mapsSeen or 0) > 0 and ((m.emptyMaps or 0) / m.mapsSeen) or 0
    local failureRate = runs > 0 and ((m.failures or 0) / runs) or 0
    local score = resourcesPerHour
        + ((valuePerHour / 10000) * (tonumber(ADAPTIVE_VALUE_WEIGHT) or 1))
        - (failureRate * (tonumber(ADAPTIVE_FAILURE_PENALTY) or 120))
        - (emptyRate * (tonumber(ADAPTIVE_EMPTY_PENALTY) or 80))

    if fromRoute and ROUTES[fromRoute] and ROUTES[fromRoute].walkDistance then
        local hops = tonumber(ROUTES[fromRoute].walkDistance[name])
        if hops then score = score - hops * (tonumber(ADAPTIVE_DISTANCE_PENALTY) or 1.5) end
    end
    return score
end

local function bestRouteName(pool, fromRoute)
    local bestName, bestScore = nil, -math.huge
    for _, name in ipairs(pool or {}) do
        if ROUTES[name] and not DISABLED_ROUTES[name] then
            local score = routeScore(name, fromRoute)
            if score > bestScore then bestName, bestScore = name, score end
        end
    end
    return bestName, bestScore
end

local function startRouteRun(name)
    if not name or name == "" then return end
    ensureTelemetry()
    if routeRun and routeRun.name == name then return end
    routeRun = {
        name = name,
        startedAt = os.time(),
        startResources = sessionResources,
        startValue = sessionValue,
        failures = 0,
        emptyMaps = 0,
        mapsSeen = 0,
    }
end

local function finalizeRouteRun(reason)
    if not routeRun then return end
    local name = routeRun.name
    local m = ensureRouteMetric(name)
    local duration = math.max(1, os.time() - (routeRun.startedAt or os.time()))
    local resources = math.max(0, sessionResources - (routeRun.startResources or sessionResources))
    local value = math.max(0, sessionValue - (routeRun.startValue or sessionValue))
    if reason == "exit" and resources == 0 and value == 0 then
        routeCooldown[name] = os.time() + 300
        printWarning("[Mineur AUTO] Route vide " .. tostring(name) .. " : repos 5 min avant nouvel essai")
    end
    m.runs = (m.runs or 0) + 1
    m.seconds = (m.seconds or 0) + duration
    m.resources = (m.resources or 0) + resources
    m.value = (m.value or 0) + value
    m.failures = (m.failures or 0) + (routeRun.failures or 0)
    m.emptyMaps = (m.emptyMaps or 0) + (routeRun.emptyMaps or 0)
    m.mapsSeen = (m.mapsSeen or 0) + (routeRun.mapsSeen or 0)
    persistRouteMetric(name)
    local rph = resources * 3600 / duration
    printMessage("[Mineur AUTO] Telemetry " .. tostring(name) .. " | " .. tostring(resources) .. " ressources | " .. tostring(math.floor(rph + 0.5)) .. "/h | " .. tostring(reason or "fin"), "info")
    routeRun = nil
end

local function inventorySnapshot()
    local snap = {}
    local items = safeCall(function() return inventory:items() end, {}) or {}
    for _, item in ipairs(items) do
        local gid = tonumber(item.gid)
        local qty = tonumber(item.qty) or 0
        if gid and gid > 0 and qty > 0 and item.equipped ~= true then
            snap[gid] = (snap[gid] or 0) + qty
        end
    end
    return snap
end

local function itemPrice(gid)
    gid = tonumber(gid) or 0
    if gid <= 0 then return 0 end
    if priceCache[gid] ~= nil then return priceCache[gid] end
    local p = safeCall(function() return inventory:getAveragePrice(gid) end, 0)
    p = math.max(0, tonumber(p) or 0)
    priceCache[gid] = p
    return p
end

local function itemName(gid)
    return tostring(safeCall(function() return data:itemName(gid) end, "GID " .. tostring(gid)))
end

local function trackedGatherAll()
    ensureTelemetry()
    if routeRun then routeRun.mapsSeen = (routeRun.mapsSeen or 0) + 1 end
    local resourcesOnMap = safeCall(function() return map:getResources() end, {}) or {}
    if #resourcesOnMap == 0 then
        if routeRun then routeRun.emptyMaps = (routeRun.emptyMaps or 0) + 1 end
        return false
    end

    local beforeStats = tonumber(safeCall(function() return stats:resources() end, 0)) or 0
    local before = inventorySnapshot()
    local okGather = gatherAll()
    local after = inventorySnapshot()
    local afterStats = tonumber(safeCall(function() return stats:resources() end, beforeStats)) or beforeStats
    local gathered = math.max(0, afterStats - beforeStats)
    local valueDelta = 0

    for gid, qty in pairs(after) do
        local delta = qty - (before[gid] or 0)
        if delta > 0 then
            sessionItems[gid] = (sessionItems[gid] or 0) + delta
            valueDelta = valueDelta + delta * itemPrice(gid)
        end
    end

    sessionResources = sessionResources + gathered
    sessionValue = sessionValue + valueDelta
    if gathered > 0 or valueDelta > 0 then lastProgressAt = os.time() end
    return okGather
end

local function topItemsSince(baseItems)
    local rows = {}
    for gid, qty in pairs(sessionItems) do
        local base = baseItems and (baseItems[gid] or 0) or 0
        local delta = qty - base
        if delta > 0 then rows[#rows + 1] = { gid = gid, qty = delta, value = delta * itemPrice(gid) } end
    end
    table.sort(rows, function(a, b) return (a.value or 0) > (b.value or 0) end)
    local out = {}
    for i = 1, math.min(5, #rows) do
        local row = rows[i]
        out[#out + 1] = "**" .. itemName(row.gid) .. "** ×" .. tostring(row.qty) .. " (~" .. fmtInt(row.value) .. " k)"
    end
    return table.concat(out, " • ")
end

local function cloneSessionItems()
    local t = {}
    for gid, qty in pairs(sessionItems) do t[gid] = qty end
    return t
end

local bankBaselineItems = {}

local function recordRouteFailure()
    if routeRun then routeRun.failures = (routeRun.failures or 0) + 1 end
end

local function incident(kind, message)
    Webhook.sendIncident(kind, message, {
        { name = "🗺️ Route", value = tostring(currentRoute or exitingRoute or "?"), inline = true },
        { name = "🧭 Map / cellule", value = tostring(getMapId()) .. " / " .. tostring(getCurrentCell()), inline = true },
        { name = "🔁 Echecs", value = tostring(failureCount) .. "/" .. tostring(MAX_FAILURES), inline = true },
    })
end

local function log(msg) printMessage("[Mineur AUTO] " .. tostring(msg), "info") end
local function warn(msg) printWarning("[Mineur AUTO] " .. tostring(msg)) end
local function err(msg) printError("[Mineur AUTO] " .. tostring(msg)) end
local function ok(msg) printSuccess("[Mineur AUTO] " .. tostring(msg)) end

local function escapeViaKnownZaap()
    if not zaap or not zaap.known or not zaap.travel then return false end
    local known = zaap:known() or {}
    for _, z in ipairs(known) do
        local mid = tonumber(z.mapId)
        if mid and mid > 0 and zaap:travel(mid) then
            warn("Recovery via zaap direct -> " .. tostring(mid))
            return true
        end
    end
    return false
end

local function refreshMinerLevel()
    local level = MINER_LEVEL_OVERRIDE > 0 and MINER_LEVEL_OVERRIDE or getJobLevel(24)
    level = tonumber(level) or 0
    if level <= 0 then
        if not levelWarning then warn("Niveau Mineur inconnu/non appris (jobId=24)") levelWarning = true end
        return false
    end
    levelWarning = false
    if level ~= MINER_LEVEL then
        local previous = MINER_LEVEL
        MINER_LEVEL = level
        if previous > 0 then
            finalizeRouteRun("level-up")
            currentRoute = nil
            exitingRoute = nil
            sameMinePasses = 0
            forceSwitch = false
            lastMapId = -1
            routeActionCount = 0
            ok("Niveau Mineur mis à jour automatiquement: " .. tostring(level))
        end
    end
    return true
end

local function poolForLevel(level)
    if level < 40 then
        return {"Herale", "Yjupe", "Auderie"}
    elseif level < 60 then
        return {"GrandReseau", "Herale", "Auderie"}
    elseif level < 100 then
        return {"GrandReseau", "Hipouce", "Kobalte"}
    elseif level < 160 then
        return {"GrandReseau", "Hipouce", "Kobalte", "Estrone", "Hurlement"}
    else
        return {"GrandReseau", "Hipouce", "Estrone", "Hurlement", "Saharach"}
    end
end

local function currentBestRoute()
    local pool = poolForLevel(MINER_LEVEL)
    local best = bestRouteName(pool, previousRoute)
    return best
end

Webhook.minerContextProvider = function()
    ensureTelemetry()
    local elapsed = math.max(1, os.time() - sessionTelemetryStartedAt)
    return {
        currentRoute = currentRoute or exitingRoute or previousRoute or "?",
        bestRoute = currentBestRoute() or "En apprentissage",
        sinceLevelSec = math.max(0, os.time() - levelBaselineAt),
        sinceLevelResources = math.max(0, sessionResources - levelBaselineResources),
        sinceLevelValue = math.max(0, sessionValue - levelBaselineValue),
        sessionResources = sessionResources,
        sessionValue = sessionValue,
        resourcesPerHour = sessionResources * 3600 / elapsed,
    }
end

Webhook.minerOnLevelSent = function()
    levelBaselineAt = os.time()
    levelBaselineResources = sessionResources
    levelBaselineValue = sessionValue
end

local function routeContainingMap(mapId)
    local eligible = poolForLevel(MINER_LEVEL)
    for _, name in ipairs(eligible) do
        local r = ROUTES[name]
        if r and (r.actions[mapId] ~= nil or (r.entryWorldMap > 0 and r.entryWorldMap == mapId)) then
            return name
        end
    end
    return nil
end

-- Reprise H24 : conserver les routes, temporiser les echecs et resynchroniser.
local recoveryPending = false
local combatSafetyConfigured = false
local function h24Pause()
    if isInFight() then return end
    if currentRoute then routeCooldown[currentRoute] = os.time() + 300 end
    warn("Reprise H24 : route en pause 5 min, resynchronisation dans 5s")
    finalizeRouteRun("recovery-h24")
    currentRoute=nil exitingRoute=nil previousRoute=nil forceSwitch=false
    failureCount=0 routeActionCount=0 lastMapId=-1
    sameSpot=0 seenMap=-1 seenCell=-2 lastProgressAt=os.time()
    leaveDialog()
    delay(5000)
    map:sendMapRequest()
end

local function chooseRoute()
    if FORCE_ROUTE ~= "" and ROUTES[FORCE_ROUTE] and not DISABLED_ROUTES[FORCE_ROUTE] then
        currentRoute = FORCE_ROUTE
        previousRoute = FORCE_ROUTE
        exitingRoute = nil
        lastMapId = -1
        routeActionCount = 0
        ok("Route forcée: " .. FORCE_ROUTE)
        return
    end
    local raw = poolForLevel(MINER_LEVEL)
    local pool = {}
    for _, name in ipairs(raw) do if not DISABLED_ROUTES[name] and (not routeCooldown[name] or os.time() >= routeCooldown[name]) then table.insert(pool, name) end end
    if #pool == 0 then
        warn("Aucune route disponible pour ce palier : attente sans deplacement")
        delay(15000)
        return
    end
    local idx = 1
    local chosen = nil
    if ADAPTIVE_ROUTE_SELECTION then
        ensureTelemetry()
        local explore = #pool > 1 and math.random() < math.max(0, math.min(1, tonumber(ADAPTIVE_EXPLORATION_RATE) or 0.15))
        if explore then
            idx = math.random(1, #pool)
            chosen = pool[idx]
            log("Adaptive: exploration -> " .. tostring(chosen))
        else
            chosen = bestRouteName(pool, previousRoute)
            if chosen then
                log("Adaptive: meilleur score -> " .. tostring(chosen) .. " (" .. string.format("%.1f", routeScore(chosen, previousRoute)) .. ")")
            end
        end
    end
    if not chosen then
        if ROUTE_RANDOM and #pool > 1 then idx = math.random(1, #pool) end
        chosen = pool[idx]
    end
    local previousAllowed = false
    for _, name in ipairs(pool) do
        if name == previousRoute then previousAllowed = true end
    end
    if ECONOMY_MODE and previousAllowed then
        if sameMinePasses < SAME_MINE_PASSES and not forceSwitch then
            chosen = previousRoute
            log("Économie zaap: passage " .. tostring(sameMinePasses + 1) .. "/" .. tostring(SAME_MINE_PASSES) .. " dans " .. chosen)
        else
            local nearby = ROUTES[previousRoute].walkDistance or {}
            local nearest = nil
            local best = math.huge
            for _, name in ipairs(pool) do
                local hops = nearby[name]
                if name ~= previousRoute and hops and hops < best then
                    best = hops
                    nearest = name
                end
            end
            if nearest and (best <= MAX_WALK_ROTATION or ALLOW_DISTANT_ROTATION) then
                chosen = nearest
                log("Économie zaap: mine proche " .. chosen .. " (" .. tostring(best) .. " cartes à pied)")
            elseif forceSwitch then
                err("Recovery: aucune autre mine proche; arrêt pour éviter une boucle/zaap coûteux.")
                h24Pause()
                return
            else
                chosen = previousRoute
                sameMinePasses = 0
                warn("Économie zaap: aucune mine proche, nouveau cycle sur " .. chosen)
            end
        end
    elseif (not ADAPTIVE_ROUTE_SELECTION) and chosen == previousRoute and #pool > 1 then
        idx = (idx % #pool) + 1
        chosen = pool[idx]
    end
    if chosen ~= previousRoute then sameMinePasses = 0 end
    previousRoute = chosen
    currentRoute = chosen
    exitingRoute = nil
    forceSwitch = false
    lastMapId = -1
    routeActionCount = 0
    startRouteRun(chosen)
    ok("Route: " .. chosen .. " | niveau Mineur=" .. tostring(MINER_LEVEL))
end

local function recover(label)
    failureCount = failureCount + 1
    recordRouteFailure()
    warn(label .. " | échec " .. tostring(failureCount) .. "/" .. tostring(MAX_FAILURES))
    -- Correctif 24/10 : la limite d'échecs s'applique TOUJOURS. Avec SAFE_RECOVERY=false, le compteur montait sans fin
    -- (« interactif cellule 203 | échec 28291/3 ») et le même déplacement refusé était renvoyé toutes les 200 ms.
    if failureCount < MAX_FAILURES then
        delay(math.min(5000, 500 * failureCount))   -- pause croissante au lieu de réessayer immédiatement
        return false
    end
    incident("recovery", tostring(label))
    failureCount = 0
    -- 1) Sortie progressive de la mine (avec récolte) puis autre route, sauf si on est DÉJÀ en train d'en sortir.
    if currentRoute and ROUTES[currentRoute] and exitingRoute ~= currentRoute then
        exitingRoute = currentRoute
        forceSwitch = true
        warn("Recovery: sortie progressive avec récolte de " .. tostring(currentRoute))
        return true
    end
    -- 2) Évasion par zaap : seulement si SAFE_RECOVERY l'autorise (coût en kamas).
    if SAFE_RECOVERY and escapeViaKnownZaap() then
        currentRoute = nil
        exitingRoute = nil
        lastMapId = -1
        chooseRoute()
        return true
    end
    -- 3) Rien ne fonctionne : arrêt propre plutôt qu'une boucle infinie.
    err("Recovery impossible (" .. tostring(label) .. (SAFE_RECOVERY and "" or ", SAFE_RECOVERY=false : pas d'évasion zaap")
        .. ") ; arrêt sécurisé pour éviter une boucle.")
    h24Pause()
    return true
end

local function stepExit()
    local name = exitingRoute
    local r = name and ROUTES[name] or nil
    if not r then
        err("Route de sortie inconnue: " .. tostring(name))
        exitingRoute = nil
        h24Pause()
        return true
    end
    local current = getMapId()
    trackedGatherAll()
    if current == r.exitMap then
        log("Sortie " .. tostring(name) .. " atteinte après récolte")
        if previousRoute == name then
            sameMinePasses = sameMinePasses + 1
        else
            sameMinePasses = 1
        end
        previousRoute = name
        finalizeRouteRun("exit")
        exitingRoute = nil
        currentRoute = nil
        lastMapId = -1
        routeActionCount = 0
        failureCount = 0
        chooseRoute()
        return true
    end
    local nextMap = r.exitNext[current]
    if not nextMap then
        warn("Sortie " .. tostring(name) .. ": aucun saut depuis " .. tostring(current))
        if escapeViaKnownZaap() then
            previousRoute = name
            exitingRoute = nil
            currentRoute = nil
            lastMapId = -1
            routeActionCount = 0
            failureCount = 0
            chooseRoute()
            return true
        end
        err("Sortie progressive impossible; arrêt sécurisé.")
        h24Pause()
        return true
    end
    log("Sortie " .. tostring(name) .. ": récolte puis " .. tostring(current) .. " -> " .. tostring(nextMap))
    local accepted = travelTo(nextMap)
    if accepted or getMapId() == nextMap then
        failureCount = 0
        return true
    end
    failureCount = failureCount + 1
    warn("Saut de sortie refusé (" .. tostring(failureCount) .. "/" .. tostring(MAX_FAILURES) .. ")")
    if failureCount >= MAX_FAILURES then
        if escapeViaKnownZaap() then
            previousRoute = name
            exitingRoute = nil
            currentRoute = nil
            lastMapId = -1
            routeActionCount = 0
            failureCount = 0
            chooseRoute()
        else
            err("Sortie progressive/zaap impossible; arrêt sécurisé.")
            h24Pause()
        end
    end
    return true
end

local function moveThroughCell(cell)
    local current = getMapId()
    local byCell = CELL_DESTINATIONS[current]
    local target = byCell and byCell[cell]
    if not target then
        return recover("sortie worldgraph inconnue map " .. tostring(current) .. " cellule " .. tostring(cell))
    end
    local accepted = travelTo(target)
    if accepted or getMapId() == target then
        failureCount = 0
        return true
    end
    return recover("transition " .. tostring(current) .. " -> " .. tostring(target) .. " cellule " .. tostring(cell))
end

local function useInteractive(cell)
    local before = getMapId()
    local used = goUseInteractive(cell)
    if used or getMapId() ~= before then failureCount = 0 return true end
    if recover("interactif cellule " .. tostring(cell)) then return true end
    return false
end

local function executeAction(a, previousMap)
    if a.kind == "exit" then
        exitingRoute = currentRoute
        return stepExit()
    end
    trackedGatherAll()
    if a.kind == "cell" then
        return moveThroughCell(a.value)
    elseif a.kind == "interactive" then
        return useInteractive(a.value)
    elseif a.kind == "randomcell" then
        return moveThroughCell(a.values[math.random(1,#a.values)])
    elseif a.kind == "cellprev" then
        return moveThroughCell((a.from and a.from[previousMap]) or a.default)
    elseif a.kind == "interactiveprev" then
        return useInteractive((a.from and a.from[previousMap]) or a.default)
    end
    return false
end

local function observeStuck()
    local now = os.time()
    local m = tonumber(getMapId()) or 0
    local c = tonumber(getCurrentCell()) or -1
    local pods = tonumber(getPods()) or -1
    local res = tonumber(safeCall(function() return stats:resources() end, 0)) or 0

    if m ~= progressMap or c ~= progressCell or pods ~= progressPods or res ~= progressResources then
        progressMap = m
        progressCell = c
        progressPods = pods
        progressResources = res
        lastProgressAt = now
        return false
    end

    local idle = now - lastProgressAt
    if idle >= math.max(15, tonumber(STUCK_IDLE_SEC) or 45) then
        lastProgressAt = now
        warn("Watchdog v2: aucune progression depuis " .. tostring(idle) .. " s")
        incident("stuck", "Aucune variation map/cell/pods/recoltes depuis " .. tostring(idle) .. " s")
        recover("stuck watchdog v2")
        return true
    end
    return false
end

local function routeStep(r, current, previous)
    local a = r.actions[current]
    if not a then return false end
    local routeBefore = currentRoute
    lastMapId = current
    local accepted = executeAction(a, previous)
    if accepted and currentRoute == routeBefore and not exitingRoute then
        routeActionCount = routeActionCount + 1
    end
    return true
end

function move()
    if getCharacterName() ~= "Minerbot-Vaincra" then printError("Identite inattendue") return false end
    if isInFight() then return end
    if not combatSafetyConfigured then
        combat:setChallengeMode("off")
        combat:setAutoSpells(false)
        fightBasic:clearSpells()
        fightBasic:addSpell(13528,0,1,2,false,100)
        fightBasic:addSpell(14396,0,1,1,false,100)
        combatSafetyConfigured = true
    end
    if isDead() then delay(2000) return end
    local hp = tonumber(getLifePointsPercent())
    if not hp or hp <= 0 then warn("PV inconnus : recolte suspendue") delay(15000) return end
    if hp < 90 then
        if not regenerate(90,180) then warn("Regeneration non confirmee : recolte suspendue") delay(15000) end
        return
    end
    if recoveryPending then
        recoveryPending=false
        h24Pause()
        return
    end
    if isDead() then delay(2000) return end
    if not refreshMinerLevel() then delay(1000) return end
    ensureTelemetry()
    Webhook.tick()
    if bankCycle and getPodsPercent() < MAX_PODS then
        bankCycle = false
        bankCount = bankCount + 1
        memWrite(tostring(TELEMETRY_NAMESPACE) .. ".bankCount", bankCount)
        local now = os.time()
        local elapsed = math.max(1, now - bankBaselineAt)
        local summaryResources = math.max(0, sessionResources - bankBaselineResources)
        local summaryValue = math.max(0, sessionValue - bankBaselineValue)
        local best = currentBestRoute()
        local detail = topItemsSince(bankBaselineItems)
        Webhook.sendBankSummary({
            bankCount = bankCount,
            resources = summaryResources,
            value = summaryValue,
            resourcesPerHour = summaryResources * 3600 / elapsed,
            durationSec = elapsed,
            previousRoute = bankPending and bankPending.previousRoute or bankResumeRoute,
            resumeRoute = bankResumeRoute or best,
            bestRoute = best,
            topItems = detail,
        })
        bankBaselineAt = now
        bankBaselineResources = sessionResources
        bankBaselineValue = sessionValue
        bankBaselineItems = cloneSessionItems()
        currentRoute = nil
        if bankResumeRoute and ROUTES[bankResumeRoute] and not DISABLED_ROUTES[bankResumeRoute] then
            local eligible = false
            for _, n in ipairs(poolForLevel(MINER_LEVEL)) do if n == bankResumeRoute then eligible = true break end end
            if eligible then
                currentRoute = bankResumeRoute
                previousRoute = bankResumeRoute
                startRouteRun(bankResumeRoute)
                log("Reprise apres banque -> " .. tostring(bankResumeRoute))
            end
        end
        bankPending = nil
        bankResumeRoute = nil
    end
    if observeStuck() then return end
    local current = getMapId()
    local previous = lastMapId

    if exitingRoute then
        stepExit()
        return
    end

    if not currentRoute then
        local detected = routeContainingMap(current)
        if detected and not DISABLED_ROUTES[detected] and (not routeCooldown[detected] or os.time() >= routeCooldown[detected]) then
            currentRoute = detected
            routeActionCount = 0
            startRouteRun(detected)
            ok("Route détectée: " .. detected)
        else
            chooseRoute()
        end
    end

    if not currentRoute then return end

    if currentRoute and not routeRun then startRouteRun(currentRoute) end

    local r = ROUTES[currentRoute]
    if not r then
        err("Route inconnue: " .. tostring(currentRoute))
        currentRoute = nil
        return
    end

    if routeActionCount >= (r.actionBudget or 24) then
        exitingRoute = currentRoute
        warn("Quota route atteint (" .. tostring(routeActionCount) .. "/" .. tostring(r.actionBudget or 24) .. ") -> sortie progressive avec récolte")
        stepExit()
        return
    end

    if routeStep(r, current, previous) then return end

    local pos = getCurrentPos()
    local dir = r.surface[pos]
    if dir then
        changeMap(dir)
        return
    end

    if (r.entryPos ~= "" and pos == r.entryPos) or (r.entryWorldMap and r.entryWorldMap > 0 and current == r.entryWorldMap) then
        log("Entrée " .. currentRoute .. " via worldgraph -> " .. tostring(r.entryTarget))
        if travelTo(r.entryTarget) or getMapId() == r.entryTarget then
            failureCount = 0
            return
        end
        recover("entrée worldgraph " .. tostring(currentRoute))
        return
    end

    if r.hubMap and r.hubMap > 0 then
        log("Voyage vers hub route " .. currentRoute)
        if not travelTo(r.hubMap) then recover("travelTo hub") end
        return
    end
    if r.entryMap and r.entryMap > 0 then
        log("Voyage vers entrée " .. currentRoute)
        if not travelTo(r.entryMap) then recover("travelTo entrée") end
        return
    end

    warn("Impossible de rejoindre " .. currentRoute .. ", rotation")
    finalizeRouteRun("unreachable")
    previousRoute = currentRoute
    currentRoute = nil
end

function bank()
    if getCharacterName() ~= "Minerbot-Vaincra" then printError("Identite inattendue") return false end
    if isInFight() then return end
    if isDead() then delay(2000) return end
    local hp = tonumber(getLifePointsPercent())
    if not hp or hp <= 0 then warn("PV inconnus : banque suspendue") delay(15000) return end
    if hp < 90 then
        if not regenerate(90,180) then warn("Regeneration non confirmee : banque suspendue") delay(15000) end
        return
    end
    ensureTelemetry()
    Webhook.tick()
    if not bankCycle then
        bankCycle = true
        bankResumeRoute = currentRoute or previousRoute
        bankPending = { previousRoute = currentRoute or previousRoute or "?" }
        finalizeRouteRun("bank")
        log("Banque: snapshot session | reprise prevue=" .. tostring(bankResumeRoute or "adaptive"))
    end

    local current = getMapId()
    if current == BANK_INSIDE then
        return { { map = tostring(BANK_INSIDE), npcBank = true, gather = true } }
    end

    if current ~= BANK_OUTSIDE then
        log("Pods pleins -> banque Astrub")
        if not travelTo(BANK_OUTSIDE) then recover("travelTo banque") end
        return
    end
    if not goUseInteractive(BANK_DOOR_CELL) then
        recover("porte banque Astrub")
    end
end

function stopped(reason)
    finalizeRouteRun("stopped:" .. tostring(reason or "?"))
    Webhook.onStopped(reason)
end

if type(printSuccess) == "function" then
    ok("Prêt | Adaptive Miner v2 | jobId=24 | routes apprenantes | webhook enrichi | seuil pods=" .. tostring(MAX_PODS) .. "%")
end

function onFightEnd(result)
    if not result or result.won ~= true then
        warn("Defaite ou resultat inconnu : arret pour verification (energie basse), sans relance automatique")
        stopScript()
    end
end
