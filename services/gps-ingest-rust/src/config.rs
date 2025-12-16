use std::{fs, path::Path};

use anyhow::{Context, Result};
use serde::Deserialize;

#[derive(Debug, Deserialize)]
pub struct AppConfig {
    #[serde(default)]
    pub listeners: Vec<ListenerConfig>,
}

#[derive(Debug, Deserialize, Clone)]
pub struct ListenerConfig {
    pub port: u16,
    pub protocol: String,
    #[serde(default = "TransportKind::default_tcp")]
    pub transport: TransportKind,
}

#[derive(Debug, Deserialize, Clone, Copy)]
#[serde(rename_all = "lowercase")]
pub enum TransportKind {
    Tcp,
    Udp,
}

impl TransportKind {
    const fn default_tcp() -> Self {
        TransportKind::Tcp
    }
}

pub fn load_config<P: AsRef<Path>>(path: P) -> Result<AppConfig> {
    let path_ref = path.as_ref();
    let contents = fs::read_to_string(path_ref)
        .with_context(|| format!("failed to read config file: {}", path_ref.display()))?;
    let config = serde_yaml::from_str::<AppConfig>(&contents)
        .with_context(|| format!("failed to parse config file: {}", path_ref.display()))?;
    Ok(config)
}
