# CLAUDE.md — cerveau partagé du projet GISV2

Lu automatiquement par Claude Code au démarrage de chaque session. Dense et actionnable :
les règles ci-dessous priment sur les habitudes par défaut. Prose en français, commandes telles quelles.

## Ce qu'est le projet

GISV2 est une plateforme SaaS de gestion de flotte (suivi GPS temps réel, géofences, entretien,
carburant, dépenses, rapports, IA) vendue sous la marque **Calypso**. Deux déploiements K3s :
**TN** (production client, `belive-calypso.com`) et **DZ** (serveur de test). Monorepo :

| Dossier | Rôle |
|---|---|
| `services/gis-frontend/` | Angular 21 standalone (zoneless corrigé, voir pièges), `ng serve` sur 4200 |
| `services/GisAPI/` | ASP.NET Core 9, point d'entrée de l'API (`Program.cs`, contrôleurs fins) |
| `services/src/` | Couches .NET : `GisAPI.Domain` / `Application` / `Infrastructure` / `Shared` (CQRS MediatR) |
| `services/tests/GisAPI.Tests/` | Le SEUL projet de tests backend (383 tests xUnit) |
| `services/gps-ingest-rust/` | Ingestion télémétrie TCP/UDP (NEMS, Noron, Teltonika, GT06, Coban) |
| `services/gis-mobile/` | Ionic 8 / Capacitor (app Android « Calypso », AAB dans `releases/`) |
| `services/shared-kernel/` | Protocol Buffers inter-services |
| `migrations/` | **Schéma de production** : fichiers SQL numérotés `0NN_slug.sql`, appliqués à la main |
| `scripts/init-local-db.sh` | Initialise la base locale : schéma copié du serveur de test DZ + `migrations/*.sql`, aucune donnée |
| `k8s/` | Manifestes K3s prod + `update.sh`, `README.md`, `SSL-TN-RUNBOOK.md` |
| `docs/architecture/` | Notes d'architecture (CQRS, flux GPS, permissions) |

## Lancer en local

