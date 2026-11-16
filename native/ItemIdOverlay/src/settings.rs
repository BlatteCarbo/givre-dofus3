//! Local display preferences, read once at mod load; never read from the text hook.

use std::path::PathBuf;

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum SuffixFormat {
    #[default]
    Hash,
    Brackets,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Settings {
    pub enabled: bool,
    pub format: SuffixFormat,
}

impl Default for Settings {
    fn default() -> Self {
        Self {
            enabled: true,
            format: SuffixFormat::Hash,
        }
    }
}

impl Settings {
    pub fn parse(text: &str) -> Option<Self> {
        let mut settings = Self::default();
        for line in text.lines() {
            match line.split_once('\t')? {
                ("enabled", "true") => settings.enabled = true,
                ("enabled", "false") => settings.enabled = false,
                ("format", "hash") => settings.format = SuffixFormat::Hash,
                ("format", "brackets") => settings.format = SuffixFormat::Brackets,
                _ => return None,
            }
        }
        Some(settings)
    }

    pub fn load() -> Self {
        let Some(path) = std::env::var_os("GIVRE_ITEM_OVERLAY_SETTINGS") else {
            return Self::default();
        };
        let path = PathBuf::from(path);
        if !path.is_absolute() {
            return Self::default();
        }
        std::fs::read_to_string(path)
            .ok()
            .filter(|text| text.len() <= 128)
            .and_then(|text| Self::parse(&text))
            .unwrap_or_default()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_supported_preferences_and_rejects_templates() {
        assert_eq!(
            Settings::parse("enabled\tfalse\nformat\tbrackets\n"),
            Some(Settings {
                enabled: false,
                format: SuffixFormat::Brackets
            })
        );
        assert!(Settings::parse("format\t<script>").is_none());
        assert!(Settings::parse("enabled\tyes").is_none());
    }
}
