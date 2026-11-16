using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Lot B (ponts moteur Lua) : primitives PURES testables sans l'App (choix de skill, case d'interaction,
/// niveaux des monstres) + résolution PAR NOM des messages/champs utilisés (survit à un patch : pas de codes littéraux).</summary>
public class LotBTests
{
    private static string DataDir => Path.Combine(AppContext.BaseDirectory, "data");

    [Fact]
    public void MapInteractive_PickSkill()
    {
        var a = new InteractiveSkill(900, 102, 0, true);
        var b = new InteractiveSkill(901, 339, 0, true);
        var c = new InteractiveSkill(902, 45, 0, false);
        var it = new MapInteractive(100, 5000, 1, 0, new[] { a, b, c });

        Assert.Equal(a, it.PickSkill(0));    // défaut = HarvestSkill (1er actif non générique)
        Assert.Equal(a, it.PickSkill(-1));   // -N = N-ième skill ACTIF
        Assert.Equal(b, it.PickSkill(-2));
        Assert.Null(it.PickSkill(-3));       // seulement 2 actifs
        Assert.Equal(b, it.PickSkill(901));  // uid d'instance
        Assert.Equal(b, it.PickSkill(339));  // repli skillId
        Assert.Null(it.PickSkill(902));      // désactivé
    }

    [Fact]
    public void MapPathfinder_StandCellFor()
    {
        static bool All(int _) => true;
        const int target = 310;
        var (tx, ty) = MapPathfinder.MapPointOf(target);
        int adj = MapPathfinder.CellFromMapPoint(tx + 1, ty);
        Assert.True(adj >= 0);
        Assert.Equal(1, MapPathfinder.CellDistance(adj, target));

        // Source déjà voisine -> elle (pas de déplacement) ; une case « canonique » LOINTAINE (250 : élément d'une autre
        // carte / ancrage sans rapport) est IGNORÉE (avant : le bot y marchait).
        Assert.Equal(adj, MapPathfinder.StandCellFor(adj, target, 1, 250, All, null));
        Assert.Equal(adj, MapPathfinder.StandCellFor(adj, target, 1, 250, c => c != 250, null));
        // Portée 10 (pêche) : canonique ignorée, source déjà à portée.
        Assert.Equal(adj, MapPathfinder.StandCellFor(adj, target, 10, 250, All, null));
        // Rien de marchable hors source, source hors portée -> injoignable.
        Assert.True(MapPathfinder.CellDistance(0, target) > 1);
        Assert.Null(MapPathfinder.StandCellFor(0, target, 1, 250, c => c == 0, null));
    }

    // ---------------------------------------------------------------- voisinage d'interaction (8 voisines)

    private static void AssertNeighbours(int cell, int[] ortho, int[] diag)
    {
        var n = MapPathfinder.InteractionNeighbours(cell);
        Assert.Equal(ortho.Length + diag.Length, n.Count);
        Assert.Equal(ortho.OrderBy(c => c), n.Take(ortho.Length).OrderBy(c => c));   // orthogonales D'ABORD
        Assert.Equal(diag.OrderBy(c => c), n.Skip(ortho.Length).OrderBy(c => c));    // puis diagonales
        foreach (var c in n) Assert.True(MapPathfinder.InteractionAdjacent(cell, c), $"{c} voisine de {cell}");
        foreach (var c in ortho) Assert.Equal(1, MapPathfinder.CellDistance(cell, c));  // orthogonale = diamant 1
        foreach (var c in diag) Assert.Equal(2, MapPathfinder.CellDistance(cell, c));   // diagonale = diamant 2
    }

    [Fact]
    public void InteractionNeighbours_row_parity()
    {
        // Ligne IMPAIRE (352 = ligne 25) : orthogonales -14/-13/+14/+15, diagonales ±1 / ±28 (380 = la case du vrai client).
        AssertNeighbours(352, new[] { 338, 339, 366, 367 }, new[] { 351, 353, 324, 380 });
        // Ligne PAIRE (310 = ligne 22) : orthogonales -15/-14/+13/+14, diagonales ±1 / ±28.
        AssertNeighbours(310, new[] { 295, 296, 323, 324 }, new[] { 309, 311, 282, 338 });
    }

