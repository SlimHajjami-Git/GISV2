//! Connexions en attente d'identification.
//!
//! CONSTAT (EL MOUSSEM, 10/2026) : un NEMS_LITE 1.0.120 R00C03 ne renvoie pas son
//! AA00/AA01 après une coupure réseau, et ses positions n'ont ni IMEI ni préfixe
//! MAT. Jusqu'ici, toutes ses trames étaient JETÉES jusqu'à un redémarrage par SMS
//! — trajets perdus, véhicule muet des heures (5 boîtiers d'un coup au
//! redémarrage du serveur du 21/10/2026).
//!
//! Désormais ces trames sont RETENUES ici, par connexion (IP:port source). Un
//! administrateur voit la connexion sur System Health avec le véhicule probable
//! (sa dernière position connue est presque toujours à quelques mètres de la
//! première trame retenue), et confirme : la connexion est alors rattachée à ce
//! boîtier et les trames retenues sont ingérées. Jamais d'attribution sans
//! confirmation humaine — deux véhicules garés côte à côte au dépôt ne doivent
//! pas pouvoir être confondus.
//!
//! Tout est en mémoire : un redémarrage de l'ingestion perd les trames retenues
//! (il coupe aussi toutes les connexions, qui reviennent comme nouvelles).

use std::collections::{HashMap, VecDeque};

use chrono::{DateTime, NaiveDateTime, Utc};
use serde::Serialize;
use tokio::sync::Mutex;

use crate::telemetry::model::HhFrame;

/// Trames gardées au plus par connexion (≈ 2 jours à une trame par minute).
const MAX_TRAMES_PAR_CONNEXION: usize = 3000;
/// Connexions suivies au plus ; au-delà, on oublie les plus anciennes fermées.
const MAX_CONNEXIONS: usize = 300;
/// Une connexion sans nouvelle trame depuis ce délai est oubliée.
const DUREE_CONSERVATION_SECS: i64 = 48 * 3600;

#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct PositionRetenue {
    pub lat: f64,
    pub lon: f64,
    /// Heure GPS de la trame (UTC).
    pub horodatage: NaiveDateTime,
    pub vitesse_kmh: f64,
}

#[derive(Debug, Clone, Serialize)]
pub struct ConnexionEnAttente {
    pub peer: String,
    pub protocole: String,
    pub ouverte: bool,
    pub premiere_reception: DateTime<Utc>,
    pub derniere_reception: DateTime<Utc>,
    /// Trames actuellement retenues.
    pub trames: usize,
    /// Trames les plus anciennes abandonnées faute de place.
    pub trames_ecartees: u64,
    /// Première position valide reçue : c'est elle qu'on compare à la dernière
    /// position connue des véhicules disparus.
    pub premiere_position: Option<PositionRetenue>,
    pub derniere_position: Option<PositionRetenue>,
    #[serde(skip)]
    contenu: VecDeque<String>,
}

/// Connexions non identifiées, et attributions confirmées (peer → IMEI) gardées
/// tant que la connexion vit, pour vérifier l'identité si le boîtier finit par
/// s'annoncer.
#[derive(Default)]
pub struct RegistreAttente {
    connexions: Mutex<HashMap<String, ConnexionEnAttente>>,
    attributions: Mutex<HashMap<String, String>>,
}

fn position_de(trame: &HhFrame) -> Option<PositionRetenue> {
    // 0,0 = pas de position GPS : inutile pour reconnaître le véhicule.
    if trame.latitude.abs() < 1.0 && trame.longitude.abs() < 2.0 {
        return None;
    }
    Some(PositionRetenue {
        lat: trame.latitude,
        lon: trame.longitude,
        horodatage: trame.recorded_at,
        vitesse_kmh: trame.speed_kph,
    })
}

impl RegistreAttente {
    pub fn new() -> Self {
        Self::default()
    }

    /// Retient une trame rejetée faute d'identité.
    pub async fn retenir(&self, peer: &str, protocole: &str, texte: &str, trame: &HhFrame) {
        self.retenir_a(peer, protocole, texte, trame, Utc::now()).await
    }

    async fn retenir_a(&self, peer: &str, protocole: &str, texte: &str, trame: &HhFrame, maintenant: DateTime<Utc>) {
        let mut connexions = self.connexions.lock().await;
        if !connexions.contains_key(peer) {
            connexions.retain(|_, c| (maintenant - c.derniere_reception).num_seconds() < DUREE_CONSERVATION_SECS);
            if connexions.len() >= MAX_CONNEXIONS {
                let plus_ancienne = connexions
                    .values()
                    .min_by_key(|c| (c.ouverte, c.derniere_reception))
                    .map(|c| c.peer.clone());
                if let Some(p) = plus_ancienne {
                    connexions.remove(&p);
                }
            }
        }
        let entree = connexions.entry(peer.to_string()).or_insert_with(|| ConnexionEnAttente {
            peer: peer.to_string(),
            protocole: protocole.to_string(),
            ouverte: true,
            premiere_reception: maintenant,
            derniere_reception: maintenant,
            trames: 0,
            trames_ecartees: 0,
            premiere_position: None,
            derniere_position: None,
            contenu: VecDeque::new(),
        });
        entree.derniere_reception = maintenant;
        let position = position_de(trame);
        if entree.premiere_position.is_none() {
            entree.premiere_position = position.clone();
        }
        if position.is_some() {
            entree.derniere_position = position;
        }
        if entree.contenu.len() >= MAX_TRAMES_PAR_CONNEXION {
            entree.contenu.pop_front();
            entree.trames_ecartees += 1;
        }
        entree.contenu.push_back(texte.to_string());
        entree.trames = entree.contenu.len();
    }

