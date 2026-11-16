using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using GivreClone.App.Services.Social;

namespace GivreClone.App.Services.LotM;

public sealed partial class GameDb
{
    public ISocialNames SocialNames() => new SocialCatalog(this);

    private sealed class SocialCatalog : ISocialNames
    {
        private readonly GameDb _db;
        public SocialCatalog(GameDb db) => _db = db;

        public string? Breed(int id) => TextOf("BreedData", id, "shortNameId");
        public string? Job(int id) => TextOf("JobData", id, "nameId");
        public string? Ornament(int id) => TextOf("OrnamentData", id, "nameId");
        public string? AlignmentRank(int rankId) => TextOf("AlignmentRankData", rankId, "nameId");
        public string? Monster(int id) => TextOf("MonsterData", id, "nameId");

        public IReadOnlyList<int> JobIds()
        {
            try
            {
                var ids = new List<int>();
                lock (ExpeditionGate)
                {
                    foreach (var row in _db.Scan("JobData"))
                    {
                        if (!row.TryGetValue("id", out var raw) || raw is null) continue;
                        int id = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                        if (id != 1) ids.Add(id);
                    }
                }
                ids.Sort();
                return ids;
            }
            catch { return Array.Empty<int>(); }
        }

        public string? Title(int id)
        {
            try
            {
                lock (ExpeditionGate)
                {
                    var row = _db.Row("TitleData", id.ToString(CultureInfo.InvariantCulture));
                    if (row is null) return null;
                    string? male = Name(row, "nameMaleId");
                    string? female = Name(row, "nameFemaleId");
                    if (string.IsNullOrEmpty(male)) return string.IsNullOrEmpty(female) ? null : female;
                    if (string.IsNullOrEmpty(female) || female == male) return male;
                    return male + " / " + female;
                }
            }
            catch { return null; }
        }

        public string? AlignmentSideOfRank(int rankId)
        {
            try
            {
                lock (ExpeditionGate)
                {
                    var rank = _db.Row("AlignmentRankData", rankId.ToString(CultureInfo.InvariantCulture));
                    if (rank is null || !rank.TryGetValue("orderId", out var orderRaw) || orderRaw is null) return null;
                    var order = _db.Row("AlignmentOrderData", Convert.ToInt32(orderRaw, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                    if (order is null || !order.TryGetValue("sideId", out var sideRaw) || sideRaw is null) return null;
                    var side = _db.Row("AlignmentSideData", Convert.ToInt32(sideRaw, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                    return side is null ? null : Name(side, "nameId");
                }
            }
            catch { return null; }
        }

        public string? ItemName(long gid) => ItemColumn(gid, "nameId", text: true);
        public string? ItemType(long gid)
        {
            try
            {
                lock (ExpeditionGate)
                {
                    var row = _db.Row("ItemData", gid.ToString(CultureInfo.InvariantCulture));
                    if (row is null || !row.TryGetValue("typeId", out var type) || type is null) return null;
                    long typeId = Convert.ToInt64(type, CultureInfo.InvariantCulture);
                    if (typeId <= 0) return null;
                    var typeRow = _db.Row("ItemTypeData", typeId.ToString(CultureInfo.InvariantCulture));
                    return typeRow is null ? null : Name(typeRow, "nameId");
                }
            }
            catch { return null; }
        }

        public string? ItemSet(long gid)
        {
            try
            {
                lock (ExpeditionGate)
                {
                    var row = _db.Row("ItemData", gid.ToString(CultureInfo.InvariantCulture));
                    if (row is null || !row.TryGetValue("itemSetId", out var set) || set is null) return null;
                    long setId = Convert.ToInt64(set, CultureInfo.InvariantCulture);
                    if (setId <= 0) return null;
                    var setRow = _db.Row("ItemSetData", setId.ToString(CultureInfo.InvariantCulture));
                    return setRow is null ? null : Name(setRow, "nameId");
                }
            }
            catch { return null; }
        }

        public int ItemLevel(long gid) => ItemInt(gid, "level");
        public int ItemWeight(long gid) => ItemInt(gid, "realWeight");

        public string? ItemDescription(long gid)
        {
            string? text = ItemColumn(gid, "descriptionId", text: true);
            return text is null ? null : HtmlText.Strip(text);
        }

        public long? MonsterXp(int monsterId, int level)
        {
            try
            {
                lock (ExpeditionGate)
                {
                    var row = _db.Row("MonsterData", monsterId.ToString(CultureInfo.InvariantCulture));
                    if (row is null || !row.TryGetValue("grades", out var raw) || raw is not string json) return null;
                    using var doc = JsonDocument.Parse(json);
                    foreach (var grade in doc.RootElement.EnumerateArray())
                    {
                        if (!grade.TryGetProperty("level", out var lv) || !lv.TryGetInt32(out int gradeLevel) || gradeLevel != level)
                            continue;
                        if (grade.TryGetProperty("xp", out var xp) && xp.TryGetInt64(out long points)) return points;
                        return null;
                    }
                    return null;
                }
            }
            catch { return null; }
        }

        private string? TextOf(string table, int id, string column)
        {
            try
            {
                lock (ExpeditionGate)
                {
                    var row = _db.Row(table, id.ToString(CultureInfo.InvariantCulture));
                    return row is null ? null : Name(row, column);
                }
            }
            catch { return null; }
        }

        private string? Name(IReadOnlyDictionary<string, object?> row, string column)
        {
            if (!row.TryGetValue(column, out var raw) || raw is null) return null;
            long nameId = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
            return _db.Text(nameId);
        }

        private string? ItemColumn(long gid, string column, bool text)
        {
            try
            {
                lock (ExpeditionGate)
                {
                    var row = _db.Row("ItemData", gid.ToString(CultureInfo.InvariantCulture));
                    if (row is null) return null;
                    return text ? Name(row, column) : null;
                }
            }
            catch { return null; }
        }

        private int ItemInt(long gid, string column)
        {
            try
            {
                lock (ExpeditionGate)
                {
                    var row = _db.Row("ItemData", gid.ToString(CultureInfo.InvariantCulture));
                    if (row is null || !row.TryGetValue(column, out var raw) || raw is null) return -1;
                    return Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                }
            }
            catch { return -1; }
        }
    }
}
