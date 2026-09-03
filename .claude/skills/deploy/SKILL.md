---
name: deploy
description: Déployer le frontend Angular et/ou l'API .NET sur TN (prod, alias SSH belive-tn) ou DZ (test, alias SSH icosnet-dz) avec la procédure exacte — contrôle de sécurité par fichier sur TN, migration SQL avant le pod API, image à tag horodaté, rollback. Utiliser dès qu'un commit poussé sur origin/master doit être mis en ligne.
---

# /deploy tn|dz <frontend|api|both>

Arguments reçus : `$ARGUMENTS`. Le premier mot est la cible (`tn` = production,
`dz` = serveur de test), le second le service (`frontend`, `api` ou `both`).
Si un argument manque, demande-le. Si la cible est ambiguë, **DZ par défaut** :
TN ne se déploie que sur demande explicite de Slim.

Toute commande serveur passe par les alias SSH `belive-tn` (TN) et
`icosnet-dz` (DZ). Ne jamais écrire d'adresse IP, de mot de passe ni de clé
dans le dépôt ni dans le transcript.

## 0. Checklist avant de toucher un serveur

Toutes les cases doivent être cochées, sinon STOP.

1. **Le livrable est sur origin/master.** Depuis le worktree courant :

   ```bash
   git status --short            # rien d'oublié
   git fetch origin
   git merge-base --is-ancestor HEAD origin/master && echo "OK: HEAD est dans origin/master"
   ```

   Si la commande échoue, le commit n'est pas poussé → `git push origin HEAD:master` d'abord.

2. **Build + tests passés DEPUIS LE WORKTREE** (pas depuis le checkout
   principal `C:/Users/Mega-PC/Desktop/GISV2`, qui contient du vieux code) :

   ```bash
   cd <worktree>/services/GisAPI && dotnet build
   cd <worktree>/services/GisAPI && dotnet test ../tests/GisAPI.Tests/
   cd <worktree>/services/gis-frontend && npx ng build --configuration=production
   ```

   Si `services/gis-frontend/node_modules` n'existe pas dans le worktree, créer une
   jonction vers ceux du checkout principal (PowerShell) :
   `New-Item -ItemType Junction -Path <worktree>\services\gis-frontend\node_modules -Target C:\Users\Mega-PC\Desktop\GISV2\services\gis-frontend\node_modules`

3. **Liste précise des fichiers du livrable** :

   ```bash
   git diff --name-only <commit-deja-deploye>..HEAD
   ```

   `<commit-deja-deploye>` = le dernier commit déjà en ligne sur la cible
   (sur TN : `ssh belive-tn 'cd ~/GISV2 && git log --oneline -5'`, en gardant
   à l'esprit que TN a des commits locaux « merge master » ; sur DZ : demander
   à Slim ou comparer les fichiers). En cas de doute, demander.

4. **Migration SQL ?** Si le livrable contient un fichier `migrations/0NN_*.sql`
   (nouvelle colonne/table mappée par EF), il doit être joué **AVANT** le
   nouveau pod API. Sinon : `42703 column ... does not exist` → **le login
   casse pour tout le monde**. Voir l'étape « migration » plus bas.

5. **Fichiers à exclure de tout checkout/scp** (config PAR SERVEUR, jamais
   écrasée) :
   - `services/gis-frontend/src/environments/environment.ts` (marque, devise,
     centre carte, `aiAssistantLanding`, `actualTrialDays`, `simOperators`…)
   - `k8s/*.yaml` (modifs locales : env, imagePullPolicy, secrets patchés)
   - `services/gis-frontend/nginx*.conf` n'a AUCUN effet en cluster : la conf
     nginx vient du ConfigMap `frontend-nginx-config`.

## 1. Déploiement TN (production, alias `belive-tn`)

### Ce qu'il faut savoir sur TN

- `~/GISV2` est un dépôt git sur `master` à l'**historique divergé** : commits
  locaux « merge master », fichiers en CRLF, modifications locales. **Jamais**
  `git pull`, `git reset`, `git checkout -- .`, `git stash` dessus.
- L'utilisateur SSH n'a pas de sudo non interactif → `k8s/update.sh` est
  **inutilisable**. `docker` et `kubectl` fonctionnent sans sudo.