Prérequis : .NET 9 SDK, Node 22 LTS, Docker, Rust (optionnel, seulement pour l'ingest).

```bash
# 1. Infra seule (Postgres sur 5433, RabbitMQ 5672/15672, Redis 6379)
docker compose -f docker-compose.dev.yml up -d

# 2. Schéma de la base locale (accès SSH au serveur de test DZ requis)
bash scripts/init-local-db.sh
#    Copie le schéma SEUL de DZ (aucune donnée) puis rejoue migrations/*.sql.
#    Obligatoire : MigrateAsync ne sait PAS créer une base de zéro (vérifié : dotnet run plante en 42703
#    sur 20260707190000_AddAlertPrefsConfigured, puis sur price_per_vehicle tant que 039 n'est pas jouée).

# 3. API — http://localhost:5020 (Properties/launchSettings.json, profil "http")
cd services/GisAPI && dotnet run
#    Attendu : « No pending migrations » + seed des comptes (Program.cs ~l.512).
#    Sans RabbitMQ/Redis l'API démarre (consommateurs en retry) mais pas de temps réel.
#    Firebase/Groq : placeholders tolérés en local (push et IA désactivés).

# 4. Frontend — http://localhost:4200, apiUrl = '/api' RELATIF
cd services/gis-frontend && npm install && npm start
#    npm start = ng serve --proxy-config proxy.conf.json : /api et /uploads → http://localhost:5020
#    (WebSocket SignalR inclus). Ne JAMAIS mettre une URL absolue dans environment.ts.

# 5. Tests
dotnet test services/tests/GisAPI.Tests/        # backend (383 tests)
cd services/gis-frontend && npx jest             # frontend (jest-preset-angular, *.spec.ts)

# 6. Ingest Rust (optionnel)
cd services/gps-ingest-rust && cargo run         # ports config/listeners.yaml, REST 3000
```

Comptes seed (Program.cs, publics) : `admin@belive.tn` / `Admin@2026` (société Belive) et
`admin@transporttest.tn` / `Admin@2026` (société TransportTest). Jamais de compte client réel en test.

`docker-compose.yaml` à la racine = pile COMPLÈTE conteneurisée (API exposée sur 5000, front 4200,
RabbitMQ sur 5673) : utile pour une démo, pas pour le dev quotidien. `docker-compose.prod.yml` est
un vestige — la prod tourne sur K3s (`k8s/`).

**Worktrees** : toujours builder/tester depuis le chemin du worktree courant (compiler depuis
`C:/Users/Mega-PC/Desktop/GISV2` ne valide rien : vieux code). Un worktree n'a pas de `node_modules` :
`New-Item -ItemType Junction` vers ceux du checkout principal, ou `npm install`.

## Ports

| Service | Dev local | Notes |
|---|---|---|
| Angular | 4200 | proxy `/api` → 5020 |
| API .NET | **5020** | `dotnet run` ; 5000 seulement dans les conteneurs |
| PostgreSQL (PostGIS 16) | **5433** hôte → 5432 conteneur | base `gis_v2`, user `postgres` |
| RabbitMQ | 5672 (management 15672) | échanges `telemetry.raw`, `gis.gps`, `gis.alerts`, `gis.events` |
| Redis | 6379 | cache + pub/sub SignalR |
| Rust REST | 3000 | `/health`, `POST /commands/push` |
| GPS TCP | 6100 NEMS L, 6200 NEMS S, 6210 NEMS S bis, 6400 Teltonika, 6500 GT06, 6600 Coban | `hostPort` sur le nœud en prod |
| GPS UDP | 6300 Noron NR024 | |
| Valhalla / Nominatim | 8002 / 8088 | routage + géocodage (prod : services K8s) |

## Architecture

### Backend : CQRS avec MediatR
1. **Domain** (`src/GisAPI.Domain/`) — entités, value objects, événements de domaine (non dispatchés).
   Entités centrales : `Vehicle`, `Position`, `Geofence`, `Notification`, `Maintenance`, `Societe`.
2. **Application** (`src/GisAPI.Application/`) — Commands/Queries + Handlers sous `Features/<Feature>/`,
   interface `IGisDbContext`, behaviors de pipeline (validation, logs, autorisation).
3. **Infrastructure** (`src/GisAPI.Infrastructure/`) — EF Core `GisDbContext` (`Persistence/`), consommateurs
   RabbitMQ (`Messaging/`), Redis, multi-tenant (`MultiTenancy/CurrentTenantService`).
4. **API** (`services/GisAPI/Controllers/`) — ~60 contrôleurs fins qui dispatchent MediatR. Aucune logique métier.
   Les services d'arrière-plan (détection d'accident, capteurs, sauvegardes) vivent dans `services/GisAPI/Services/`.

Multi-tenant : filtres de requête `companyId` sur `GisDbContext` — toute requête est scopée au tenant courant
(`IgnoreQueryFilters()` uniquement pour le seed/admin système). Le préfixe `/api/admin` est gardé par
`PermissionMiddleware` (system_admin) ; les mutations sensibles portent `[RequireAdmin]`.

Nouvelle fonctionnalité backend : entité Domain → Command/Query + Handler → configuration EF dans
`Infrastructure/Persistence/Configurations/` + `DbSet` dans `GisDbContext`, `IGisDbContext` **et**
`tests/.../Common/TestGisDbContext.cs` (oubli = tests qui ne compilent plus) → contrôleur fin →
**migration SQL** dans `migrations/` (voir Base de données).

### Frontend : Angular orienté services (pas de NgRx)
- `ApiService` (`services/api.service.ts`, très gros) — TOUS les appels HTTP. Un nouvel endpoint va là.
- `AuthService` — JWT via `BehaviorSubject<AuthUser>` ; `localStorage` : `auth_token`, `refresh_token`,
  `user_data` (espace client) et `admin_token` (espace `/admin`). Une route client ne prend JAMAIS `admin_token`.
- `SignalrService` — positions temps réel via le hub `/api/hubs/gps`.
- `PermissionService` + `FeatureGuard` — gating par abonnement (`ModuleKey`, `data: { feature: 'module_key' }`
  dans `app.routes.ts`). Les clés de rapport non mappées sont **fail-closed**.
