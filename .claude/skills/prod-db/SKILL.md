---
name: prod-db
description: Interroger la base PostgreSQL de production TN (pod postgres-0, alias SSH belive-tn) en LECTURE SEULE via kubectl exec — lister information_schema.columns avant toute requête (piège snake_case / PascalCase), SELECT et EXPLAIN uniquement ; toute écriture se propose avec chiffres et suit le protocole sauvegarde + transaction + garde-fous. Utiliser pour toute question « qu'y a-t-il vraiment en base ? ».
---

# /prod-db <question ou requête>

Demande reçue : `$ARGUMENTS`. Traduis-la en une ou plusieurs requêtes SQL
**de lecture** contre la base `gis_v2` du serveur TN, exécute-les, et rends
les chiffres avec la requête utilisée. Si la demande contient (ou implique)
une écriture, saute directement à la section « Si une écriture est nécessaire ».

Les données sont l'actif du client : même du « junk » apparent peut avoir une
valeur (audit, litige) que tu ne connais pas. Un « continue » ou un « ok »
générique n'est **jamais** une autorisation d'écrire.

## 1. La commande exacte (lecture seule forcée)

Toujours passer par `PGOPTIONS` pour ouvrir la session en transaction lecture
seule : tout `UPDATE`/`DELETE`/`INSERT`/DDL y échoue avec
`cannot execute ... in a read-only transaction`, même par erreur de frappe.

Requête courte :

```bash
ssh belive-tn "kubectl exec -i postgres-0 -n gisv2 -- env PGOPTIONS='-c default_transaction_read_only=on' psql -U postgres -d gis_v2 -X -v ON_ERROR_STOP=1 -c \"SELECT count(*) FROM vehicles WHERE company_id = 14\""
```

Requête longue (heredoc → stdin traverse ssh puis kubectl exec -i) :

```bash
ssh belive-tn "kubectl exec -i postgres-0 -n gisv2 -- env PGOPTIONS='-c default_transaction_read_only=on' psql -U postgres -d gis_v2 -X -v ON_ERROR_STOP=1" <<'SQL'
SELECT s.id, s.name, st.code, s.billing_cycle, s.next_payment_amount, s.subscription_expires_at
FROM societes s
LEFT JOIN subscription_types st ON st.id = s.subscription_type_id
ORDER BY s.id;
SQL
```

Sur DZ (serveur de test) : même commande via `ssh icosnet-dz "sudo kubectl exec -i postgres-0 -n gisv2 -- ..."`.

Options utiles : `-A -F ';'` pour un CSV brut, `\x` (mode étendu) pour une
ligne large, `-t` pour supprimer les en-têtes. Jamais `psql -it` interactif
depuis un script.

## 2. Lister les colonnes AVANT de requêter (obligatoire)

Le nommage des colonnes est **incohérent** d'une table à l'autre. Ne devine
jamais un nom de colonne : liste-le.

```sql
SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'vehicles'
ORDER BY ordinal_position;
```

Pour retrouver une table : `SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY 1;`

Le piège, avec des exemples réels :

| Table | Convention | Exemples |
|---|---|---|
| `vehicles` | snake_case… | `company_id`, `mileage`, `plate_number`, `gps_device_id` |
| `vehicles` (échéances) | …SAUF PascalCase entre guillemets | `"InsuranceExpiry"`, `"TaxExpiry"`, `"TechnicalInspectionExpiry"` |
| `vehicle_costs`, `maintenance_logs`, `fuel_entries`, `societes`, `subscription_types`, `gps_positions`, `gps_alerts` | snake_case | `company_id`, `amount`, `odometer_km`, `recorded_at` |
| `notifications`, `audit_logs`, `driver_scores`, `driving_events` | PascalCase entre guillemets | `"CompanyId"`, `"IsRead"`, `"Timestamp"`, `"CreatedAt"` |

Sans guillemets, Postgres replie l'identifiant en minuscules : `WHERE CompanyId = 14`
donne `column "companyid" does not exist`. Écris `WHERE "CompanyId" = 14`.

```sql
-- vehicles : mélange des deux conventions dans la MÊME table
SELECT id, plate_number, mileage, "InsuranceExpiry", "TechnicalInspectionExpiry"
FROM vehicles WHERE company_id = 14 ORDER BY id;

-- notifications : tout en PascalCase
SELECT "Id", "Type", "IsRead", "CreatedAt"
FROM notifications WHERE "CompanyId" = 14 ORDER BY "CreatedAt" DESC LIMIT 20;
```

