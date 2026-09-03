# GISV2 — plateforme de gestion de flotte

GISV2 est la plateforme SaaS de gestion de flotte commercialisée sous les marques Calypso, Bougeo et GPA selon le déploiement : suivi GPS temps réel, géorepérage, entretien, carburant, échéances, rapports et abonnements.
C'est un monorepo : API ASP.NET Core 9 (CQRS / MediatR), frontend Angular 21, application mobile Ionic, service d'ingestion GPS en Rust et schéma PostgreSQL / PostGIS.
Ce README sert à mettre un poste de développement en route. L'architecture, les commandes et les règles de travail détaillées sont dans [CLAUDE.md](CLAUDE.md), qui vaut aussi pour les humains.

## Organisation du dépôt

| Chemin | Rôle |
|---|---|
| `services/GisAPI/` | API REST ASP.NET Core 9 — point d'entrée (`Program.cs`), contrôleurs minces qui délèguent à MediatR |
| `services/src/` | Couches .NET : `GisAPI.Domain`, `GisAPI.Application` (commandes / requêtes MediatR), `GisAPI.Infrastructure` (EF Core, RabbitMQ, Redis, multi-tenant), `GisAPI.Shared` |
| `services/tests/GisAPI.Tests/` | Tests backend (`dotnet test`) |
| `services/gis-frontend/` | Frontend Angular 21 (composants standalone, un seul `ApiService` pour tous les appels HTTP) |
| `services/gis-mobile/` | Application mobile Ionic / Capacitor (Android, iOS) |
| `services/gps-ingest-rust/` | Ingestion télémétrie GPS : écoute TCP/UDP des boîtiers (NEMS, Noron, Teltonika, GT06, Coban) → PostgreSQL + RabbitMQ |
| `services/shared-kernel/` | Contrats Protocol Buffers entre services |
| `migrations/` | Migrations SQL manuelles, numérotées `0NN_slug.sql` — **seule** source d'évolution du schéma en production |
| `k8s/` | Manifestes K3s et scripts de déploiement (`k8s/README.md`) |
| `scripts/init-local-db.sh` | Initialise la base locale : schéma copié du serveur de test + migrations SQL |
| `docs/` | Architecture, schéma de données, protocoles GPS |
| `docker-compose.dev.yml` | Infrastructure locale seule (Postgres, RabbitMQ, Redis) — c'est le fichier du démarrage rapide |
| `docker-compose.yaml` | Pile complète en conteneurs (tout-Docker en dev) |
| `docker-compose.prod.yml` | Vestige de l'ancienne prod docker-compose ; la prod actuelle est K3s (`k8s/`) |
| `.claude/` | Outillage Claude Code partagé par l'équipe : skills, agents, hooks, réglages |

## Prérequis

- **Git** — sous Windows, Git for Windows fournit Git Bash, utilisé pour les boucles shell ci-dessous.
- **Docker Desktop** (Compose v2 : la commande est `docker compose`, avec un espace).
- **.NET 9 SDK** (`dotnet --version` doit afficher 9.x).
- **Node.js 22 LTS** (npm inclus) — la version utilisée par la CI et les images Docker.
- **Rust** via `rustup` — uniquement pour travailler sur l'ingest GPS (la CI le compile avec la toolchain `nightly`).

## Démarrage rapide

Trois commandes donnent une API qui tourne : `docker compose … up -d`, `bash scripts/init-local-db.sh`, `dotnet run`. Le frontend suit avec `npm start`. Chaque étape a été rejouée de bout en bout le 03/09/2026.

### 1. Cloner le dépôt

```bash
git clone <url-du-depot> GISV2
cd GISV2
```

### 2. Démarrer l'infrastructure locale

```bash
docker compose -f docker-compose.dev.yml up -d
docker compose -f docker-compose.dev.yml ps      # les trois services doivent passer "healthy"
```

