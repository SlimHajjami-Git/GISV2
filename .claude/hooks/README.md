# Garde-fous mécaniques (hooks Claude Code)

`guard-bash.js` est branché en `PreToolUse` sur les outils Bash et PowerShell (voir `.claude/settings.json`). Il lit la commande que Claude s'apprête à lancer et la **bloque** (exit 2, message explicatif renvoyé à Claude) dans cinq cas, tirés d'incidents réels du projet :

1. **Écriture destructive SQL via `psql`** (`DELETE FROM`, `DROP TABLE|SCHEMA|DATABASE|COLUMN` — donc aussi `ALTER TABLE … DROP COLUMN` —, `TRUNCATE`, y compris via `kubectl exec` / `docker exec` / `ssh`) — les données sont l'actif du client : on propose avec chiffres, on n'exécute jamais de sa propre initiative. `UPDATE`/`INSERT`/`ALTER … ADD`/`SELECT` passent, de même que `date_trunc`, une colonne `deleted_at` ou un `grep "DELETE FROM"` sans `psql`.
   **Déverrouillage** : le fichier `.claude/hooks/.unlock-destructive-sql` (ignoré par git) est créé **UNIQUEMENT par un humain** — Slim, depuis son propre terminal, jamais par Claude ni par un script lancé par Claude. Le garde-fou SQL se lève pendant **15 minutes** après la création ou la modification de ce fichier, puis se réarme tout seul (aucune suppression du fichier nécessaire).
2. **`git stash pop` / `git stash` nu / `git stash apply` sans SHA / `git stash clear`** — la pile de stash est partagée entre worktrees ; utiliser `git stash push -u -m <tag>` puis `git stash apply <sha>`. Le SHA attendu est une chaîne hexadécimale de **6 à 40 caractères contenant au moins une lettre a-f** (`abc123`, `3f2a9c1d` passent) ; une valeur purement numérique (`123456`) est refusée, car git la lirait comme un index dans la pile, tout comme `stash@{0}`.
3. **`dotnet ef migrations add` / `dotnet ef database update`** — aussi sous la forme `dotnet-ef …` ou avec des options intercalées (`dotnet ef --verbose database update`) — snapshot EF en dérive massive (une migration générée voulait DROP des tables de prod) ; le schéma évolue uniquement via `migrations/0NN_slug.sql`. `dotnet ef migrations list` passe.
4. **`git push --force` / `-f` / `+refspec`** — master est déployé tel quel et le dépôt TN a un historique divergé.
5. **`git pull` / `git reset --hard` / `git checkout` avec un pathspec `.` ou `./` (n'importe où dans les arguments : `git checkout -- .`, `git checkout origin/master -- .`, `git checkout -- ./`) dans un `ssh belive-tn` ou `ssh icosnet-dz`** — le dépôt TN a des modifications locales (`environment.ts`, `k8s/*.yaml`) à ne jamais écraser ; procédure = `git fetch` + contrôle par fichier + `git checkout origin/master -- <fichiers précis>` (qui passe). Les mêmes commandes lancées en local, hors `ssh`, passent.

Tout le reste passe (exit 0), et toute erreur interne (stdin vide, JSON invalide, stdin jamais fermé — le hook se termine seul au bout de 5 s) laisse passer plutôt que de tout bloquer. Rejouer la table de cas : `node .claude/hooks/guard-bash.js --test`.

## Faux positifs à connaître (le hook lit le TEXTE de la commande)

Le garde-fou n'analyse pas ce que la commande *fait*, seulement ce qu'elle *contient*. Écrire un **texte** qui mentionne un motif interdit déclenche donc un blocage, même si rien n'est exécuté. Cas rencontrés en vrai, tous les deux légitimes :

- rédiger de la documentation ou une fiche mémoire citant `psql` et `TRUNCATE` dans un `cat > fichier <<'EOF' … EOF` ;
- un **message de commit** décrivant les règles (`git commit -m "… migrations EF …"`).

La parade n'est pas de contourner le garde-fou mais d'éviter de faire transiter ce texte par une ligne de commande : écrire le fichier avec l'outil **Write**, et passer un message de commit long par `git commit -F <fichier>`. C'est le comportement voulu : mieux vaut un faux positif qu'un `DELETE` silencieux en production.

## Commandes auto-autorisées (`permissions.allow` de `.claude/settings.json`)

La liste blanche ne contient que des commandes en lecture seule (`git status|log|diff|show|…`, `ls`, `cat`, `grep`, `rg`, `kubectl get pods|deployments|svc|nodes`, `kubectl describe|logs`, `kubectl rollout status`) plus trois exceptions assumées : **`dotnet build`, `dotnet test` et `npm run build`**. Ces trois-là **écrivent** bien sur le disque (`bin/`, `obj/`, `dist/`), mais uniquement des artefacts de compilation ignorés par git et jamais committés — le gain (compiler et tester sans prompt à chaque itération) l'emporte sur le risque. Le hook `guard-bash.js` s'applique de toute façon à ces commandes comme à toutes les autres.