    [Fact]
    public void InteractionNeighbours_map_edges()
    {
        AssertNeighbours(0, new[] { 14 }, new[] { 1, 28 });                   // coin haut-gauche : 13 (fin de ligne) exclue
        AssertNeighbours(13, new[] { 26, 27 }, new[] { 12, 41 });             // fin de ligne 0 : 14 (ligne suivante) exclue
        AssertNeighbours(14, new[] { 0, 1, 28, 29 }, new[] { 15, 42 });       // début de ligne impaire : 13 exclue
        AssertNeighbours(27, new[] { 13, 41 }, new[] { 26, 55 });             // fin de ligne impaire : 14/42 exclues
        AssertNeighbours(546, new[] { 532, 533 }, new[] { 547, 518 });       // début de la dernière ligne : 545 exclue
        AssertNeighbours(559, new[] { 545 }, new[] { 558, 531 });             // coin bas-droit : 546 exclue
        Assert.Empty(MapPathfinder.InteractionNeighbours(-1));
        Assert.Empty(MapPathfinder.InteractionNeighbours(MapPathfinder.CellCount));
        Assert.False(MapPathfinder.InteractionAdjacent(352, 352));            // jamais sa propre voisine
        Assert.False(MapPathfinder.InteractionAdjacent(0, 13));
        Assert.False(MapPathfinder.InteractionAdjacent(13, 14));
    }

    [Fact]
    public void InteractionAdjacent_matches_pathfinder_8_connectivity()
    {
        // Le voisinage d'interaction (MapPoint) coïncide EXACTEMENT avec un pas du pathfinder 8-connexe (repère interne,
        // formule indépendante) : aucune voisine inventée ni oubliée, parité et bords compris ; relation symétrique.
        for (int a = 0; a < MapPathfinder.CellCount; a++)
        {
            var n = MapPathfinder.InteractionNeighbours(a);
            int count = 0;
            for (int b = 0; b < MapPathfinder.CellCount; b++)
            {
                bool adj = MapPathfinder.InteractionAdjacent(a, b);
                Assert.Equal(a != b && MapPathfinder.Distance(a, b) == 1, adj);
                Assert.Equal(adj, MapPathfinder.InteractionAdjacent(b, a));
                if (adj) { count++; Assert.Contains(b, n); }
            }
            Assert.Equal(count, n.Count);
            Assert.Equal(n.Count, n.Distinct().Count());
        }
    }

    [Fact]
    public void StandCellFor_R3_door_reachable_only_by_diagonal()
    {
        // Carte 206177286, porte 352 : cellule non marchable, 7 voisines bloquées, seule 380 (= 352+28, diagonale) marchable.
        // La cellule mapData de l'élément (« canonique ») = 352 elle-même -> ignorée.
        var blocked = new HashSet<int> { 352, 338, 339, 366, 367, 351, 353, 324 };
        bool W(int c) => !blocked.Contains(c);
        Assert.Equal(380, MapPathfinder.StandCellFor(380, 352, 1, 352, W, null));   // déjà sur 380 -> pas de déplacement
        Assert.Equal(380, MapPathfinder.StandCellFor(381, 352, 1, 352, W, null));
        Assert.Equal(380, MapPathfinder.StandCellFor(394, 352, 1, 352, W, null));
        Assert.Equal(380, MapPathfinder.StandCellFor(100, 352, 1, null, W, null));

        // Couloir réaliste : seules 380/381/394/395/408/409 sont marchables.
        var corridor = new HashSet<int> { 380, 381, 394, 395, 408, 409 };
        Assert.Equal(380, MapPathfinder.StandCellFor(409, 352, 1, 352, corridor.Contains, null));
        Assert.Equal(380, MapPathfinder.StandCellFor(381, 352, 1, 352, corridor.Contains, null));

        // Prédicat de marche : la porte n'étant PAS marchable, l'exclure ne change rien -> portes inchangées (même chemin).
        Assert.Equal(380, MapPathfinder.StandCellFor(100, 352, 1, 352, W, null, out var mw));
        for (int c = 0; c < MapPathfinder.CellCount; c++) Assert.Equal(W(c), mw(c));
        Assert.Equal(MapPathfinder.KeyMovements(100, 380, W), MapPathfinder.KeyMovements(100, 380, mw));

        // Ordre des candidates depuis 380 : orthogonales (triées par distance à la source, tri stable) puis diagonales.
        Assert.Equal(new[] { 367, 366, 338, 339, 380, 353, 351, 324 },
                     MapPathfinder.InteractionStandCandidates(380, 352, 352));
    }