- `authInterceptor` — refresh automatique sur 401 et refresh proactif avant expiration ; saute `/assistant/`.
- Réglages par déploiement (marque, devise, `selfSignup`, `aiAssistantLanding`, `subscriptionModule`,
  `simOperators`) : `src/environments/environment.ts` — **`environment.prod.ts` est du code mort**
  (pas de `fileReplacements`). Chaque serveur garde SA copie locale de `environment.ts`.

Nouveau module frontend : composant standalone sous `src/components/` → route(s) avec `AuthGuard` +
`FeatureGuard` → méthodes dans `ApiService` → `ModuleKey` dans `PermissionService` si gaté.

### Flux temps réel
```
Boîtier GPS → Rust ingest (TCP/UDP) → PostgreSQL + RabbitMQ
                                            ↓
                         GpsTelemetryConsumer (.NET) → Redis PubSub
                                            ↓
                         SignalR Hub → Angular (monitoring, cloche hors-ligne)
```
`/vehicles/with-positions` est LE chemin chaud (pollé ~30 s par page et par utilisateur) : y agréger en
SQL (LATERAL / fenêtres), jamais matérialiser des dizaines de milliers de lignes en C#.

### Exports et IA
Excel : **ClosedXML** côté API (`ReportsController.cs` montre le motif) ; PDF : jsPDF côté front, QuestPDF côté
API. IA : `AiChatController` + `GroqLlmService` (Groq, function calling). Le tier Groq est plafonné à
**8000 tokens/minute** : `max_tokens` ≤ 2500 et prompt borné, sinon 413/429.

## RÈGLES NON NÉGOCIABLES

> **1. Données de production.** JAMAIS de `DELETE` / `DROP` / `TRUNCATE` / `UPDATE` de masse / `VACUUM FULL`
> sur TN ou DZ de sa propre initiative — même sur du « junk » évident. Une purge se PROPOSE avec les
> chiffres (lignes, montants, méthode, plan de restauration). Un « continue » n'est PAS une autorisation.
> Si l'utilisateur autorise explicitement : sauvegarder les lignes (`row_to_json` → fichier), `DELETE` dans
> une transaction avec garde-fous complets (id + société + libellé + montant) et un bloc `DO $$` qui
> `RAISE EXCEPTION` si `ROW_COUNT` diffère de l'attendu, puis vérifier. Le hook bloque le `DELETE` : l'exécution
> passe par Slim lui-même (terminal) ou par le fichier de déverrouillage `.claude/hooks/.unlock-destructive-sql`
> qu'il crée LUI-MÊME (fenêtre 15 min) — Claude ne le crée jamais.
>
> **2. Boîtiers GPS.** JAMAIS de commande STOP (coupure moteur). Seules les commandes GO sont permises ; le
> filtre `command_type = 'GO'` dans `gps-ingest-rust/src/db.rs` doit rester en place.
>
> **3. Migrations EF.** JAMAIS `dotnet ef migrations add` ni `dotnet ef database update` contre une base
> réelle. Le snapshot EF est en dérive massive : la migration générée voulait DROP des tables de prod.
> Le schéma évolue UNIQUEMENT via `migrations/0NN_slug.sql`.
>
> **4. `environment.ts`.** JAMAIS copié vers un serveur (scp ni checkout) : il porte la marque, la devise et
> les interrupteurs propres à chaque déploiement. Idem pour `k8s/*.yaml` sur TN (modifs locales).
>
> **5. Secrets.** Aucun mot de passe, clé, jeton, IP de serveur dans le repo ni dans ce fichier — seulement
> les alias SSH (`belive-tn`, `icosnet-dz`), noms de pods/deployments et chemins. Les secrets prod vivent
> hors git (`.env.prod.example` liste les noms). `update.sh config` réapplique les secrets committés et
> ÉCRASE les valeurs patchées à chaud : ne pas le lancer.
>
> **6. Build.** Toujours compiler et tester depuis le worktree courant, jamais depuis un autre checkout.

Le hook `PreToolUse` `.claude/hooks/guard-bash.js` (déclaré dans `.claude/settings.json`) bloque mécaniquement :
`DELETE`/`DROP`/`TRUNCATE` via `psql`, `git stash pop` / `stash` nu / `clear`, `dotnet ef migrations add` /
`database update`, `git push` forcé, et `git pull` / `reset --hard` / `checkout` global dans un `ssh` vers
`belive-tn` ou `icosnet-dz`. Les autres règles (STOP boîtier, `environment.ts`, secrets) reposent sur toi.
Un blocage est un signal d'arrêt, pas un obstacle à contourner.