Cela lance uniquement PostgreSQL/PostGIS (**port 5433**, base `gis_v2`, `postgres` / `postgres`), RabbitMQ (5672, console web http://localhost:15672 en `guest` / `guest`) et Redis (6379). Ces valeurs sont exactement celles attendues par défaut par `services/GisAPI/appsettings.json` ; ne changez l'un sans l'autre. Le port 5433 (et non 5432) évite tout conflit avec un PostgreSQL déjà installé sur le poste.

### 3. Initialiser le schéma de la base locale

```bash
bash scripts/init-local-db.sh        # Git Bash sous Windows ; nécessite l'accès SSH au serveur de test (alias icosnet-dz)
```

Le script copie le **schéma seul** (aucune donnée) de la base du serveur de test DZ, plus la table `__EFMigrationsHistory`, le charge dans `gisv2-postgres`, puis rejoue tous les fichiers `migrations/*.sql`. Sans accès SSH, demandez à un collègue le dump produit par le script (`/tmp/gisv2-schema-*.sql`) et chargez-le avec `docker exec -i gisv2-postgres psql -U postgres -d gis_v2 < fichier.sql`, puis lancez la boucle des migrations SQL ci-dessous.

Pourquoi passer par un serveur : l'API applique bien les migrations EF Core en attente à chaque démarrage, mais **la chaîne EF ne sait pas créer une base de zéro** (vérifié le 03/09/2026 : sur une base vide, `dotnet run` s'arrête à la migration `20260707190000_AddAlertPrefsConfigured`, qui suppose des colonnes ajoutées par `migrations/025_*.sql`), et le modèle EF mappe des colonnes que seuls les fichiers `migrations/*.sql` créent (`subscription_types.price_per_vehicle`, migration 039). Le schéma de référence est donc celui des serveurs, où les deux mécanismes se sont succédé.

**Mettre à jour sa base par la suite** : après un `git pull` qui apporte un nouveau fichier dans `migrations/`, rejouez la boucle (fichiers idempotents — `IF NOT EXISTS`, `UPDATE` conditionnels — un « already exists » est sans gravité) **avant** de relancer l'API :

```bash
for f in migrations/*.sql; do echo "== $f"; docker exec -i gisv2-postgres psql -U postgres -d gis_v2 -f - < "$f"; done
```

`gisv2-postgres` est le nom du conteneur fixé dans `docker-compose.dev.yml`. Pour repartir de zéro : `docker compose -f docker-compose.dev.yml down -v`, puis étapes 2 et 3.

### 4. Lancer l'API

```bash
cd services/GisAPI
dotnet run
```

L'API écoute sur **http://localhost:5020** (profil `http` de `Properties/launchSettings.json`) ; santé : http://localhost:5020/health.

