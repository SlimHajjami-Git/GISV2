-- 062 — Une plaque ne peut exister qu'une fois par société (campagne de tests GPA du 06/10/2026, erreur n°16).
--
-- Constat : un double clic sur « Ajouter » créait deux véhicules de même plaque. L'API contrôle
-- bien le matricule (VehicleWriteRules.EnsurePlateAvailableAsync, DEF-037), mais les deux
-- requêtes simultanées lisent la base AVANT que l'une ou l'autre n'écrive : les deux passent.
-- Seule la base peut trancher entre deux écritures concurrentes. Vérifié le 15/10/2026 : aucune
-- plaque en double sur TN (505 véhicules) ni en local, l'index se pose sans nettoyage.
--
-- Modèle : même clé que VehiclePlateKey.Normalize — lettres et chiffres seuls, en majuscules
-- (« GA-214-RK », « ga 214 rk » et « GA214RK » sont la même plaque), par société. Les plaques
-- vides ou faites de séparateurs ne sont pas concernées. Deux sociétés peuvent porter la même
-- plaque. L'API traduit le refus (23505 sur cet index) en « Ce matricule est déjà utilisé dans
-- votre société. » (ExceptionHandlingMiddleware).
--
-- Ordre : indépendant du pod API (aucune colonne nouvelle). Si l'index échoue (« could not create
-- unique index »), c'est qu'un doublon est apparu depuis le 15/10 : le lister, ne rien supprimer
-- sans l'accord explicite de Karim/Slim.

CREATE UNIQUE INDEX IF NOT EXISTS ux_vehicles_societe_plaque
    ON vehicles (company_id, upper(regexp_replace(plate_number, '[^[:alnum:]]', '', 'g')))
    WHERE regexp_replace(coalesce(plate_number, ''), '[^[:alnum:]]', '', 'g') <> '';