## Base de données

- **Schéma = `migrations/*.sql`** (dernier numéro : voir `ls migrations`), idempotents (`IF NOT EXISTS`),
  en-tête de commentaires expliquant le constat et le pourquoi. Utiliser `/migration` pour en créer une.
- **Ordre de déploiement** : jouer le SQL AVANT le nouveau pod API. Une colonne mappée par EF absente en base
  = `42703 column does not exist` → le login casse pour tout le monde. Le SQL doit passer sur DZ puis TN.
- Une cinquantaine de migrations EF sous `Infrastructure/Persistence/Migrations/` (les 7 premières générées, le
  reste écrit à la main : `[Migration]` + `[DbContext]`, SQL idempotent) — héritage joué par `MigrateAsync` ;
  n'en ajoutez pas sans en discuter.
- **Piège de nommage — les colonnes sont INCOHÉRENTES** : `vehicles` est en snake_case (`company_id`,
  `plate_number`, `mileage`) SAUF les échéances en PascalCase entre guillemets (`"InsuranceExpiry"`,
  `"TaxExpiry"`, `"TechnicalInspectionExpiry"`). `vehicle_costs`, `maintenance_logs`, `fuel_entries`,
  `societes`, `subscription_types`, `accident_events` = snake_case. `notifications`, `audit_logs`,
  `driver_scores`, `driving_events`, `tours` = PascalCase entre guillemets (`"CompanyId"`, `"IsRead"`,
  `"Timestamp"`). Toujours lister `information_schema.columns` avant de requêter.
- Accès prod en lecture seule : `kubectl exec -i postgres-0 -n gisv2 -- psql -U postgres -d gis_v2`
  (`-i` seul, jamais `-it` avec un heredoc ; `SET statement_timeout='30s'` par défaut, `'180s'` explicitement
  pour un `EXPLAIN ANALYZE` lourd). Passer par `/prod-db` : lecture par défaut (session `PGOPTIONS` read-only),
  écriture seulement via son protocole ; l'agent `prod-db-readonly` n'émet que du SELECT.
- Jointure préférée positions ↔ véhicules : `vehicles.gps_device_id = gps_positions.device_id`.
- `gps_alerts.timestamp` contient des horloges boîtier corrompues (min 2004) : jamais de purge par âge dessus.
- Sauvegardes : écran `/admin/database` + CronJob `db-backup` (pg_dump zstd:9, rotation APRÈS validation).

## Déploiement

Procédure détaillée et vérifications : **`/deploy`**. L'essentiel :

- **DZ = test par défaut** (alias `icosnet-dz`). `~/GISV2` n'est PAS un dépôt git : `scp` des fichiers
  modifiés (+ le `.csproj` si une dépendance a bougé) puis `sudo bash k8s/update.sh <api|frontend|ingest|mobile>`,
  lancé détaché (`nohup … &`) car la session SSH tombe sur les builds longs.
- **TN = production** (alias `belive-tn`), à déployer seulement quand c'est demandé. Pas de sudo non
  interactif → `update.sh` inutilisable. Voie qui marche : `git fetch origin master` ; contrôle par fichier
  `git diff --numstat --ignore-cr-at-eol <parent> -- <fichier>` VIDE sinon STOP ; `git checkout origin/master
  -- <fichiers précis>` ; build avec un **tag neuf horodaté** (`imagePullPolicy: IfNotPresent` : un tag déjà
  présent n'est jamais re-tiré) ; `docker push` sur `localhost:5000` (pas `registry.local:5000`, IPv6 refusé) ;
  `kubectl set image deployment/<frontend|gis-api> … registry.local:5000/gisv2/<image>:<tag> -n gisv2` ;
  `kubectl rollout status`. Rollback gratuit : `kubectl rollout undo`.
  JAMAIS `git pull` / `reset` / `checkout -- .` sur `~/GISV2` de TN (historique divergé, CRLF, modifs locales).
- Contextes de build : API = `services/` avec `GisAPI/Dockerfile` ; frontend = `services/gis-frontend/` avec
  `Dockerfile.prod` ; ingest = `services/gps-ingest-rust/` (image `gisv2/gps-ingest`, deployment `gps-ingest` —
  mêmes conventions que `gis-api` et `frontend`).
- La conf nginx du frontend vient du ConfigMap `frontend-nginx-config` (`k8s/02-applications.yaml`), pas de
  l'image ; `index.html` doit rester `no-cache`.
- Après déploiement : vérification LIVE via l'API (jeton `POST /api/auth/login` d'un compte de test de la
  société concernée), puis nettoyage des données de test injectées.

