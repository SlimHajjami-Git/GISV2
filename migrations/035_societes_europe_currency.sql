-- 035 — Comptes européens : monnaie EUR et fuseau Europe/Paris.
--
-- L'inscription fixe désormais ces réglages d'elle-même (RegisterCommandHandler)
-- pour tout pays de la liste EuropeanCountries. Cette migration rattrape les
-- sociétés européennes créées AVANT le correctif, qui affichaient des dinars
-- sur l'écran d'abonnement (recette client du 26/08/2026).
--
-- Idempotente : ne touche que les lignes encore sans Currency='EUR'.

UPDATE societes
SET settings = jsonb_set(
                 jsonb_set(COALESCE(settings, '{}'::jsonb), '{Currency}', '"EUR"'),
                 '{Timezone}', '"Europe/Paris"')
WHERE country IN ('AD','AT','BE','BG','CH','CY','CZ','DE','DK','EE','ES','FI',
                  'FR','GB','GR','HR','HU','IE','IS','IT','LI','LT','LU','LV',
                  'MC','MT','NL','NO','PL','PT','RO','SE','SI','SK','SM')
  AND COALESCE(settings->>'Currency', '') <> 'EUR';