    [Fact]
    public void StandCellFor_R2_never_stands_on_the_resource()
    {
        static bool All(int _) => true;
        const int plant = 310;   // plante sur case MARCHABLE (Ortie, Sauge, Blé…)
        int[] ortho = { 295, 296, 323, 324 };
        int[] diag = { 309, 311, 282, 338 };

        // Canonique = la cellule de la plante (cas réel) -> ignorée : on se place sur une ORTHOGONALE, jamais sur la plante.
        int? far = MapPathfinder.StandCellFor(100, plant, 1, plant, All, null);
        Assert.NotNull(far);
        Assert.NotEqual(plant, far);
        Assert.Contains(far!.Value, ortho);
        // Le perso est SUR la plante -> il en descend vers une orthogonale.
        int? onIt = MapPathfinder.StandCellFor(plant, plant, 1, plant, All, null);
        Assert.NotNull(onIt);
        Assert.Contains(onIt!.Value, ortho);
        // Déjà voisin en DIAGONALE -> reste sur place (comme le vrai client).
        Assert.Equal(309, MapPathfinder.StandCellFor(309, plant, 1, plant, All, null));
        // Orthogonales toutes bloquées -> une diagonale (avant : injoignable).
        int? viaDiag = MapPathfinder.StandCellFor(100, plant, 1, null, c => Array.IndexOf(ortho, c) < 0, null);
        Assert.NotNull(viaDiag);
        Assert.Contains(viaDiag!.Value, diag);
        // Dernier recours : SUR la plante et aucune voisine marchable -> on reste (le serveur accepte l'usage sur place).
        Assert.Equal(plant, MapPathfinder.StandCellFor(plant, plant, 1, plant, c => c == plant, null));
        // Jamais la cible parmi les candidates.
        Assert.DoesNotContain(plant, MapPathfinder.InteractionStandCandidates(100, plant, plant));
    }

    [Fact]
    public void StandCellFor_canonical_only_when_real_stand_cell()
    {
        static bool All(int _) => true;
        const int target = 310;
        // Canonique DISTINCTE et VOISINE (même diagonale) -> 1re candidate : elle gagne les ÉGALITÉS de chemin (perso sur la
        // cible : toutes les voisines à 1 pas) mais plus un chemin plus long (depuis 100 : 338 à 9 pas, 296 à 7).
        Assert.Equal(338, MapPathfinder.InteractionStandCandidates(100, target, 338)[0]);
        Assert.Equal(338, MapPathfinder.StandCellFor(target, target, 1, 338, All, null));
        Assert.Equal(296, MapPathfinder.StandCellFor(100, target, 1, 338, All, null));
        // Canonique = la cible -> ignorée ; canonique lointaine -> ignorée.
        Assert.DoesNotContain(target, MapPathfinder.InteractionStandCandidates(100, target, target));
        Assert.DoesNotContain(250, MapPathfinder.InteractionStandCandidates(100, target, 250));
        Assert.Equal(8, MapPathfinder.InteractionStandCandidates(100, target, 250).Count);
        // Canonique voisine mais non marchable -> repli sur les autres voisines.
        int? r = MapPathfinder.StandCellFor(100, target, 1, 338, c => c != 338, null);
        Assert.NotNull(r);
        Assert.NotEqual(338, r);
        Assert.True(MapPathfinder.InteractionAdjacent(r!.Value, target));
    }

