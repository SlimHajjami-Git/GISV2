-- 058 — Les prix carburant de référence en dinars ne s'appliquent plus aux sociétés hors dinar
--       (campagne de tests GPA du 06/10/2026, erreur n°2).
--
-- Constat : Carburant → Saisie manuelle préremplissait 2,075 le litre de diesel et 2,225
-- l'essence pour « Belive GPA », société facturée en EUR. Ce sont les prix tunisiens en
-- dinars que le seed de démarrage (Program.cs, SeedFuelTypesAndPricing) posait chez TOUTE
-- société sans prix, quelle que soit sa devise. Sur TN le 14/10/2026 : 2 sociétés en EUR
-- (7 prix chacune, tous actifs), aucun plein enregistré à ces prix. Un prix posé par le
-- client lui-même (1,98 sans plomb) n'est pas concerné.
--
-- Modèle : le seed ne donne désormais ses prix en dinars qu'aux sociétés en dinar tunisien
-- (devise TND ou DT, ou devise vide = devise du déploiement). Ici on DÉSACTIVE (IsActive =
-- false, rien n'est supprimé) les prix du seed déjà posés ailleurs : le champ « prix au
-- litre » reste vide et le client saisit le prix du ticket (ou le scan le lit). L'offre GPA
-- n'a pas l'écran « Gestion de Flotte » où se saisissent les tarifs : c'est voulu (décision
-- de Karim du 14/10/2026). Une ligne désactivée compte comme « prix existant » pour le seed,
-- qui ne la recrée donc pas. Restauration : remettre "IsActive" = true sur les mêmes lignes.
--
-- NB : une société à devise VIDE suit la devise du déploiement ; elle est laissée telle
-- quelle (sur TN c'est le dinar). Sur un déploiement en DZD, ces prix seraient faux aussi :
-- à traiter à part si ce déploiement les a reçus.

UPDATE fuel_pricing fp
   SET "IsActive" = false,
       "UpdatedAt" = now()
  FROM fuel_types ft, societes s
 WHERE ft."Id" = fp."FuelTypeId"
   AND s.id = fp."CompanyId"
   AND upper(trim(coalesce(s.settings->>'Currency', ''))) NOT IN ('', 'TND', 'DT')
   AND (ft."Code", fp."PricePerLiter") IN (VALUES
         ('diesel', 2.075), ('essence', 2.225), ('sans_plomb', 2.185), ('gpl', 0.850),
         ('gnv', 0.650), ('hybride', 2.075), ('hybride_rechargeable', 2.075))
   AND fp."IsActive" IS DISTINCT FROM false;
