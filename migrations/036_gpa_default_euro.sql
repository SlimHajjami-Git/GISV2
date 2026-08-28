-- 036 — L'offre « gestion de parc sans GPS » est facturée en euros par défaut.
--
-- Décision client du 28/08/2026 : le produit GPA est le produit européen,
-- sa monnaie par défaut est l'EUR quel que soit le pays saisi à l'inscription
-- (le fuseau horaire, lui, continue de suivre le pays). L'inscription applique
-- désormais cette règle (RegisterCommandHandler) ; cette migration rattrape
-- les comptes GPA créés avant — vérifiés un à un sur TN le 28/08/2026 : les
-- quatre sociétés concernées sont des comptes internes ou de recette, aucun
-- client installé.
--
-- Idempotente : ne touche que les lignes encore sans Currency='EUR'.

UPDATE societes s
SET settings = jsonb_set(COALESCE(s.settings, '{}'::jsonb), '{Currency}', '"EUR"')
FROM subscription_types st
WHERE st.id = s.subscription_type_id
  AND st.gps_tracking = false
  AND COALESCE(s.settings->>'Currency', '') <> 'EUR';