    // Chemin DÉROULÉ (cellule par cellule, source comprise) que le déplacement réel suivrait avec ce prédicat ; vide si aucun.
    private static IReadOnlyList<int> Walk(int from, int to, Func<int, bool> walk)
        => MapPathfinder.ExpandPath(MapPathfinder.KeyMovements(from, to, walk, allowDiag: true, null).Select(k => k & 0xFFF).ToList());

    // Nombre de pas de ce chemin, -1 si aucun.
    private static int Steps(int from, int to, Func<int, bool> walk) => Walk(from, to, walk).Count - 1;

    [Fact]
    public void StandCellFor_ortie_337_never_walks_through_the_plant()
    {
        // Cas réel (transport-2026-09-24_16-54-18.log 17:10:01, carte 153878786) : Ortie (élément 489697) sur la case
        // MARCHABLE 337, bot en 212. Couloir synthétique = chemin du log + un coin marchable par pas diagonal (règle du coin).
        const int plant = 337, src = 212;
        var open = new HashSet<int> { 212, 240, 255, 269, 284, 312, 325, 324, 338, plant, 322, 227, 298, 311, 351 };
        bool W(int c) => open.Contains(c);

        // AVANT : 1re orthogonale joignable = 322, rejointe PAR la plante (A* sur W brut) -> le bot marchait sur l'Ortie.
        Assert.Equal(322, MapPathfinder.InteractionStandCandidates(src, plant, plant)[0]);
        Assert.Contains(plant, Walk(src, 322, W));

        // APRÈS : voisine au chemin le plus court qui CONTOURNE la plante = 338 (diagonale, 8 pas), prédicat sans 337.
        Assert.Equal(338, MapPathfinder.StandCellFor(src, plant, 1, plant, W, null, out var mw));
        Assert.False(mw(plant));
        Assert.True(mw(338));
        var path = Walk(src, 338, mw);
        Assert.DoesNotContain(plant, path);
        Assert.Equal(8, path.Count - 1);
        Assert.Empty(MapPathfinder.KeyMovements(src, 322, mw, allowDiag: true, null));   // 322 : seulement par la plante
    }

    [Fact]
    public void StandCellFor_fewest_steps_then_candidate_order()
    {
        static bool All(int _) => true;
        // Diagonale STRICTEMENT plus courte (4 pas) que toute orthogonale (>= 5) -> la diagonale (avant : une orthogonale).
        Assert.Equal(4, Steps(342, 338, All));
        foreach (int o in new[] { 322, 323, 350, 351 }) Assert.True(Steps(342, o, All) >= 5, $"orthogonale {o}");
        Assert.Equal(338, MapPathfinder.StandCellFor(342, 337, 1, null, All, null));
        // Égalité orthogonale / diagonale (296 et 282 à 7 pas depuis 100) -> l'orthogonale (ordre des candidates).
        Assert.Equal(7, Steps(100, 296, All));
        Assert.Equal(7, Steps(100, 282, All));
        Assert.Equal(296, MapPathfinder.StandCellFor(100, 310, 1, null, All, null));
        // Perso SUR la plante : toutes les voisines à 1 pas -> 1re candidate (orthogonale).
        Assert.Equal(MapPathfinder.InteractionStandCandidates(310, 310, null)[0], MapPathfinder.StandCellFor(310, 310, 1, null, All, null));
    }