- Le registre d'images écoute sur `localhost:5000` ; les pods tirent via le
  miroir `registry.local:5000`. **Pousser sur `localhost:5000`** (l'autre nom
  résout en IPv6 → `connection refused`).
- Le déploiement a `imagePullPolicy: IfNotPresent` (modif locale ; le yaml du
  dépôt dit `Always`, ne pas s'y fier) : **un tag déjà présent n'est jamais
  re-tiré**. D'où le tag horodaté obligatoire — qui offre aussi un rollback.
- Namespace `gisv2`. Deployments : `frontend` (container `frontend`) et
  `gis-api` (container `gis-api`). Postgres : pod `postgres-0`.

### Étapes

1. **État des lieux** (lecture seule) :

   ```bash
   ssh belive-tn 'cd ~/GISV2 && git status --short | head -20 && git log --oneline -5'
   ```

2. **Rapatrier master sans toucher l'arbre de travail** :

   ```bash
   ssh belive-tn 'cd ~/GISV2 && git fetch origin master'
   ```

3. **Contrôle de sécurité par fichier** — pour CHAQUE fichier `F` du livrable,
   avec `P` = commit parent du livrable (= la version censée être déjà sur TN) :

   ```bash
   ssh belive-tn 'cd ~/GISV2 && git diff --numstat --ignore-cr-at-eol <P> -- <F>'
   ```

   La sortie doit être **VIDE** (TN identique au parent, aucune modification
   locale à écraser ; `--ignore-cr-at-eol` neutralise le CRLF de TN). Si elle
   ne l'est pas : **STOP**, afficher `git diff --ignore-cr-at-eol <P> -- <F>`,
   expliquer à Slim ce que TN a de différent et décider ensemble (report de la
   modif locale par `sed`, abandon du fichier…). Ne jamais passer outre.

4. **Checkout des fichiers précis** (jamais `.`, jamais `environment.ts`, jamais `k8s/`) :

   ```bash
   ssh belive-tn 'cd ~/GISV2 && git checkout origin/master -- <F1> <F2> <migrations/0NN_x.sql>'
   ```

5. **Migration SQL d'abord** (seulement si le livrable en contient une) :

   ```bash
   ssh belive-tn 'cd ~/GISV2 && kubectl exec -i postgres-0 -n gisv2 -- psql -U postgres -d gis_v2 -v ON_ERROR_STOP=1 < migrations/0NN_x.sql'
   ```

   Puis vérifier que la colonne/table est là :

   ```bash
   ssh belive-tn "kubectl exec -i postgres-0 -n gisv2 -- psql -U postgres -d gis_v2 -c \"SELECT column_name FROM information_schema.columns WHERE table_name='<table>' AND column_name='<colonne>'\""
   ```

   Les migrations sont idempotentes (`IF NOT EXISTS`) : rejouer est sans danger.
   Vérifier le NB devise en tête du fichier (TN facture en TND sauf les comptes GPA en EUR).

6. **Build + push + bascule avec un tag NEUF** :

   Frontend :

   ```bash
   ssh belive-tn 'TAG=$(date +%Y%m%d-%H%M%S) && echo "TAG=$TAG" \
     && cd ~/GISV2/services/gis-frontend \
     && docker build -q -t localhost:5000/gisv2/frontend:$TAG -f Dockerfile.prod . \
     && docker push -q localhost:5000/gisv2/frontend:$TAG \
     && kubectl set image deployment/frontend frontend=registry.local:5000/gisv2/frontend:$TAG -n gisv2 \
     && kubectl rollout status deployment/frontend -n gisv2 --timeout=300s'
   ```

   API (contexte de build = `services/`, car le Dockerfile référence `src/`) :

   ```bash
   ssh belive-tn 'TAG=$(date +%Y%m%d-%H%M%S) && echo "TAG=$TAG" \
     && cd ~/GISV2/services \
     && docker build -q -t localhost:5000/gisv2/gis-api:$TAG -f GisAPI/Dockerfile . \
     && docker push -q localhost:5000/gisv2/gis-api:$TAG \
     && kubectl set image deployment/gis-api gis-api=registry.local:5000/gisv2/gis-api:$TAG -n gisv2 \
     && kubectl rollout status deployment/gis-api -n gisv2 --timeout=300s'
   ```

   Pour `both` : migration SQL → API → frontend. Si la session SSH risque de
   tomber (build long), lancer en détaché et surveiller :

   ```bash
   ssh belive-tn 'nohup bash -c "<la commande ci-dessus>" > ~/deploy-$(date +%Y%m%d-%H%M%S).log 2>&1 &'
   ssh belive-tn 'tail -20 ~/deploy-*.log; pgrep -af "docker build"'
   ```

   Noter le TAG affiché : c'est la clé du rollback.

7. **Vérification live** :

   ```bash
   ssh belive-tn 'kubectl get pods -n gisv2 -o wide | grep -E "frontend|gis-api"'
   ssh belive-tn 'kubectl logs deployment/gis-api -n gisv2 --tail=80 | grep -iE "42703|exception|error" || echo "aucune erreur"'
   ssh belive-tn 'curl -s http://127.0.0.1/health'
   ```

   Puis un vrai login via l'API avec un **compte de test** de la société
   concernée (identifiants fournis par Slim dans la session, jamais un compte
   client réel, jamais écrits dans un fichier) :

   ```bash
   ssh belive-tn "curl -s -X POST http://127.0.0.1/api/auth/login -H 'Content-Type: application/json' -d '{\"email\":\"<compte-test>\",\"password\":\"<mdp>\"}'"
   ```

   Utiliser le `token` renvoyé pour appeler le ou les endpoints du livrable
   (`-H "Authorization: Bearer <token>"`) et comparer avec l'attendu de la recette.
   Côté navigateur, `index.html` est en `no-cache` depuis le 21/07 : un
   Ctrl+Shift+R suffit si l'écran semble ancien.

8. **Rollback** (gratuit, immédiat) :

   ```bash
   ssh belive-tn 'kubectl rollout undo deployment/gis-api -n gisv2 && kubectl rollout status deployment/gis-api -n gisv2'
   ssh belive-tn 'kubectl rollout history deployment/frontend -n gisv2'
   ```

   Une migration SQL additive (colonne avec DEFAULT) est compatible avec
   l'ancien pod : le rollback du pod seul suffit. Ne jamais « annuler » une
   migration par un DROP sans passer par `/prod-db` et l'accord de Slim.

### Fichiers TN-locaux : modification ciblée par `sed`, jamais par checkout

Exemple pour `environment.ts` (vérifier avant, appliquer, vérifier après) :

```bash
ssh belive-tn 'cd ~/GISV2/services/gis-frontend/src/environments && grep -n "actualTrialDays" environment.ts'
ssh belive-tn 'cd ~/GISV2/services/gis-frontend/src/environments && sed -i "s/actualTrialDays: 14/actualTrialDays: 7/" environment.ts && grep -n "actualTrialDays" environment.ts'
```

Variables d'environnement de l'API : poser en live ET refléter dans le yaml local
(sinon la valeur se perd au prochain `kubectl apply`) :

```bash
ssh belive-tn 'kubectl set env deployment/gis-api Registration__TrialDays=7 -n gisv2'
ssh belive-tn 'cd ~/GISV2 && grep -n "Registration__TrialDays" -A1 k8s/02-applications.yaml'
```

Conf nginx du frontend : elle vient du ConfigMap `frontend-nginx-config`
(clé `default.conf`, défini dans `k8s/02-applications.yaml`). Ne jamais
`kubectl apply -f k8s/02-applications.yaml` en entier sur TN (cela remettrait
les images sur `:latest` / `Always` et effacerait les env posées en live) :
modifier le bloc dans le yaml local par sed, puis appliquer le ConfigMap seul
(`kubectl edit configmap frontend-nginx-config -n gisv2` ou extraction du
bloc) et `kubectl rollout restart deployment/frontend -n gisv2`.

### Créer l'alias SSH `belive-tn` (poste neuf)

Dans `~/.ssh/config` (fichier LOCAL, jamais versionné) :

```
Host belive-tn
    HostName <adresse du serveur TN — à demander à Slim, ne jamais l'écrire dans le dépôt>
    User <utilisateur fourni par Slim>
    IdentityFile ~/.ssh/<clé privée fournie par Slim>

Host icosnet-dz
    HostName <adresse du serveur DZ — à demander à Slim>
    User <utilisateur fourni par Slim>
    IdentityFile ~/.ssh/<clé privée fournie par Slim>
```

Tester : `ssh belive-tn 'hostname && kubectl get pods -n gisv2 | head -5'`.

## 2. Déploiement DZ (serveur de test, alias `icosnet-dz`)

DZ est la cible **par défaut** du travail en cours. `~/GISV2`
n'est **pas un dépôt git** : on y copie les fichiers modifiés par `scp`, puis
`update.sh <service>` (sudo sans mot de passe sur DZ).

1. **Copier les fichiers du livrable** en conservant l'arborescence (créer les
   dossiers manquants d'abord) :

   ```bash
   ssh icosnet-dz 'mkdir -p ~/GISV2/<dossier-du-fichier>'
   scp <worktree>/<chemin/du/fichier> icosnet-dz:GISV2/<chemin/du/fichier>
   ```

   Piège de dérive : DZ ne reçoit que les fichiers copiés, son arbre est en
   retard sur master. Si un `.cs` copié utilise un package nouveau, copier
   aussi le `.csproj` correspondant (`services/GisAPI/GisAPI.csproj`…), sinon
   le build casse.

2. **Migration SQL d'abord** (si le livrable en contient une) :

   ```bash
   ssh icosnet-dz 'sudo kubectl exec -i postgres-0 -n gisv2 -- psql -U postgres -d gis_v2 -v ON_ERROR_STOP=1' < <worktree>/migrations/0NN_x.sql
   ```

   Relire le NB devise du fichier : DZ facture en DZD, une migration calibrée
   EUR/TND doit être adaptée AVANT d'être jouée.

3. **Build + rollout, en détaché** (la session SSH DZ tombe sur les longs builds) :

   ```bash
   ssh icosnet-dz 'cd ~/GISV2 && nohup sudo bash k8s/update.sh api > /tmp/update-api.log 2>&1 &'
   ssh icosnet-dz 'cd ~/GISV2 && nohup sudo bash k8s/update.sh frontend > /tmp/update-frontend.log 2>&1 &'
   ```

   Surveiller jusqu'à la fin :

   ```bash
   ssh icosnet-dz 'pgrep -af update.sh || echo "termine"; tail -5 /tmp/update-api.log'
   ```

   `update.sh` construit l'image, la pousse sur le registre local et fait un
   `rollout restart` ; il ne touche ni aux secrets ni aux env posées en live
   (`App__DefaultCurrency=DZD` survit).

4. **Vérification live** : mêmes commandes qu'en TN, préfixées de `sudo` pour
   `kubectl` (`ssh icosnet-dz 'sudo kubectl get pods -n gisv2'`), login avec un
   compte de test DZ fourni par Slim.

5. **Rollback** : `ssh icosnet-dz 'sudo kubectl rollout undo deployment/<svc> -n gisv2'`.

## 3. Compte rendu

Terminer par : cible, service(s), TAG d'image (TN), migration jouée (oui/non
+ numéro), résultat de la vérification live, commande de rollback.

## Ne jamais

- `git pull`, `git reset`, `git checkout -- .`, `git stash` dans `~/GISV2` sur TN.
- Passer outre un contrôle `--numstat` non vide : c'est une modif locale de prod.
- Checkout ou scp de `environment.ts` ni de `k8s/*.yaml` vers un serveur.
- Déployer le pod API avant la migration SQL qu'il attend (42703 = login cassé).
- Réutiliser un tag d'image (`latest` ou ancien) : rien ne se déploie.
- Pousser sur `registry.local:5000` (IPv6 → refus) : toujours `localhost:5000`.
- `update.sh config` ou `kubectl apply -f k8s/00-namespace-secrets.yaml` : efface
  les secrets patchés en live (clé Groq…). `update.sh all` fait un `git pull`.
- `kubectl apply -f k8s/02-applications.yaml` en entier sur TN.
- `dotnet ef migrations add` / `dotnet ef database update` contre une base réelle.
- Déployer sur TN sans demande explicite de Slim ; DZ est la cible par défaut.
- Utiliser un compte client réel pour la vérification, ni écrire un identifiant
  de prod dans un fichier.
- Toute commande destructive en base pendant un déploiement (voir `/prod-db`).
