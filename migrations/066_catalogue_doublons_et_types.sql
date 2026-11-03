-- 066 — Catalogue des modèles : doublons masqués et types ramenés à la liste de la fiche véhicule
-- (corrections restantes Calypso GPA du 28/10/2026, points 1 et 3).
--
-- Constat (lecture seule de TN, 28/10/2026) :
--   - 45 couples marque/modèle en double dans vehicle_models, dont 35 où les DEUX lignes sont
--     actives : le modèle apparaît deux fois dans la liste « Modèle » (RENAULT Clio, Kangoo,
--     Master, TOYOTA Hilux, FORD Transit…) ;
--   - 93 modèles portent un type hors de la liste de la fiche (van, hatchback, sedan, truck,
--     pickup) : choisir l'un d'eux posait ce type tel quel, le champ Type paraissait vide ;
--   - 18 véhicules ont gardé un tel type (16 « van », 2 « hatchback ») : la liste des véhicules
--     les affiche sans libellé ni icône.
--
-- Modèle : RIEN n'est supprimé. Dans chaque groupe de doublons (même marque, même nom sans
-- tenir compte de la casse ni des espaces), la ligne active la plus ancienne reste active, les
-- autres lignes actives passent à IsActive = false (l'API des marques ne sert que les modèles
-- actifs). Les types suivent la même table de rattachement que l'écran
-- (services/gis-frontend/src/services/types-vehicule.ts) : van, fourgon, camionnette, pickup,
-- pick-up → utilitaire ; truck → camion ; hatchback, sedan, berline → citadine ; toute autre
-- valeur hors liste → other. Les véhicules enregistrent marque et modèle en texte, sans lien
-- vers le catalogue : désactiver un modèle ne touche aucun véhicule.
--
-- Rejouable sans effet (second passage : 0 ligne). Indépendant du pod API.
-- À jouer sur TN SEULEMENT avec l'accord de Karim (modification de données de production).
-- Sauvegarde conseillée avant, sur TN :
--   \copy (SELECT row_to_json(m) FROM vehicle_models m) TO 'vehicle_models_avant_066.json'
--   \copy (SELECT row_to_json(v) FROM (SELECT id, type FROM vehicles) v) TO 'vehicles_type_avant_066.json'

-- 1. Doublons : seule la ligne active la plus ancienne de chaque groupe reste active.
UPDATE vehicle_models m
SET "IsActive" = false
WHERE m."IsActive"
  AND EXISTS (
      SELECT 1 FROM vehicle_models plus_ancien
      WHERE plus_ancien."IsActive"
        AND plus_ancien."BrandId" = m."BrandId"
        AND upper(trim(plus_ancien."Name")) = upper(trim(m."Name"))
        AND plus_ancien."Id" < m."Id"
  );

-- 2. Types des modèles ramenés à la liste de la fiche.
UPDATE vehicle_models
SET "VehicleType" = CASE lower(trim("VehicleType"))
        WHEN 'van' THEN 'utilitaire'
        WHEN 'fourgon' THEN 'utilitaire'
        WHEN 'camionnette' THEN 'utilitaire'
        WHEN 'pickup' THEN 'utilitaire'
        WHEN 'pick-up' THEN 'utilitaire'
        WHEN 'truck' THEN 'camion'
        WHEN 'hatchback' THEN 'citadine'
        WHEN 'sedan' THEN 'citadine'
        WHEN 'berline' THEN 'citadine'
        WHEN 'camion' THEN 'camion'
        WHEN 'citadine' THEN 'citadine'
        WHEN 'suv' THEN 'suv'
        WHEN 'utilitaire' THEN 'utilitaire'
        ELSE 'other'
    END
WHERE "VehicleType" IS NOT NULL
  AND trim("VehicleType") <> ''
  AND "VehicleType" NOT IN ('camion', 'citadine', 'suv', 'utilitaire', 'other');

-- 3. Types des véhicules, même rattachement (un type vide reste vide).
UPDATE vehicles
SET type = CASE lower(trim(type))
        WHEN 'van' THEN 'utilitaire'
        WHEN 'fourgon' THEN 'utilitaire'
        WHEN 'camionnette' THEN 'utilitaire'
        WHEN 'pickup' THEN 'utilitaire'
        WHEN 'pick-up' THEN 'utilitaire'
        WHEN 'truck' THEN 'camion'
        WHEN 'hatchback' THEN 'citadine'
        WHEN 'sedan' THEN 'citadine'
        WHEN 'berline' THEN 'citadine'
        WHEN 'camion' THEN 'camion'
        WHEN 'citadine' THEN 'citadine'
        WHEN 'suv' THEN 'suv'
        WHEN 'utilitaire' THEN 'utilitaire'
        ELSE 'other'
    END,
    updated_at = now()
WHERE type IS NOT NULL
  AND trim(type) <> ''
  AND type NOT IN ('camion', 'citadine', 'suv', 'utilitaire', 'other');