    [Fact]
    public void StandCellFor_only_access_past_the_plant_stops_before_it()
    {
        static int C(int x, int y) { int c = MapPathfinder.CellFromMapPoint(x, y); Assert.True(c >= 0, $"({x},{y}) hors grille"); return c; }
        // Couloir en ligne droite (MapPoint Y = -11) : source … entrée | PLANTE 337 | au-delà. Tout accès à la voisine
        // « au-delà » traverse la plante.
        const int plant = 337;
        Assert.Equal(plant, C(13, -11));
        int entry = C(14, -11), beyond = C(12, -11), src = C(18, -11);
        var open = new HashSet<int> { plant, beyond, C(11, -11) };
        for (int x = 14; x <= 18; x++) open.Add(C(x, -11));
        bool W(int c) => open.Contains(c);

        // Canonique = la voisine AU-DELÀ (1re candidate de l'ancien ordre) : avant, le bot la rejoignait EN TRAVERSANT la
        // plante. Maintenant : la voisine d'ENTRÉE, chemin sans la plante ; l'au-delà est injoignable sans la traverser.
        Assert.Equal(beyond, MapPathfinder.InteractionStandCandidates(src, plant, beyond)[0]);
        Assert.Contains(plant, Walk(src, beyond, W));
        Assert.Equal(entry, MapPathfinder.StandCellFor(src, plant, 1, beyond, W, null, out var mw));
        Assert.DoesNotContain(plant, Walk(src, entry, mw));
        Assert.Empty(MapPathfinder.KeyMovements(src, beyond, mw, allowDiag: true, null));

        // Perso SUR la plante : les deux côtés à 1 pas -> la canonique (1re candidate) ; partir de la plante reste permis.
        Assert.Equal(beyond, MapPathfinder.StandCellFor(plant, plant, 1, beyond, W, null, out var mw2));
        Assert.NotEmpty(MapPathfinder.KeyMovements(plant, beyond, mw2, allowDiag: true, null));

        // Dernier recours (c) : SUR la plante sans voisine marchable -> on reste ; prédicat brut (aucun déplacement).
        Assert.Equal(plant, MapPathfinder.StandCellFor(plant, plant, 1, null, c => c == plant, null, out var mw3));
        Assert.True(mw3(plant));
    }

    [Fact]
    public void StandCellFor_property_never_crosses_target_and_is_shortest()
    {
        // Cartes aléatoires (graine fixe) : la case rendue est une voisine, rejointe SANS traverser la cible, au chemin le plus
        // court parmi les voisines joignables ; dès que l'ancienne sélection (W brut) trouvait une voisine, la nouvelle aussi
        // (le repli brut n'est jamais nécessaire : tout chemin par la cible passe d'abord par l'une de ses 8 voisines).
        var rng = new Random(20260926);
        for (int iter = 0; iter < 300; iter++)
        {
            var open = new bool[MapPathfinder.CellCount];
            for (int c = 0; c < open.Length; c++) open[c] = rng.NextDouble() < 0.7;
            bool W(int c) => (uint)c < (uint)open.Length && open[c];
            int target = rng.Next(MapPathfinder.CellCount);
            int src;
            if (rng.Next(10) == 0) src = target;
            else do src = rng.Next(MapPathfinder.CellCount); while (!open[src]);
            var nb = MapPathfinder.InteractionNeighbours(target);
            int? canon = rng.Next(3) switch { 0 => nb[rng.Next(nb.Count)], 1 => target, _ => null };

            int? r = MapPathfinder.StandCellFor(src, target, 1, canon, W, null, out var mw);
            if (MapPathfinder.InteractionAdjacent(src, target)) { Assert.Equal(src, r); continue; }
            var cands = MapPathfinder.InteractionStandCandidates(src, target, canon);
            bool oldReach = cands.Any(c => c != src && W(c) && Steps(src, c, W) > 0);
            if (!oldReach) { Assert.Equal(src == target ? src : (int?)null, r); continue; }

            Assert.NotNull(r);
            int s = r!.Value;
            Assert.NotEqual(target, s);
            Assert.True(MapPathfinder.InteractionAdjacent(s, target));
            Assert.False(mw(target));
            var path = Walk(src, s, mw);
            Assert.True(path.Count >= 2, $"iter {iter} : {src}->{s} sans chemin");
            Assert.DoesNotContain(target, path.Skip(1));   // la source peut être la cible (départ permis)
            int best = path.Count - 1;
            foreach (var c in cands)
            {
                if (c == s || !W(c)) continue;
                int k = Steps(src, c, mw);
                if (k > 0) Assert.True(best <= k, $"iter {iter} : {s} ({best} pas) plus long que {c} ({k} pas)");
            }
        }
    }

