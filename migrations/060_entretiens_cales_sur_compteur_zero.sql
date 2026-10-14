-- 060 — Entretiens au km calés sur un compteur à 0 : recalage sur le compteur actuel
--       (campagne de tests GPA du 06/10/2026, erreur n°5 ; recalage décidé par Karim le 14/10/2026).
--
-- Constat : à l'affectation, l'échéance au km valait « compteur + intervalle ». Un véhicule
-- encore à 0 km (fiche sans compteur, GPS qui n'avait pas remonté) recevait donc une échéance
-- à 0 + intervalle, et passait « en retard » dès l'arrivée du vrai compteur. Sur TN le
-- 14/10/2026 : HERTZ, 97 entretiens sur 26 véhicules, affectés du 08 au 23/08/2026, en retard
-- de 48 705 km en moyenne et jusqu'à 208 194 km (206 TU 3873 : vidange « due » à 10 000 km,
-- compteur à 218 194 km) ; 10 908 notifications d'entretien en 7 jours.
--
-- Modèle : même règle que le bouton « Recalculer » (RebaseMaintenanceScheduleCommand) :
-- prochaine échéance = compteur actuel + intervalle (personnalisé, sinon celui du modèle).
-- L'API ne crée plus d'échéance calée sur 0 : elle attend le premier relevé.
--
-- Cible, au plus près : entretien JAMAIS fait (last_done_km et last_done_date vides), échéance
-- au km égale à l'intervalle (= calée sur 0) et compteur actuel au-delà. Un entretien calé sur
-- 0 mais pas encore dépassé n'est pas touché (véhicule peut-être réellement neuf). L'échéance
-- par date n'est pas touchée. Statut remis à « ok » et compteur de notifications remis à zéro
-- (comme « Recalculer ») ; le recalcul horaire (MaintenanceStatusRefreshService) remet le bon
-- statut dans l'heure si une échéance par date est dépassée.
--
-- Précautions : sauvegarde des lignes AVANT recalage dans vehicle_maintenance_schedules_recalage_060
-- (gardée : c'est le plan de restauration) ; tout se fait dans UNE transaction ; refus si plus
-- de 500 lignes (garde-fou : 97 attendues sur TN le 14/10/2026) ; rejouable sans effet (la table
-- de sauvegarde n'est créée qu'une fois et l'UPDATE ne touche que les lignes encore à l'ancienne
-- valeur).
--
-- Restauration :
--   UPDATE vehicle_maintenance_schedules s
--      SET next_due_km = b.next_due_km, status = b.status, notification_count = b.notification_count,
--          last_notification_at = b.last_notification_at, updated_at = now()
--     FROM vehicle_maintenance_schedules_recalage_060 b WHERE b.id = s.id;
--
-- Indépendante du déploiement de l'API (pas de nouvelle colonne) ; à jouer avec la 059.

BEGIN;

CREATE TABLE IF NOT EXISTS vehicle_maintenance_schedules_recalage_060 AS
SELECT s.*, v.mileage AS compteur_au_recalage, now() AS sauvegarde_le
  FROM vehicle_maintenance_schedules s
  JOIN maintenance_templates t ON t.id = s.template_id
  JOIN vehicles v ON v.id = s.vehicle_id
 WHERE s.last_done_km IS NULL
   AND s.last_done_date IS NULL
   AND s.next_due_km IS NOT NULL
   AND s.next_due_km = coalesce(s.custom_interval_km, t.interval_km)
   AND v.mileage > s.next_due_km;

DO $$
DECLARE
    sauvegardees integer;
    recalees integer;
BEGIN
    SELECT count(*) INTO sauvegardees FROM vehicle_maintenance_schedules_recalage_060;
    IF sauvegardees > 500 THEN
        RAISE EXCEPTION '060 : % lignes visées (plus de 500) — recalage annulé, à vérifier à la main', sauvegardees;
    END IF;

    UPDATE vehicle_maintenance_schedules s
       SET next_due_km = b.compteur_au_recalage + coalesce(s.custom_interval_km, t.interval_km),
           status = 'ok',
           notification_count = 0,
           last_notification_at = NULL,
           updated_at = now()
      FROM vehicle_maintenance_schedules_recalage_060 b, maintenance_templates t
     WHERE b.id = s.id
       AND t.id = s.template_id
       AND s.next_due_km = b.next_due_km
       AND s.last_done_km IS NULL;
    GET DIAGNOSTICS recalees = ROW_COUNT;

    RAISE NOTICE '060 : % ligne(s) sauvegardée(s), % recalée(s) maintenant', sauvegardees, recalees;
END $$;

COMMIT;