## Flux recette client

Détail dans **`/recette`** (agent `recette-investigator`) : capture d'écran annotée → investigation en trois
lentilles en parallèle (composant Angular / endpoints + handlers .NET / valeurs RÉELLES en base prod en lecture
seule) → correctif → build + tests depuis le worktree → commit `fix(scope): …` → `git push origin HEAD:master`
→ déploiement (SQL d'abord si migration) → vérification live → nettoyage → récapitulatif.

## Skills et agents disponibles (`.claude/`)

| Commande / agent | Quand |
|---|---|
| `/deploy <dz\|tn> <services>` | Déployer selon la procédure ci-dessus, avec les contrôles de sécurité |
| `/prod-db <question>` | Interroger la base TN/DZ en lecture seule, colonnes listées avant requête |
| `/migration <slug>` | Créer `migrations/0NN_slug.sql` idempotent + rappel de l'ordre SQL-avant-pod |
| `/recette` | Dérouler le flux recette client de bout en bout |
| agent `prod-db-readonly` | Accès SQL SELECT uniquement, pour les investigations sur données réelles |
| agent `recette-investigator` | Les trois lentilles d'investigation, en parallèle |

`.claude/settings.json` (versionné) autorise sans prompt les commandes en lecture seule et déclare le hook
de garde ; `settings.local.json` est personnel et ignoré par git.

## Conventions de commit

- Messages en français : `feat(scope): …` / `fix(scope): …` ; corps = constat + pourquoi (pas le comment).
- Terminer par le trailer `Co-Authored-By` de Claude Code (nom du modèle courant, ex.
  `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`).
- **master est la source de vérité de déploiement** : après un commit sur une branche de worktree,
  `git push origin HEAD:master`. Commit/push seulement à la demande de l'utilisateur.
- Pas de `--no-verify`, pas d'amend sur un commit poussé ; `bin/`, `obj/`, `dist/` ne se committent pas.

## Pièges connus (une ligne chacun, détails dans l'historique git)

- **Zoneless Angular 21** : `provideZoneChangeDetection()` en tête de `main.ts` est obligatoire ; sans lui
  l'écran se fige après un retour HTTP/`await` sans erreur console. Ne jamais le retirer.
- La région (vitrine Europe `/fr`) se déduit du NOM DE DOMAINE (`environment.europeanHostnames`) ou du pays
  de l'IP (`/api/public/region`), **jamais du fuseau horaire** (faux en Tunisie).
- Visibilité véhicules : `if (!isAdmin && userId > 0)` dans les handlers — un admin voit TOUT ; le statut
  admin est un drapeau explicite `IsCompanyAdmin`, jamais déduit de « toutes les permissions cochées ».
- Les boîtiers NEMS ne répondent pas aux commandes TCP (ils les exécutent en silence) ; confirmation par SMS.
- `power_voltage` ne mesure rien sur 88 % des boîtiers : l'affichage batterie est verrouillé par
  `gps_devices.voltage_sensor_reliable` sur les trois chemins (REST, fusion Redis, SignalR).
- `gps_alerts` n'accepte que `send_flag` 5..11 côté Rust (liste blanche) ; ne pas réélargir.
- Jamais `filter + order + First` par groupe sur `gps_positions` en EF : LATERAL « dernière trame » + filtre en C#.
- Mobile : autorité FileProvider = `${applicationId}.fileprovider` ; `versionCode` strictement croissant ;
  push FCM exige le secret K8s `firebase-sa` créé à la main sur chaque serveur.