    [Fact]
    public void StandCellFor_range_above_one_unchanged()
    {
        static bool All(int _) => true;
        const int fish = 310;
        // Source à <= 10 (diamant) -> sur place ; SUR la cellule -> sur place (comportement historique de la pêche).
        int near = MapPathfinder.CellFromMapPoint(MapPathfinder.MapPointOf(fish).X + 3, MapPathfinder.MapPointOf(fish).Y + 3);
        Assert.Equal(6, MapPathfinder.CellDistance(near, fish));
        Assert.Equal(near, MapPathfinder.StandCellFor(near, fish, 10, 250, All, null));
        Assert.Equal(fish, MapPathfinder.StandCellFor(fish, fish, 10, null, All, null));
        // Hors portée -> 1re case à portée joignable (celle de StandCellsInRange, la plus proche de la source).
        Assert.True(MapPathfinder.CellDistance(0, fish) > 10);
        int? r = MapPathfinder.StandCellFor(0, fish, 10, null, All, null);
        Assert.Equal(MapPathfinder.StandCellsInRange(fish, 10, 0, All)[0], r);
        Assert.True(MapPathfinder.CellDistance(r!.Value, fish) <= 10);
    }

    [Fact]
    public void MapActorsReader_reads_monster_levels_v4()
    {
        var cat = ProtocolCatalog.LoadFromDirectory(DataDir);
        int Fn(string m, string f) => cat.FieldNumber(m, f) ?? throw new Xunit.Sdk.XunitException($"{m}.{f} absent du mapping");
        static ProtoField V(int f, ulong v) => new() { Field = f, Wire = WireType.Varint, Varint = v };
        static ProtoField M(int f, params ProtoField[] k) => new() { Field = f, Wire = WireType.Len, Message = k.ToList() };

        int gidF = Fn("MonsterInGroupInformation", "gid"), lvlF = Fn("MonsterInGroupInformation", "level");
        ProtoField Mon(int field, ulong gid, ulong lvl) => M(field, V(gidF, gid), V(lvlF, lvl));
        var ident = M(Fn("MonsterGroupActor", "identification"),
            Mon(Fn("MonsterGroupStaticInformation", "main_creature"), 101, 12),
            Mon(Fn("MonsterGroupStaticInformation", "underlings"), 102, 7),
            Mon(Fn("MonsterGroupStaticInformation", "underlings"), 101, 9));
        var actor = M(5,
            M(Fn("ActorPositionInformation", "actor_information"),
                M(Fn("ActorInformation", "role_play_actor"), M(Fn("RolePlayActor", "monster_group_actor"), ident))),
            V(Fn("ActorPositionInformation", "actor_id"), unchecked((ulong)-3L)),
            M(Fn("ActorPositionInformation", "disposition"), V(Fn("EntityDisposition", "cell_id"), 222), V(Fn("EntityDisposition", "direction"), 1)));

        var actors = MapActorsReader.Extract(new List<ProtoField> { actor }, 5, g => $"M{g}", null);
        var g = Assert.Single(actors);
        Assert.Equal(MapActorKind.Monster, g.Kind);
        Assert.Equal(222, g.CellId);
        Assert.NotNull(g.Members);
        Assert.Equal(3, g.Members!.Count);
        Assert.Equal(new[] { 12, 7, 9 }, g.Members.Select(m => m.Level));
        Assert.Equal(new[] { 101, 102, 101 }, g.Members.Select(m => m.GenericId));
        Assert.Equal(28, g.GroupLevel);
    }

    [Fact]
    public void LotB_mapping_resolves()
    {
        var cat = ProtocolCatalog.LoadFromDirectory(DataDir);
        foreach (var n in new[] { "InteractiveUsedEvent", "ObjectMovementEvent", "InteractiveUseRequest", "CharacterCharacteristicUpgradeRequest" })
            Assert.NotNull(cat.CodeForName(n));
        Assert.NotNull(cat.FieldNumber("InteractiveUsedEvent", "element_id"));
        Assert.NotNull(cat.FieldNumber("ObjectMovementEvent", "object_uid"));
        Assert.NotNull(cat.FieldNumber("ObjectMovementEvent", "position"));
        Assert.NotNull(cat.FieldNumber("MonsterInGroupInformation", "level"));
    }
}