## 3. Règles de requête

1. **SELECT, EXPLAIN, `\d`, `\dt` uniquement.** Rien d'autre ne sort de cette skill.
2. **Toujours un `LIMIT`** sur une table d'historique, et un filtre société
   (`company_id` / `"CompanyId"`) : la base est multi-tenant, ne mélange pas
   les clients dans un même résultat sans raison.
3. **Grosses tables** : `gps_positions` (~15 Go, > 12 M lignes) et `gps_alerts`
   (~7,5 Go, noyée de télémétrie). Jamais de balayage sans filtre
   `device_id` + borne temporelle sur `recorded_at`. Pour « la dernière trame
   par boîtier », préférer un `LATERAL ... ORDER BY recorded_at DESC LIMIT 1`
   à un `GROUP BY` + `max()`.
4. **Piège `gps_alerts.timestamp`** : son `min()` est en 2004 (horloges de
   boîtiers corrompues). Pour dater une ligne, utiliser `created_at` ou `id`,
   jamais `timestamp` seul.
5. **Performance d'une requête de l'API** : passer la requête réelle (celle du
   log EF ou reconstituée) sous `EXPLAIN (ANALYZE, BUFFERS)` — elle s'exécute
   vraiment, donc uniquement sur un SELECT, ce que la session lecture seule
   garantit. Lire `rows=` réels vs estimés, `Seq Scan` sur une grosse table,
   `Buffers: shared read`. Contexte : `/vehicles/with-positions` est LE chemin
   chaud (interrogé toutes les ~30 s par chaque utilisateur) — une requête
   « seulement 250 ms » y ralentit toute l'application.
6. **Données personnelles** : ne remonte que les colonnes nécessaires
   (pas d'emails/téléphones en masse), et n'en écris aucune dans un fichier du
   dépôt. Les extractions vont dans le scratchpad.
7. Une question de schéma (« la colonne X existe-t-elle ? ») se répond par
   `information_schema.columns`, pas par un `dotnet ef`.

Requêtes de repérage fréquentes :

```sql
-- Sociétés, plan et échéance d'abonnement
SELECT s.id, s.name, st.code AS plan, s.billing_cycle, s.next_payment_amount,
       s.subscription_expires_at, (SELECT count(*) FROM vehicles v WHERE v.company_id = s.id) AS parc
FROM societes s LEFT JOIN subscription_types st ON st.id = s.subscription_type_id
ORDER BY s.id;

-- Véhicules d'une société, avec ou sans boîtier
SELECT id, plate_number, mileage, gps_device_id IS NOT NULL AS a_un_boitier
FROM vehicles WHERE company_id = <id> ORDER BY id;

-- Dernière position par boîtier d'une société (LATERAL, pas de GROUP BY)
SELECT v.id, v.plate_number, p.recorded_at, p.latitude, p.longitude
FROM vehicles v
JOIN LATERAL (SELECT recorded_at, latitude, longitude FROM gps_positions gp
              WHERE gp.device_id = v.gps_device_id ORDER BY recorded_at DESC LIMIT 1) p ON true
WHERE v.company_id = <id> AND v.gps_device_id IS NOT NULL;

-- Volumétrie des tables (sans les parcourir)
SELECT relname, n_live_tup, pg_size_pretty(pg_total_relation_size(relid)) AS taille
FROM pg_stat_user_tables ORDER BY pg_total_relation_size(relid) DESC LIMIT 15;
```

## 4. Restituer

Pour chaque réponse : la requête exécutée (bloc de code), le résultat
(tableau ou chiffres), et l'interprétation en une ou deux phrases. Signale
explicitement ce qui t'a surpris (colonne inattendue, valeur incohérente comme
un `settings->>'Currency'` à `DT`), c'est souvent la vraie réponse à la question.

## 5. Si une écriture est nécessaire

Tu ne l'exécutes pas. Tu la **proposes** à Slim avec les chiffres, puis tu
attends une approbation explicite de **cette opération précise**.

1. **Proposer** : table, nombre exact de lignes (par `SELECT count(*)` avec les
   mêmes garde-fous que l'ordre prévu), identifiants, montants ou libellés
   concernés, méthode (DELETE / UPDATE ciblé), plan de restauration (le
   fichier JSON de sauvegarde + la sauvegarde nocturne `pg_dump` du volume
   `/backups`). Pour une purge de volume, ajouter la volumétrie et rappeler
   l'écran `/admin/database` (sauvegarde `presuppr_` puis suppression par
   lots) plutôt qu'un DELETE manuel.
