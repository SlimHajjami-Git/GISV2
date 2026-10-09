-- 057 — Liste de blocage des boîtiers supprimés par l'administration.
--
-- DEMANDE (Slim, 09/10/2026) : « dans l'interface admin il faut avoir la possibilité de
-- supprimer un boîtier ; si le boîtier est supprimé, toutes ses informations et relations
-- dans la base doivent être supprimées aussi (véhicule, position, carburant…) ».
--
-- CONSTAT — Supprimer la fiche ne suffit pas : l'ingestion Rust recrée une fiche (société 1,
-- non affectée) pour tout IMEI inconnu qui émet (ensure_device, ON CONFLICT (device_uid)),
-- et son cache mémoire garde l'ancien id jusqu'au redémarrage du pod — chaque trame tombait
-- alors en violation de clé étrangère. Un boîtier encore branché « revenait » donc tout seul.
--
-- RÈGLE — La suppression inscrit l'IMEI ici ; l'ingestion consulte cette table avant de créer
-- ou de mettre à jour une fiche et ignore les trames d'un IMEI bloqué (journal technique
-- « blocked_deleted »). « Débloquer » (écran Admin › Boîtiers GPS) retire la ligne : une
-- réinstallation du même boîtier redevient possible. Les colonnes *_before gardent ce que
-- portait la fiche : c'est la seule trace lisible après coup, l'audit ne gardant que l'id.
--
-- À jouer AVANT le pod API (qui lit et écrit la table) et AVANT le pod ingest (qui la lit).

CREATE TABLE IF NOT EXISTS gps_device_blocklist (
    device_uid          varchar(100) PRIMARY KEY,
    blocked_at          timestamptz  NOT NULL DEFAULT now(),
    blocked_by_user_id  integer      NULL,
    device_id_before    integer      NULL,
    mat                 varchar(50)  NULL,
    company_id_before   integer      NULL,
    reason              text         NULL
);

COMMENT ON TABLE gps_device_blocklist IS
    'IMEI des boîtiers supprimés par l''administration : l''ingestion ignore leurs trames et ne recrée pas de fiche (migration 057, 09/10/2026).';

-- frame_debug_log (journal technique écrit par l'ingestion, sans clé étrangère) n'avait aucun
-- index : l'inventaire avant suppression et la suppression elle-même le balayaient en entier
-- (124 Mo / 497 000 lignes sur TN le 09/10/2026, et ça grossit). CONCURRENTLY : la table est
-- écrite en continu par l'ingestion ; à jouer hors transaction (psql -f, pas de BEGIN).
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_frame_debug_log_device_id ON frame_debug_log (device_id);
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_frame_debug_log_device_uid ON frame_debug_log (device_uid);
