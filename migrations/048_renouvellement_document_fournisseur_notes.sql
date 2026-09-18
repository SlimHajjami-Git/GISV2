-- 047 — Renouvellement de document : fournisseur, notes et date d'échéance
-- réellement stockés (campagne de test Calypso GPA, DEF-007 du 11/09/2026).
--
-- CONSTAT — La fenêtre de renouvellement (document-renewal-popup) propose
-- numéro de document, fournisseur et notes, et l'API les accepte
-- (RenewDocumentCommand). Mais vehicle_costs n'a que receipt_number /
-- receipt_url / description : les propriétés ExpiryDate, DocumentNumber et
-- DocumentUrl de l'entité VehicleCost sont Ignore() dans
-- VehicleCostConfiguration — aucune colonne derrière. Résultat : la saisie
-- partait dans le vide sans message, et GET /api/documents/vehicle/{id}/history
-- renvoyait documentNumber null, notes null, expiryDate null et un « fournisseur »
-- qui était en fait la description complète (« Renouvellement Assurance - X »).
--
-- POURQUOI DES COLONNES — le numéro de document est désormais écrit dans
-- receipt_number (colonne existante, même nature : numéro de pièce), mais le
-- fournisseur et les notes n'ont AUCUN emplacement, et la date d'échéance du
-- document renouvelé non plus. Les déduire de la description est un piège :
-- elle est libre, traduite et réutilisée par l'écran Dépenses.
--
-- ORDRE DE DÉPLOIEMENT — le code livré dans le même changement MAPPE ces trois
-- colonnes (VehicleCostConfiguration) : un pod API démarré sans elles fait
-- échouer en 42703 toute requête qui charge une dépense de vehicle_costs.
-- CE SQL SE JOUE AVANT LE NOUVEAU POD API, sur DZ puis TN.

ALTER TABLE vehicle_costs ADD COLUMN IF NOT EXISTS expiry_date TIMESTAMPTZ NULL;
ALTER TABLE vehicle_costs ADD COLUMN IF NOT EXISTS provider VARCHAR(200) NULL;
ALTER TABLE vehicle_costs ADD COLUMN IF NOT EXISTS notes VARCHAR(1000) NULL;

COMMENT ON COLUMN vehicle_costs.expiry_date IS
    'Renouvellement de document : nouvelle date d''échéance du document payé (assurance, visite technique, vignette…). NULL pour toute autre dépense.';

COMMENT ON COLUMN vehicle_costs.provider IS
    'Renouvellement de document : fournisseur saisi (assureur, centre de contrôle…). La description reste le libellé affiché dans les dépenses.';

COMMENT ON COLUMN vehicle_costs.notes IS
    'Renouvellement de document : notes libres saisies par l''exploitant.';
