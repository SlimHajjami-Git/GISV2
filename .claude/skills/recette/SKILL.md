---
name: recette
description: Traiter un point de recette client de bout en bout — capture ou description → investigation en 3 lentilles (composant Angular, endpoints/handlers .NET, valeurs réelles en base via /prod-db) → correctif → build et tests depuis le WORKTREE → commit en français → push origin HEAD:master → /deploy → vérification live via /api/auth/login d'un compte de test → nettoyage des données de test → récap pour le client. Utiliser dès qu'un client (aujourd'hui l'offre GPA sans GPS) signale un écart à l'écran.
---

# /recette <capture ou description du point client>

Point reçu : `$ARGUMENTS` (capture d'écran annotée, texte du client, ou les deux).
Si une image est jointe, la lire en premier et relever chaque annotation.

## Contexte : l'offre GPA (« gestion de parc sans GPS »)

Les recettes en cours portent sur des clients **sans boîtier GPS** : plan
`plan-basique` (« Plan Basique GPA »), module Monitoring éteint, saisie manuelle
(carburant, entretiens, réparations, dépenses, échéances) et rapports dérivés des
pleins saisis. Points structurants déjà livrés — s'y raccorder plutôt que
réinventer :

- Détection « sans GPS » = absence du module Monitoring dans l'abonnement /
  `Vehicle.GpsDeviceId` null. Le frontend masque alors tout ce qui suppose un
  boîtier (bloc `*ngIf="!hasGps"` du tableau de bord, onglets Geofences/Tournées,
  cloche hors-ligne, sections Limites vitesse / Contrôle à distance).
- Rapports GPS masqués par le plan (migration 037) ; rapport kilométrique et
  courbe carburant alimentés par `fuel_entries.odometer_km` / les pleins.
- Tarification **par véhicule** (migration 039, `subscription_types.price_per_vehicle`) :
  montant dû = prix du cycle × max(1, parc), recalculé à chaque lecture par
  `SubscriptionPricing` (source unique) ; `isTrial` renvoyé par
  `GET /api/subscriptions/current` ; essai = 7 jours partout.
- Comptes GPA facturés en EUR par défaut (migration 036) ; les clients installés
  gardent la monnaie locale. La devise affichée vient du compte (`user.currency`),
  `environment.defaultCurrency` n'est que le défaut pré-connexion.
- Import/export Excel (Paramètres > Données, `DataPortController`).
- Points encore ouverts avec le client : « pas de prix de carburant » (des prix
  sont pourtant configurés) et « message d'erreur carburant » — sans capture,
  demander la capture avant d'investiguer.

Société de recette et compte de test : fournis par Slim dans la session (sur TN,
la société de recette est un compte de test). Ne jamais les écrire dans un fichier.

## 0. Cadrer (2 minutes)

1. Reformuler en une phrase : **attendu** (ce que le client voulait voir) vs
   **constaté** (ce que montre la capture), avec l'écran (route Angular) et,
   si visible, la valeur fautive exacte (« 299,00 EUR », « 0 entretien »).
2. Identifier la société de recette (id) et le compte de test à utiliser.
3. Si la capture ne permet pas de savoir de quel écran ou de quelle valeur il
   s'agit : poser UNE question précise (ou demander la capture) et s'arrêter là.

## 1. Investigation en 3 lentilles (en parallèle)

Proposer de lancer **trois agents en parallèle**, un par lentille, chacun avec
le cadrage ci-dessus et l'ordre de rapporter fichier:ligne + valeurs. Si les
agents ne sont pas disponibles, dérouler les trois lentilles soi-même, dans
cet ordre. Aucune conclusion sans la lentille C : les valeurs en base sont
réelles, jamais supposées.

**Lentille A — composant Angular** (`services/gis-frontend/src/`)
- Trouver le composant : `grep -n "<segment-de-route>" src/app.routes.ts` puis
  `src/components/<composant>.component.ts` (composants standalone, template inline).
- Remonter la donnée : template → propriété/signal → méthode `ApiService`
  (`src/services/api.service.ts`, TOUS les appels HTTP y sont) → endpoint.
- Vérifier ce qui masque ou transforme : `*ngIf`/`@if` liés à `hasGps`,
  `PermissionService.hasFeature/hasReportAccess` (fail-closed : une clé non
  mappée est refusée), pipe `appCurrency`, arrondis.
- Pièges connus : (1) zoneless — l'écran ne se rafraîchit pas après un `await`
  ou une réponse HTTP si l'état n'est pas un signal (`provideZoneChangeDetection`
  est posé dans `main.ts`, mais vérifier le composant) ; (2) `environment.ts`
  est PAR SERVEUR (TN a sa copie locale, différente de git) : un drapeau vu
  dans git n'est pas forcément celui de TN ; (3) une réponse API peut être un
  tableau OU `{ items: [...] }`.

**Lentille B — endpoints et handlers .NET**
- Contrôleur : `services/GisAPI/Controllers/<X>Controller.cs` (mince, dispatch
  MediatR) → handler : `services/src/GisAPI.Application/Features/<Feature>/…Handler.cs`
  → DTO renvoyé.
- Vérifier : DTO de LISTE vs DTO de DÉTAIL (précédent : les champs leasing
  n'existaient que dans `/vehicles/{id}`, l'écran Dépenses partait de la liste),
  filtre tenant (`company_id`), gating `PermissionMiddleware` (court-circuité
  si la société n'a PAS de plan), calcul de montant (`SubscriptionPricing`),
  arrondis décimaux, valeurs par défaut du seed (create-only, ne corrige jamais l'existant).
- Lire les tests existants du handler dans `services/tests/GisAPI.Tests/` : ils
  disent ce qui est garanti et ce qui ne l'est pas.

**Lentille C — valeurs réelles en base** (`/prod-db`, lecture seule)
- Pour la société de recette : plan et cycle (`societes` ↔ `subscription_types`),
  parc (`vehicles`), lignes de l'écran concerné (`fuel_entries`, `vehicle_costs`,
  `maintenance_logs`, échéances `"InsuranceExpiry"`…), et la valeur exacte que
  l'écran affiche (elle vient toujours de quelque part).
- Lister `information_schema.columns` avant chaque table (nommage incohérent).
- Comparer chiffre à chiffre avec la capture : l'écart dit où est le bug
  (donnée fausse en base ≠ calcul faux ≠ affichage masqué).

**Synthèse** avant de coder : cause racine en une phrase, preuve
(fichier:ligne + valeur en base), correctif proposé, effets de bord (autres
écrans qui lisent la même donnée, autres sociétés). Si la cause est une donnée
de prod à corriger : proposer via `/prod-db` (chiffres), ne pas exécuter.

## 2. Corriger

- Correctif minimal, dans le worktree courant, en suivant les patrons du dépôt :
  HTTP dans `ApiService`, contrôleurs minces, logique dans les handlers,
  masquage GPS via la détection `hasGps` existante, une seule source de calcul.
- Nouvelle colonne/table → `/migration` (SQL numéroté idempotent + entité +
  les DEUX mappings EF). Réglage par serveur (essai, devise, marque) →
  `environment.ts` ou variable d'environnement du pod, appliqués par `sed` /
  `kubectl set env` lors du `/deploy`, jamais en dur dans le code.
- Ajouter ou ajuster un test dans `services/tests/GisAPI.Tests/` quand la logique
  d'un handler change ; le test reproduit d'abord le constat client.

## 3. Build et tests DEPUIS LE WORKTREE

Piège du 01/09 : compiler depuis le checkout principal
(`C:/Users/Mega-PC/Desktop/GISV2`) alors qu'on travaille dans un worktree ne
valide RIEN (vieux code). Contrôler d'abord où l'on est :

```bash
git rev-parse --show-toplevel     # doit afficher le chemin du worktree, pas Desktop/GISV2
```

Puis, en chemins absolus vers le worktree :

```bash
cd <worktree>/services/GisAPI && dotnet build
cd <worktree>/services/GisAPI && dotnet test ../tests/GisAPI.Tests/
cd <worktree>/services/gis-frontend && npx ng build --configuration=production
```

- `node_modules` absent du worktree → jonction vers ceux du checkout principal
  (PowerShell) : `New-Item -ItemType Junction -Path <worktree>\services\gis-frontend\node_modules -Target C:\Users\Mega-PC\Desktop\GISV2\services\gis-frontend\node_modules`
  (ou `npm install`).
- Tests : 2 échecs `Suppliers` préexistants sont connus et hors périmètre ;
  tout AUTRE échec bloque.
- DLL verrouillée au build : arrêter le `GisAPI` qui tourne, ou `dotnet build --no-dependencies`.
- Pour voir l'écran en local : `dotnet run` dans `services/GisAPI` (port 5020,
  base locale sur 5433, compte seed `admin@belive.tn` / `Admin@2026`) et
  `npx ng serve --proxy-config proxy.conf.json` dans `services/gis-frontend`
  (port 4200, `/api` proxifié vers 5020).

## 4. Commit en français

`git status` du worktree liste `services/GisAPI/bin/` et `obj/` comme modifiés
(fichiers suivis par erreur) : **ajouter les fichiers par leur nom**, jamais
`git add -A` ni `git add .`.

```bash
git add <fichiers précis>
git commit -F - <<'MSG'
fix(scope): titre court, à l'indicatif, sans point final

Recette client JJ/MM/AAAA : ce que l'écran montrait (valeur exacte) et ce que
le client attendait. Cause réelle en une ou deux phrases (fichier ou table).

Ce que fait le correctif, et ce qu'il ne change volontairement pas.
Vérifié en conditions réelles (société <id>, <comment>).

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
MSG
```

`feat(scope)` pour une fonctionnalité nouvelle, `fix(scope)` pour un écart ;
scope = écran ou domaine (`abonnement`, `depenses`, `tableau de bord`, `carburant`).

## 5. Pousser sur master

```bash
git push origin HEAD:master
```

Si le push est refusé (non fast-forward) : `git fetch origin && git rebase origin/master`,
relancer build + tests, pousser à nouveau. Jamais `--force`.

## 6. Déployer

`/deploy dz <frontend|api|both>` par défaut ; `/deploy tn …` seulement si Slim le
demande (la société de recette GPA est sur TN : demander avant). Une migration
SQL se joue AVANT le pod API — la skill `/deploy` s'en charge.

## 7. Vérifier en LIVE via l'API

Jeton par login d'un **compte de test** de la société de recette (identifiants
donnés par Slim dans la session ; jamais un compte client réel, jamais dans un fichier) :