Au démarrage, `Program.cs` applique les migrations EF Core encore en attente (normalement aucune après l'étape 3) puis **seed** ce qui manque : la société « Belive » et son administrateur, les plans d'abonnement, la société de test « TransportTest » et ses comptes, les types de carburant et le référentiel véhicules. Suivez les lignes `[Startup]` et `[Seed]` dans la console. Sans RabbitMQ ni Redis l'API démarre quand même (les consommateurs journalisent une erreur et attendent), mais sans temps réel.

### 5. Lancer le frontend

```bash
cd services/gis-frontend
npm install          # si npm se plaint de dépendances pair : npm install --legacy-peer-deps (comme la CI)
npm start
```

`npm start` exécute `ng serve --proxy-config proxy.conf.json` : tout ce qui part vers `/api` (y compris le hub SignalR `/api/hubs/gps`, WebSocket compris) et `/uploads` est relayé vers l'API sur le port 5020. Il n'y a donc ni CORS à configurer ni URL d'API à saisir — `environment.ts` garde son `apiUrl: '/api'` relatif, comme en production derrière nginx.

### 6. Se connecter

Ouvrez **http://localhost:4200** et utilisez l'un des comptes créés par le seed (ils n'existent qu'en local) :

| Compte | Mot de passe | Rôle |
|---|---|---|
| `admin@belive.tn` | `Admin@2026` | Administrateur plateforme (société Belive) — accès aux écrans `/admin` |
| `admin@transporttest.tn` | `Admin@2026` | Administrateur de la société de test TransportTest (vue « client ») |
| `operateur@transporttest.tn` | `User@2026` | Utilisateur standard de TransportTest (monitoring + véhicules seulement) |

Vérification rapide sans navigateur :

```bash
curl -s -X POST http://localhost:5020/api/auth/login -H "Content-Type: application/json" \
     -d '{"email":"admin@belive.tn","password":"Admin@2026"}'
```

### Optionnel : ingest GPS (Rust) et mobile

- **Ingest** : `cd services/gps-ingest-rust && cargo run`. Le binaire lit ses variables d'environnement depuis un fichier `.env` (ignoré par git) ; partez de `.env.docker` et du bloc `gps-ingest.environment` de `docker-compose.yaml` en remplaçant les noms d'hôtes Docker par `localhost` (Postgres sur **5433**). Les ports d'écoute des boîtiers sont dans `config/listeners.yaml` (6100, 6200, 6210, 6300/udp, 6400, 6500, 6600).
- **Mobile** : `cd services/gis-mobile && npm install && npm start` (Angular sur 4200 lui aussi : ne pas le lancer en même temps que le frontend, ou passer `-- --port 8100`).

### Récapitulatif des ports en local

| Service | Port | Lancé par |
|---|---|---|
| Frontend Angular | 4200 | `npm start` (proxy `/api` → 5020) |
| API .NET | 5020 | `dotnet run` |
| PostgreSQL / PostGIS | **5433** | `docker-compose.dev.yml` |
| RabbitMQ (AMQP / console) | 5672 / 15672 | `docker-compose.dev.yml` |
| Redis | 6379 | `docker-compose.dev.yml` |
| Ingest Rust (API HTTP / boîtiers) | 3000 / 6100–6600 | `cargo run` |

## Tests et build

```bash
dotnet test services/tests/GisAPI.Tests/GisAPI.Tests.csproj     # backend
cd services/gis-frontend && npx jest                             # frontend (jest.config.js)
cd services/gis-frontend && npm run build -- --configuration=production   # build AOT, comme la CI
cd services/gps-ingest-rust && cargo build                       # ingest
```

Pièges connus :

- **Worktrees git** : compilez et testez toujours depuis le chemin du worktree sur lequel vous travaillez, jamais depuis le checkout principal (il contient du vieux code, le build « passe » sans rien valider). Un worktree n'a pas de `node_modules` : `npm install`, ou une jonction vers ceux du checkout principal.
- **Angular 21 est zoneless par défaut** : `provideZoneChangeDetection()` est posé dans `src/main.ts` ; ne le retirez pas, tout le code est écrit en style zone (un écran qui ne se rafraîchit plus après un appel HTTP réussi, c'est ce piège).

## Travailler avec Claude Code

Le dépôt embarque l'outillage de l'équipe pour [Claude Code](https://claude.ai/code) ; il est chargé automatiquement à l'ouverture du projet.

- **`CLAUDE.md`** est lu à chaque session : architecture, commandes, patterns à suivre pour ajouter une fonctionnalité, règles non négociables. À lire aussi en tant qu'humain.
- **Skills** (commandes `/nom`, un dossier par skill dans `.claude/skills/<nom>/SKILL.md`) :
  - `/deploy` — déployer un service sur DZ (test) ou TN (production) en suivant la procédure validée (tag d'image horodaté, contrôles de sécurité, ordre SQL-avant-API, rollback) ;
  - `/prod-db` — consulter une base serveur en **lecture seule** (colonnes réelles, volumétrie) sans jamais rien modifier ;
  - `/migration` — créer une migration SQL numérotée et idempotente dans `migrations/`, avec son pendant côté EF ;
  - `/recette` — dérouler le flux « recette client » : constat → investigation → correctif → build et tests depuis le worktree → commit → déploiement → vérification live.

  Le frontmatter `description` de chaque `SKILL.md` précise quand l'utiliser.
- **Agents** (`.claude/agents/*.md`) : sous-agents spécialisés que Claude délègue (investigation, revue…) ; l'en-tête de chaque fichier dit quand y recourir.
- **Garde-fous** : `.claude/settings.json` (versionné) autorise sans confirmation les commandes en lecture seule et branche un hook `PreToolUse` (`.claude/hooks/guard-bash.js`) qui **bloque avant exécution** cinq familles de commandes : `DELETE`/`DROP`/`TRUNCATE` via `psql`, `git stash pop` / `stash` nu / `clear`, `dotnet ef migrations add` / `database update`, `git push` forcé, et `git pull` / `reset --hard` / `checkout` global dans un `ssh` vers `belive-tn` ou `icosnet-dz`. Le motif du refus est affiché à Claude, qui doit alors proposer au lieu d'exécuter. La commande STOP vers un boîtier et la copie d'`environment.ts` restent des règles de `CLAUDE.md` sans garde mécanique. `.claude/settings.local.json` est propre à chaque poste et ignoré par git.
- **Mémoire** : Claude Code conserve une mémoire personnelle **par machine** (`~/.claude/projects/…`), qui n'est pas partagée. Ce dépôt — `CLAUDE.md` et `.claude/` — est la couche commune : tout savoir utile à l'équipe doit y être écrit, pas seulement retenu localement.

## Règles à connaître

Le détail et le pourquoi sont dans `CLAUDE.md` ; l'essentiel tient en six points.

1. **Données de production** : jamais de `DELETE`, `DROP`, `TRUNCATE` ni `UPDATE` de masse de sa propre initiative. On propose avec des chiffres (lignes, montants) ; on n'exécute que sur ordre explicite, après sauvegarde des lignes, dans une transaction avec garde-fous.
2. **Schéma** : uniquement via `migrations/0NN_slug.sql`, idempotent, avec un en-tête qui explique le pourquoi, joué sur la base **avant** de déployer le pod API qui utilise la colonne (sinon l'API tombe en `column does not exist` et le login casse pour tout le monde). Jamais `dotnet ef migrations add` ni `dotnet ef database update` contre une base réelle : le snapshot EF est en dérive massive et la migration générée voudrait supprimer des tables de prod. Les migrations EF qui existent (une cinquantaine : les 7 premières générées, le reste écrit à la main, idempotent) sont un héritage appliqué par l'API à son démarrage — mais elles ne savent pas créer une base de zéro (d'où `scripts/init-local-db.sh`) ; n'en ajoutez pas sans en discuter.
3. **`services/gis-frontend/src/environments/environment.ts` est propre à chaque déploiement** (marque, devise, carte, drapeaux) et c'est le fichier réellement embarqué — `angular.json` n'a pas de `fileReplacements`, `environment.prod.ts` est mort. Ne jamais le copier vers un serveur.
4. **Colonnes Postgres au nommage incohérent** : `snake_case` dans la plupart des tables, `"PascalCase"` entre guillemets dans d'autres (`notifications`, `audit_logs`…) et même mélangé dans `vehicles`. Lister `information_schema.columns` avant d'écrire une requête.
5. **Boîtiers GPS** : jamais de commande STOP (coupure moteur) ; seules les commandes GO sont permises, le filtre est dans le Rust.
6. **Secrets** : jamais dans le dépôt. `.env.prod.example` est le modèle ; `.env.prod` et les clés de service sont ignorés par git.

## Déploiement

Deux serveurs : **DZ** (test, alias SSH `icosnet-dz`) reçoit par défaut le travail en cours ; **TN** (production, alias `belive-tn`) uniquement sur demande explicite. La procédure — build d'image avec un tag neuf horodaté, registre local, `kubectl set image`, `kubectl rollout status` / `undo`, migrations SQL jouées avant le pod API, fichiers locaux à ne jamais écraser — est portée par le skill `/deploy` et résumée dans `CLAUDE.md` ; les manifestes sont dans `k8s/` (`k8s/README.md`). Ne déployez pas « à la main » sans l'avoir lue.

## Pour aller plus loin

- `CLAUDE.md` — architecture, flux temps réel, patterns d'ajout de fonctionnalité.
- `docs/architecture/`, `docs/db-schema/`, `docs/protocols/` — conception, modèle de données, protocoles boîtiers.
- `k8s/README.md` — topologie de production K3s.
- `services/gis-frontend/README.md` — notes propres au frontend.
