//! Table nom/niveau/type -> identifiants d'objets (generee depuis ItemData + i18n francais ; voir items.tsv).

use std::collections::HashMap;
use std::sync::OnceLock;

const RAW: &str = include_str!("items.tsv");
const SUFFIX_MARK: &str = "  #";

struct Table {
    /// nom -> [(niveau, type, gid)]
    by_name: HashMap<&'static str, Vec<(i32, &'static str, i32)>>,
    recipe_counts: HashMap<i32, u32>,
}

fn table() -> &'static Table {
    static TABLE: OnceLock<Table> = OnceLock::new();
    TABLE.get_or_init(|| parse_table(RAW))
}

fn parse_table(raw: &'static str) -> Table {
    let mut by_name: HashMap<&'static str, Vec<(i32, &'static str, i32)>> = HashMap::new();
    let mut recipe_counts = HashMap::new();
    for line in raw.lines() {
        let mut parts = line.split('\t');
        let (Some(name), Some(level), Some(kind), Some(gid)) =
            (parts.next(), parts.next(), parts.next(), parts.next())
        else {
            continue;
        };
        let (Ok(level), Ok(gid)) = (level.parse::<i32>(), gid.parse::<i32>()) else {
            continue;
        };
        by_name.entry(name).or_default().push((level, kind, gid));
        if let Some(count) = parts.next().and_then(|value| value.parse::<u32>().ok()) {
            recipe_counts.insert(gid, count);
        }
    }
    Table {
        by_name,
        recipe_counts,
    }
}

/// Analyse la ligne « Niveau 12 • Amulette » -> (12, "Amulette").
pub fn parse_level_line(text: &str) -> Option<(i32, &str)> {
    let rest = text.trim().strip_prefix("Niveau")?.trim_start();
    let (digits, tail) = rest.split_once(|c: char| !c.is_ascii_digit())?;
    let level = digits.parse::<i32>().ok()?;
    let kind = tail
        .trim_start_matches(|c: char| c.is_whitespace() || c == '•')
        .trim();
    (!kind.is_empty()).then_some((level, kind))
}

/// Retire le suffixe « #id » que ce mod a ajoute (pour comparer avec le vrai nom).
pub fn strip_mark(text: &str) -> &str {
    mark_start(text).map_or(text, |offset| &text[..offset])
}

pub fn has_mark(text: &str) -> bool {
    mark_start(text).is_some()
}

fn mark_start(text: &str) -> Option<usize> {
    for marker in [SUFFIX_MARK, "  ["] {
        if let Some((name, tail)) = text.rsplit_once(marker) {
            let ids = tail.split(" · ").next()?;
            let ids = if marker == "  [" {
                ids.strip_suffix(']')?
            } else {
                ids
            };
            if !ids.is_empty()
                && ids
                    .split('/')
                    .all(|id| id.parse::<u32>().is_ok_and(|v| v > 0))
            {
                return Some(name.len());
            }
        }
    }
    None
}

/// Identifiants correspondant a un nom d'objet affiche avec son niveau et son type.
/// Niveau et type departagent les homonymes ; a defaut on garde les candidats du nom seul si un seul niveau/type ne colle pas.
pub fn ids_for(name: &str, level: Option<i32>, kind: Option<&str>) -> Vec<i32> {
    let Some(candidates) = table().by_name.get(name) else {
        return Vec::new();
    };
    let exact: Vec<i32> = candidates
        .iter()
        .filter(|(l, k, _)| level.is_none_or(|v| v == *l) && kind.is_none_or(|v| v == *k))
        .map(|(_, _, gid)| *gid)
        .collect();
    if !exact.is_empty() {
        return exact;
    }
    if candidates.len() == 1 {
        return vec![candidates[0].2];
    }
    Vec::new()
}

/// Tous les identifiants portant ce nom exact (homonymes compris), tries.
pub fn ids_by_name(name: &str) -> Vec<i32> {
    let Some(candidates) = table().by_name.get(name) else {
        return Vec::new();
    };
    let mut ids: Vec<i32> = candidates.iter().map(|(_, _, gid)| *gid).collect();
    ids.sort_unstable();
    ids.dedup();
    ids
}

pub fn is_known_name(name: &str) -> bool {
    table().by_name.contains_key(name)
}

pub fn with_ids(name: &str, ids: &[i32]) -> String {
    let joined = ids.iter().map(i32::to_string).collect::<Vec<_>>().join("/");
    format!("{name}{SUFFIX_MARK}{joined}")
}

/// Add the item type only when all name candidates unambiguously share one ID.
/// The text is sourced from the bundled item dataset, never from the UI tree.
#[cfg(test)]
pub fn with_ids_enriched(name: &str, ids: &[i32]) -> String {
    with_format(name, ids, crate::settings::SuffixFormat::Hash)
}

pub fn with_format(name: &str, ids: &[i32], format: crate::settings::SuffixFormat) -> String {
    format_from(table(), name, ids, format)
}