```bash
ssh belive-tn "curl -s -X POST http://127.0.0.1/api/auth/login -H 'Content-Type: application/json' -d '{\"email\":\"<compte-test>\",\"password\":\"<mdp>\"}'" > "<scratchpad>/login.json"
TOKEN=$(node -e "console.log(JSON.parse(require('fs').readFileSync(process.argv[1],'utf8')).token)" "<scratchpad>/login.json")
ssh belive-tn "curl -s http://127.0.0.1/api/subscriptions/current -H 'Authorization: Bearer $TOKEN'"
```

(sur DZ : `ssh icosnet-dz`, même chemin `/api/...`). Comparer la réponse à
l'attendu de l'étape 0, chiffre à chiffre. Puis :

```bash
ssh belive-tn 'kubectl logs deployment/gis-api -n gisv2 --tail=100 | grep -iE "exception|error|42703" || echo "aucune erreur"'
```

Si le point est purement visuel, ouvrir l'écran dans le navigateur avec le
compte de test (Ctrl+Shift+R pour être sûr du bundle) et confronter à la capture.
Supprimer `login.json` du scratchpad une fois terminé.

## 8. Nettoyer les données de test

Tout ce qui a été créé pour vérifier (plein, dépense, véhicule, commande
d'abonnement, notification) se supprime **par l'API de l'application** avec le
même compte de test (endpoints `DELETE` correspondants), puis se confirme par
un `GET`. Tenir la liste des ids créés au fur et à mesure. Jamais de `DELETE`
SQL (ce serait une écriture en prod : protocole `/prod-db`, accord explicite
requis). Ne jamais toucher aux lignes réelles du client.

## 9. Récap pour le client

En français, sans jargon technique, sans IP ni identifiant :

```
Point : <reformulation du constat client>
Constat : <ce que montrait l'écran, valeur exacte>
Cause : <une phrase compréhensible>
Correctif : <ce qui change, pour qui>
Déployé : <DZ / TN, date> — <tag d'image si TN>
Pour vérifier : <écran, étapes, valeur attendue>
Reste ouvert : <questions au client, points hors périmètre>
```

Ajouter en fin de réponse, pour Slim : commit (hash + titre), fichiers,
migration jouée (oui/non), commande de rollback.

## Ne jamais

- Conclure sans la lentille C (valeurs réelles en base) : ne jamais inventer un chiffre.
- Builder ou tester depuis `C:/Users/Mega-PC/Desktop/GISV2` en travaillant dans un worktree.
- `git add -A` / `git add .` (bin/obj suivis) ; `git push --force`.
- Committer sans build + tests verts (hors les 2 échecs Suppliers connus).
- Utiliser un compte client réel, ni écrire un identifiant de prod dans un fichier.
- Exécuter une écriture SQL en prod (correction de donnée, nettoyage) : proposer via `/prod-db`.
- Copier `environment.ts` vers un serveur ; coder en dur un réglage par serveur.
- Déployer sur TN sans demande explicite ; déployer un pod API avant sa migration SQL.
- Laisser des données de test dans la base du client.
- Annoncer « corrigé » au client sans vérification live sur le serveur déployé.
- Envoyer une commande STOP à un boîtier GPS (hors sujet GPA, mais règle absolue).