    /// La connexion TCP est fermée : ses trames restent consultables et
    /// attribuables ; son attribution éventuelle n'a plus d'objet.
    pub async fn fermer(&self, peer: &str) {
        if let Some(c) = self.connexions.lock().await.get_mut(peer) {
            c.ouverte = false;
        }
        self.attributions.lock().await.remove(peer);
    }

    /// Les connexions en attente, la plus récente d'abord.
    pub async fn lister(&self) -> Vec<ConnexionEnAttente> {
        let maintenant = Utc::now();
        let connexions = self.connexions.lock().await;
        let mut liste: Vec<ConnexionEnAttente> = connexions
            .values()
            .filter(|c| (maintenant - c.derniere_reception).num_seconds() < DUREE_CONSERVATION_SECS)
            .cloned()
            .collect();
        liste.sort_by(|a, b| b.derniere_reception.cmp(&a.derniere_reception));
        liste
    }

    /// Retire la connexion du registre et rend ses trames, dans l'ordre reçu.
    pub async fn extraire(&self, peer: &str) -> Option<(ConnexionEnAttente, Vec<String>)> {
        let mut c = self.connexions.lock().await.remove(peer)?;
        let trames: Vec<String> = c.contenu.drain(..).collect();
        Some((c, trames))
    }

    pub async fn noter_attribution(&self, peer: &str, imei: &str) {
        self.attributions.lock().await.insert(peer.to_string(), imei.to_string());
    }

    /// IMEI attribué à la main à cette connexion, s'il y en a un.
    pub async fn attribution(&self, peer: &str) -> Option<String> {
        self.attributions.lock().await.get(peer).cloned()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::telemetry::hh::parse_frame;

    /// Position réelle d'un NEMS_LITE R00C03 d'EL MOUSSEM, rejetée le 19/10/2026.
    const AA13: &str = "AA130082CC021BB7C60091D13B00001D2A0002FEFDE3804C00000000010000000026F10C15";

    #[tokio::test]
    async fn une_trame_retenue_garde_sa_position_et_son_texte() {
        let registre = RegistreAttente::new();
        let trame = parse_frame(AA13).unwrap();
        registre.retenir("203.0.113.10:2656", "gps_type_1", AA13, &trame).await;
        registre.retenir("203.0.113.10:2656", "gps_type_1", AA13, &trame).await;

        let liste = registre.lister().await;
        assert_eq!(liste.len(), 1);
        assert_eq!(liste[0].trames, 2);
        assert!(liste[0].ouverte);
        let p = liste[0].premiere_position.clone().expect("position");
        assert!((p.lat - trame.latitude).abs() < 1e-9 && (p.lon - trame.longitude).abs() < 1e-9);

        let (connexion, trames) = registre.extraire("203.0.113.10:2656").await.unwrap();
        assert_eq!(connexion.peer, "203.0.113.10:2656");
        assert_eq!(trames, vec![AA13.to_string(), AA13.to_string()]);
        assert!(registre.lister().await.is_empty());
    }

    #[tokio::test]
    async fn les_trames_les_plus_anciennes_sont_ecartees_au_dela_du_plafond() {
        let registre = RegistreAttente::new();
        let trame = parse_frame(AA13).unwrap();
        for _ in 0..(MAX_TRAMES_PAR_CONNEXION + 5) {
            registre.retenir("p:1", "gps_type_1", AA13, &trame).await;
        }
        let liste = registre.lister().await;
        assert_eq!(liste[0].trames, MAX_TRAMES_PAR_CONNEXION);
        assert_eq!(liste[0].trames_ecartees, 5);
    }

    #[tokio::test]
    async fn une_connexion_trop_ancienne_est_oubliee() {
        let registre = RegistreAttente::new();
        let trame = parse_frame(AA13).unwrap();
        let il_y_a_3_jours = Utc::now() - chrono::Duration::days(3);
        registre.retenir_a("vieille:1", "gps_type_1", AA13, &trame, il_y_a_3_jours).await;
        assert!(registre.lister().await.is_empty());
        registre.retenir("neuve:1", "gps_type_1", AA13, &trame).await;
        let liste = registre.lister().await;
        assert_eq!(liste.len(), 1);
        assert_eq!(liste[0].peer, "neuve:1");
    }

    #[tokio::test]
    async fn fermer_garde_les_trames_et_oublie_l_attribution() {
        let registre = RegistreAttente::new();
        let trame = parse_frame(AA13).unwrap();
        registre.retenir("p:2", "gps_type_1", AA13, &trame).await;
        registre.noter_attribution("p:2", "860000000000209").await;
        registre.fermer("p:2").await;
        assert!(!registre.lister().await[0].ouverte);
        assert_eq!(registre.attribution("p:2").await, None);
    }
}