fn format_from(
    data: &Table,
    name: &str,
    ids: &[i32],
    format: crate::settings::SuffixFormat,
) -> String {
    let joined = ids.iter().map(i32::to_string).collect::<Vec<_>>().join("/");
    let basic = match format {
        crate::settings::SuffixFormat::Hash => with_ids(name, ids),
        crate::settings::SuffixFormat::Brackets => format!("{name}  [{joined}]"),
    };
    if ids.len() != 1 {
        return basic;
    }
    let Some(candidates) = data.by_name.get(name) else {
        return basic;
    };
    let Some((_, item_type, _)) = candidates.iter().find(|(_, _, gid)| *gid == ids[0]) else {
        return basic;
    };
    let item_type = item_type.trim();
    let mut enriched = basic;
    if !item_type.is_empty() && item_type.chars().count() <= 32 {
        enriched.push_str(&format!(" · {item_type}"));
    }
    if let Some(count) = data.recipe_counts.get(&ids[0]) {
        enriched.push_str(&format!(" · {count} recette(s)"));
    }
    enriched
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn renders_recipe_counts_without_inventing_ambiguous_metadata() {
        let data = parse_table(include_str!("../tests/fixtures/items.tsv"));
        let format = crate::settings::SuffixFormat::Brackets;
        assert_eq!(
            format_from(&data, "Used", &[2], format),
            "Used  [2] · Type · 12 recette(s)"
        );
        assert_eq!(
            format_from(&data, "Known", &[1], format),
            "Known  [1] · Type · 0 recette(s)"
        );
        assert_eq!(format_from(&data, "Old", &[3], format), "Old  [3] · Type");
        assert_eq!(format_from(&data, "Used", &[1, 2], format), "Used  [1/2]");
    }

    #[test]
    fn parses_known_zero_and_absent_recipe_counts() {
        let table = parse_table(
            "Known\t1\tType\t1\t0\nUsed\t1\tType\t2\t12\nOld\t1\tType\t3\nInvalid\t1\tType\t4\t-1\n",
        );
        assert_eq!(table.recipe_counts.get(&1), Some(&0));
        assert_eq!(table.recipe_counts.get(&2), Some(&12));
        assert_eq!(table.recipe_counts.get(&3), None);
        assert_eq!(table.recipe_counts.get(&4), None);
    }

    #[test]
    fn bracket_suffix_round_trip_and_ambiguity() {
        let marked = with_format("test", &[1, 2], crate::settings::SuffixFormat::Brackets);
        assert_eq!(marked, "test  [1/2]");
        assert!(has_mark(&marked));
        assert_eq!(strip_mark(&marked), "test");
        assert!(!has_mark("test  [ordinary text]"));
    }

    #[test]
    fn only_our_numeric_suffix_is_stripped() {
        assert_eq!(strip_mark("test  #word"), "test  #word");
        assert!(!has_mark("test  #word"));
    }

    #[test]
    fn parses_the_level_line_from_the_tooltip() {
        assert_eq!(
            parse_level_line("Niveau 1 • Conteneur"),
            Some((1, "Conteneur"))
        );
        assert_eq!(
            parse_level_line("Niveau 200 • Amulette"),
            Some((200, "Amulette"))
        );
        assert_eq!(parse_level_line("Sachet de Haricots"), None);
    }

    #[test]
    fn finds_the_bean_pouch() {
        assert_eq!(
            ids_for("Sachet de Haricots", Some(1), Some("Conteneur")),
            vec![16828]
        );
        assert!(is_known_name("Sachet de Haricots"));
    }

    #[test]
    fn marker_round_trip() {
        let marked = with_ids("Sachet de Haricots", &[16828]);
        assert!(has_mark(&marked));
        assert_eq!(strip_mark(&marked), "Sachet de Haricots");
        assert_eq!(strip_mark("Sachet de Haricots"), "Sachet de Haricots");
    }

    #[test]
    fn unknown_name_gives_nothing() {
        assert!(ids_for("Objet qui n'existe pas", Some(1), None).is_empty());
    }

    #[test]
    fn enriches_known_single_gid_with_local_item_type() {
        let marked = with_ids_enriched("Sachet de Haricots", &[16828]);
        assert_eq!(
            marked,
            "Sachet de Haricots  #16828 · Conteneur · 0 recette(s)"
        );
        assert!(has_mark(&marked));
    }

    #[test]
    fn bundled_release_contains_real_recipe_counts() {
        assert_eq!(table().recipe_counts.len(), 20784);
        assert_eq!(table().recipe_counts.get(&2540), Some(&186));
        assert_eq!(
            with_ids_enriched("Substrat de Futaie", &[2540]),
            "Substrat de Futaie  #2540 \u{00b7} Substrat \u{00b7} 186 recette(s)"
        );
    }

    #[test]
    fn ambiguous_ids_do_not_invent_item_types() {
        assert_eq!(with_ids_enriched("test", &[1, 2]), "test  #1/2");
    }
}