2. **Attendre** l'accord explicite (« ok enlève les doublons » = oui pour LES
   doublons chiffrés, rien d'autre). Un « continue » n'est pas un accord.
3. **Re-vérifier** juste avant d'agir (les lignes peuvent avoir bougé).
4. **Sauvegarder les lignes** dans le scratchpad, jamais dans le dépôt :

   ```bash
   ssh belive-tn "kubectl exec -i postgres-0 -n gisv2 -- psql -U postgres -d gis_v2 -X -A -t -c \"SELECT row_to_json(t) FROM vehicle_costs t WHERE id IN (123, 456) AND company_id = 14\"" > "<scratchpad>/sauvegarde_vehicle_costs_$(date +%Y%m%d-%H%M%S).json"
   ```

   Contrôler que le fichier contient bien le nombre de lignes attendu.
5. **Exécuter dans une transaction avec garde-fous complets** — c'est la
   SEULE commande de cette skill sans `PGOPTIONS` lecture seule. Le WHERE
   porte l'identifiant ET la société ET le libellé ET le montant ; un bloc
   `DO $$` compare `ROW_COUNT` à l'attendu et annule tout si ça diffère.
   Le hook `guard-bash` bloquera cette commande (`DELETE` via `psql`) : soit Slim
   l'exécute lui-même, soit il crée lui-même `.claude/hooks/.unlock-destructive-sql`
   (fenêtre de 15 min) — Claude ne crée jamais ce fichier.
   Premier passage avec `ROLLBACK` pour voir l'effet, second avec `COMMIT` :

   ```bash
   ssh belive-tn "kubectl exec -i postgres-0 -n gisv2 -- psql -U postgres -d gis_v2 -X -v ON_ERROR_STOP=1" <<'SQL'
   BEGIN;
   DO $$
   DECLARE n integer;
   BEGIN
     DELETE FROM vehicle_costs
      WHERE id IN (123, 456)
        AND company_id = 14
        AND description = 'FILTRE CLIM'
        AND amount = 85.00;
     GET DIAGNOSTICS n = ROW_COUNT;
     IF n <> 2 THEN
       RAISE EXCEPTION 'Attendu 2 lignes, obtenu % : annulation', n;
     END IF;
   END $$;
   SELECT count(*) AS restantes FROM vehicle_costs WHERE id IN (123, 456);
   ROLLBACK;   -- remplacer par COMMIT au second passage, une fois le compte vérifié
   SQL
   ```

   Même gabarit pour un `UPDATE` de recalage (avec `AND colonne IS DISTINCT FROM nouvelle_valeur`
   dans le WHERE pour que le compte attendu soit exact).
6. **Vérifier après** par un SELECT (lignes absentes / valeurs recalées) et
   consigner dans le compte rendu : ids, nombre, chemin du fichier de sauvegarde.

Un changement de schéma (colonne, table, index) n'est pas une écriture ad hoc :
il passe par un fichier `migrations/0NN_*.sql` (`/migration`) joué par `/deploy`
AVANT le pod API.

## Ne jamais

- Exécuter `UPDATE`, `DELETE`, `INSERT`, `TRUNCATE`, `DROP`, `ALTER`, `VACUUM FULL`
  de sa propre initiative, même sur des lignes « manifestement » inutiles.
- Considérer un « continue », un « ok » ou un « vas-y » générique comme une
  autorisation d'écrire : l'accord porte sur une opération chiffrée précise.
- Omettre `PGOPTIONS='-c default_transaction_read_only=on'` sur une session de lecture.
- Deviner un nom de colonne sans avoir listé `information_schema.columns`.
- Balayer `gps_positions` ou `gps_alerts` sans filtre boîtier + borne temporelle, ni sans LIMIT.
- Dater `gps_alerts` sur la colonne `timestamp` (horloges corrompues, min en 2004).
- Écrire un extrait de base (surtout emails, téléphones, montants clients) dans un fichier du dépôt.
- Utiliser `dotnet ef` pour lire ou modifier le schéma de prod.
- Lancer `docker system prune -a --volumes` ou toute commande de nettoyage sur
  le serveur au passage : les anciens volumes docker-compose contiennent
  peut-être une ancienne base.
