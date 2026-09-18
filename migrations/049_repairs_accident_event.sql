-- 049 — La réparation d'un sinistre apparaît dans l'écran Réparations (recette
-- Karim du 18/09/2026 : « il faut pas oublier d'ajouter la réparation de
-- l'accident dans RÉPARATIONS »).
--
-- CONSTAT — La phase 5 d'un sinistre (coût réel de la réparation) créait une
-- ligne vehicle_costs de type « repair ». L'écran Réparations, lui, lit la table
-- repairs : une réparation consécutive à un accident n'y a jamais figuré, et le
-- client la cherchait là où il enregistre toutes ses autres interventions.
--
-- POURQUOI UNE COLONNE — la phase 5 doit désormais écrire dans repairs, et une
-- seconde sauvegarde de la même phase doit RETROUVER sa ligne pour la mettre à
-- jour plutôt que d'en empiler une nouvelle à chaque enregistrement. Aucune
-- colonne existante ne porte ce lien (colonnes réelles de repairs vérifiées :
-- reference, description, repair_date, coûts, statut, facture, notes,
-- repair_type). Le lien est aussi ce qui permet de retirer la ligne quand le
-- coût réel est vidé.
--
-- ON DELETE SET NULL — même règle que vehicle_costs.accident_event_id
-- (contrainte vehicle_costs_accident_event_id_fkey, déjà en place) : l'argent
-- reste. Supprimer un dossier de sinistre détache la réparation, il ne l'efface
-- pas ; elle demeure dans l'écran Réparations et dans les rapports de coûts.
--
-- INDEX PARTIEL — seules les réparations nées d'un sinistre portent la colonne
-- (NULL partout ailleurs) : l'index ne couvre que celles-là, ce qui suffit à la
-- recherche « la réparation de CE sinistre » faite à chaque enregistrement de la
-- phase 5.
--
-- AUCUNE DONNÉE MODIFIÉE — les dépenses « repair » créées par les sinistres
-- antérieurs restent dans Dépenses. Le montant ne peut pas être compté deux fois :
-- l'API retire la dépense liée au dossier au moment même où elle écrit la ligne
-- de réparation correspondante.
--
-- ORDRE DE DÉPLOIEMENT — ce SQL sur DZ puis TN AVANT le pod API qui mappe
-- Repair.AccidentEventId, sinon 42703 sur toute lecture des réparations.

ALTER TABLE repairs
    ADD COLUMN IF NOT EXISTS accident_event_id integer;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'repairs_accident_event_id_fkey'
          AND conrelid = 'repairs'::regclass
    ) THEN
        ALTER TABLE repairs
            ADD CONSTRAINT repairs_accident_event_id_fkey
            FOREIGN KEY (accident_event_id) REFERENCES accident_events(id) ON DELETE SET NULL;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_repairs_accident_event_id
    ON repairs (accident_event_id)
    WHERE accident_event_id IS NOT NULL;

COMMENT ON COLUMN repairs.accident_event_id IS
    'Sinistre à l''origine de la réparation (NULL = réparation ordinaire saisie dans l''écran Réparations)';
