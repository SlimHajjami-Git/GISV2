#!/usr/bin/env node
'use strict';
/*
 * guard-bash.js — garde-fou PreToolUse (Claude Code) pour l'outil Bash/PowerShell.
 *
 * Lit sur stdin le JSON envoyé par Claude Code :
 *   {"tool_name":"Bash","tool_input":{"command":"..."}}
 * et répond par son code de sortie :
 *   0 = laisser passer            2 = BLOQUER (le texte écrit sur stderr est montré à Claude)
 *
 * Règles (voir README.md dans ce dossier) :
 *   a) écriture destructive SQL via psql (DELETE FROM / DROP TABLE|SCHEMA|DATABASE|COLUMN / TRUNCATE)
 *   b) git stash pop / git stash nu / git stash apply sans SHA (hexa 6-40 avec au moins une lettre) / git stash clear
 *   c) dotnet ef migrations add / dotnet ef database update (aussi « dotnet-ef » et options intercalées)
 *   d) git push forcé (--force, -f, +refspec)
 *   e) git pull / git reset --hard / git checkout avec pathspec « . » ou « ./ » (n'importe où dans les
 *      arguments) dans une commande ssh vers belive-tn ou icosnet-dz
 *
 * Robustesse : stdin vide, JSON invalide, exception quelconque → exit 0 (ne jamais tout bloquer).
 * Aucune dépendance, Node >= 14, portable Windows/Linux.
 *
 * Auto-test :  node .claude/hooks/guard-bash.js --test
 */

const fs = require('fs');
const path = require('path');

const UNLOCK_FILE = path.join(__dirname, '.unlock-destructive-sql');
const UNLOCK_WINDOW_MS = 15 * 60 * 1000;

// --------------------------------------------------------------------------
// Messages (français, sans secret, sans IP)
// --------------------------------------------------------------------------

const MSG = {
  sql: (what) => [
    'GARDE-FOU guard-bash : écriture DESTRUCTIVE SQL bloquée (' + what + ' dans une commande psql).',
    '',
    'Règle absolue du projet : on ne supprime JAMAIS de données d\'une base (TN, DZ, locale) de sa',
    'propre initiative — même du junk prouvé inutile. Un « continue » ou un « ok » générique n\'est',
    'PAS une autorisation. UPDATE / INSERT / ALTER / SELECT ne sont pas bloqués.',
    '',
    'Marche à suivre :',
    '  1. PROPOSER l\'opération à Slim avec les chiffres (SELECT count(*), lignes, montants, sociétés',
    '     concernées), la méthode et le plan de restauration. Ne rien exécuter.',
    '  2. S\'il autorise EXPLICITEMENT cette suppression précise : sauvegarder d\'abord les lignes',
    '     (row_to_json → fichier dans le scratchpad), puis DELETE dans une transaction avec garde-fous',
    '     complets (id + société + libellé + montant) et un bloc DO $$ ... RAISE EXCEPTION si ROW_COUNT',
    '     diffère de l\'attendu, puis vérifier l\'état après.',
    '  3. Pour exécuter le script : soit Slim le lance lui-même depuis son terminal, soit il crée',
    '     LUI-MÊME (jamais Claude) le fichier de déverrouillage .claude/hooks/.unlock-destructive-sql',
    '     — le garde-fou SQL se lève 15 minutes après la création/modification de ce fichier.',
  ].join('\n'),

  stash: (what) => [
    'GARDE-FOU guard-bash : « git stash ' + what + ' » bloqué.',
    '',
    'La pile de stash est PARTAGÉE entre tous les worktrees du dépôt : stash@{0} peut appartenir à',
    'une autre session, et « pop » applique puis EFFACE une entrée qui n\'est peut-être pas la vôtre.',
    '',
    'À faire à la place :',
    '  git stash push -u -m "<tag explicite, ex. wt-trusting-hawking-fix-x>"',
    '  git stash list                       # retrouver son entrée par le tag',
    '  git rev-parse \'stash@{n}\'            # obtenir son SHA',
    '  git stash apply <sha>                # appliquer par SHA, jamais par index',
    '« git stash clear » est aussi bloqué (efface la pile pour tout le monde).',
  ].join('\n'),

  ef: (what) => [
    'GARDE-FOU guard-bash : « ' + what + ' » bloqué.',
    '',
    'Les migrations EF sont INUTILISABLES : le snapshot (GisDbContextModelSnapshot.cs) est en dérive',
    'massive par rapport à la prod — une migration générée a voulu DROP des tables de production.',
    '',
    'Le schéma évolue UNIQUEMENT via migrations/0NN_slug.sql (idempotent : IF NOT EXISTS, en-tête de',
    'commentaires expliquant le pourquoi), appliquée à la main :',
    '  kubectl exec -i postgres-0 -n gisv2 -- psql -U postgres -d gis_v2 < migrations/0NN_slug.sql',
    'sur DZ puis TN, AVANT de déployer le nouveau pod API (sinon 42703 column does not exist → login',
    'cassé pour tout le monde). Côté code : entité + configuration EF + DbSet dans GisDbContext,',
    'IGisDbContext ET TestGisDbContext.',
  ].join('\n'),

  push: (what) => [
    'GARDE-FOU guard-bash : git push forcé bloqué (' + what + ').',
    '',
    'master est déployé tel quel sur TN/DZ et le dépôt TN a un historique local DIVERGÉ : un push',
    'forcé réécrit l\'historique partagé et casse la procédure de déploiement (git fetch + checkout',
    'par fichier).',
    '',
    'Pousser normalement : git push origin HEAD:master',
    'Si un push forcé est réellement voulu, c\'est Slim qui le fait lui-même depuis son terminal.',
  ].join('\n'),

  remote: (what, host) => [
    'GARDE-FOU guard-bash : « ' + what + ' » dans une commande ssh vers ' + host + ' bloqué.',
    '',
    '~/GISV2 sur TN est un dépôt git à l\'historique DIVERGÉ (commits locaux « merge master », fichiers',
    'en CRLF, modifs locales : environment.ts, k8s/*.yaml). Un pull / reset --hard / checkout global',
    'écrase du travail local ou casse le dépôt. DZ n\'est pas un dépôt git : on y copie par scp.',
    '',
    'Procédure TN qui marche (par fichier, jamais globale) :',
    '  ssh belive-tn \'cd ~/GISV2 && git fetch origin master\'',
    '  git diff --numstat --ignore-cr-at-eol <commit parent> -- <fichier>   # doit être VIDE, sinon STOP',
    '  git checkout origin/master -- <fichiers précis>',
    'Jamais sur environment.ts ni k8s/*.yaml (modification ciblée par sed).',
  ].join('\n'),
};

// --------------------------------------------------------------------------
// Détecteurs
// --------------------------------------------------------------------------

/** a) psql (direct, kubectl exec, docker exec, ssh…) + verbe destructif dans le texte. */
function detectDestructiveSql(cmd) {
  if (!/\bpsql\b/i.test(cmd)) return null;
  if (/\bDELETE\s+FROM\b/i.test(cmd)) return 'DELETE FROM';
  const drop = /\bDROP\s+(TABLE|SCHEMA|DATABASE|COLUMN)\b/i.exec(cmd);
  if (drop) return 'DROP ' + drop[1].toUpperCase();
  if (/\bTRUNCATE\b/i.test(cmd)) return 'TRUNCATE';
  return null;
}

function sqlUnlocked() {
  try {
    const st = fs.statSync(UNLOCK_FILE);
    return Date.now() - st.mtimeMs < UNLOCK_WINDOW_MS;
  } catch (_) {
    return false;
  }
}

/** b) git stash pop / nu / apply sans SHA / clear — cherché partout (y compris dans une chaîne ssh). */
// SHA accepté : hexa de 6 à 40 caractères contenant AU MOINS une lettre a-f. Une valeur purement
// numérique (ex. 123456) est refusée : git l'interpréterait comme un index dans la pile partagée.
const STASH_SHA_RE = /\b(?=[0-9a-f]*[a-f])[0-9a-f]{6,40}\b/i;

function detectStash(cmd) {
  const re = /\bgit\s+stash\b([^|;&\n]*)/gi;
  let m;
  while ((m = re.exec(cmd)) !== null) {
    const tail = m[1].replace(/["'`)\s]+$/g, '').trim();
    if (tail === '' || tail.startsWith('-')) return 'nu (push implicite)';
    const sub = tail.split(/\s+/)[0].toLowerCase();
    if (sub === 'pop') return 'pop';
    if (sub === 'clear') return 'clear';
    if (sub === 'apply' && !STASH_SHA_RE.test(tail.slice(sub.length))) return 'apply sans SHA';
  }
  return null;
}

/** c) migrations EF — « dotnet ef » ou « dotnet-ef », options intercalées tolérées (--verbose, --project…). */
function detectEf(cmd) {
  const m = /\bdotnet(?:\s+|-)ef\b[^|;&\n]*?\b(database\s+update|migrations\s+add)\b/i.exec(cmd);
  return m ? 'dotnet ef ' + m[1].replace(/\s+/g, ' ').toLowerCase() : null;
}

/** d) git push forcé. */
function detectForcePush(cmd) {
  const re = /\bgit\s+push\b([^|;&\n]*)/gi;
  let m;
  while ((m = re.exec(cmd)) !== null) {
    const args = m[1];
    if (/(^|\s)--force(-with-lease|-if-includes)?\b/.test(args)) return '--force';
    if (/(^|\s)-[a-zA-Z]*f[a-zA-Z]*(?=\s|$)/.test(args)) return '-f';
    if (/(^|\s)\+\S+/.test(args)) return '+refspec';
  }
  return null;
}

/** e) opérations git globales dans une commande ssh vers TN ou DZ. */
function detectRemoteGit(cmd) {
  const m = /\bssh\b[^\n|;&]*?\b(belive-tn|icosnet-dz)\b/i.exec(cmd);
  if (!m) return null;
  const host = m[1].toLowerCase();
  const after = cmd.slice(m.index);
  if (/\bgit\s+pull\b/i.test(after)) return { what: 'git pull', host: host };
  if (/\bgit\s+reset\s+--hard\b/i.test(after)) return { what: 'git reset --hard', host: host };
  // Pathspec « . » ou « ./ » N'IMPORTE OÙ dans les arguments (ex. « git checkout origin/master -- . »),
  // mais pas un fichier précis (« -- services/x.ts », « ./services/x.ts », « .gitignore »).
  if (/\bgit\s+checkout\b[^|;&\n]*?(?:^|\s)(?:--\s+)?\.\/?(?=\s|$|["'])/im.test(after)) {
    return { what: 'git checkout -- .', host: host };
  }
  return null;
}

/**
 * Évalue une commande. Retourne null (laisser passer) ou { rule, message }.
 */
function evaluate(cmd) {
  const sql = detectDestructiveSql(cmd);
  if (sql && !sqlUnlocked()) return { rule: 'a-sql', message: MSG.sql(sql) };

  const stash = detectStash(cmd);
  if (stash) return { rule: 'b-stash', message: MSG.stash(stash) };

  const ef = detectEf(cmd);
  if (ef) return { rule: 'c-ef', message: MSG.ef(ef) };

  const push = detectForcePush(cmd);
  if (push) return { rule: 'd-push', message: MSG.push(push) };

  const remote = detectRemoteGit(cmd);
  if (remote) return { rule: 'e-remote', message: MSG.remote(remote.what, remote.host) };

  return null;
}

// --------------------------------------------------------------------------
// Entrée hook : stdin JSON → code de sortie
// --------------------------------------------------------------------------

const WATCHED_TOOLS = new Set(['Bash', 'PowerShell']);

function decide(raw) {
  let cmd;
  try {
    const data = JSON.parse(raw);
    if (!data || !WATCHED_TOOLS.has(data.tool_name)) return { code: 0 };
    cmd = data.tool_input && data.tool_input.command;
  } catch (_) {
    return { code: 0 };
  }
  if (typeof cmd !== 'string' || cmd.trim() === '') return { code: 0 };
  let verdict = null;
  try {
    verdict = evaluate(cmd);
  } catch (_) {
    return { code: 0 };
  }
  return verdict ? { code: 2, message: verdict.message } : { code: 0 };
}

function runHook() {
  const chunks = [];
  let done = false;
  let timer = null;
  const finish = (raw) => {
    if (done) return;
    done = true;
    if (timer) clearTimeout(timer);
    const r = decide(raw);
    // Forcer la fin du processus même si stdin reste ouvert (pipe tenu par un autre processus) :
    // on laisse stderr se vider (écriture asynchrone sur un pipe Windows), puis on ferme stdin
    // et on sort explicitement avec le bon code — process.exitCode seul ne suffit pas.
    const exit = () => {
      try { process.stdin.destroy(); } catch (_) {}
      process.exit(r.code);
    };
    if (r.message) process.stderr.write(r.message + '\n', exit);
    else exit();
  };
  // Si stdin n'est jamais fermé (pipe tenu ouvert, TTY…), on décide au bout de 5 s sur ce qui a
  // été reçu : rien ou un JSON partiel → laisser passer plutôt que de bloquer Claude.
  timer = setTimeout(() => finish(chunks.join('')), 5000);
  try {
    process.stdin.setEncoding('utf8');
    process.stdin.on('data', (c) => chunks.push(c));
    process.stdin.on('end', () => finish(chunks.join('')));
    process.stdin.on('error', () => finish(''));
  } catch (_) {
    finish('');
  }
}

// --------------------------------------------------------------------------
// Auto-test : node guard-bash.js --test
// --------------------------------------------------------------------------

const TEST_CASES = [
  // [libellé, commande (null = stdin vide), code attendu]
  ['SELECT via kubectl exec psql', 'kubectl exec -n gisv2 postgres-0 -- psql -U postgres -d gis_v2 -c "SELECT count(*) FROM vehicles"', 0],
  ['DELETE FROM via kubectl exec psql', 'kubectl exec -n gisv2 postgres-0 -- psql -U postgres -d gis_v2 -c "DELETE FROM vehicle_costs WHERE id = 42"', 2],
  ['UPDATE via psql', 'psql -h localhost -p 5433 -U postgres -d gis_v2 -c "UPDATE vehicles SET mileage = 1 WHERE id = 1"', 0],
  ['INSERT via docker exec psql', 'docker exec -i gisv2-postgres-1 psql -U postgres -d gis_v2 -c "INSERT INTO fuel_entries(vehicle_id) VALUES (1)"', 0],
  ['ALTER TABLE via psql (migration)', 'psql -U postgres -d gis_v2 -c "ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS foo int"', 0],
  ['ALTER TABLE ... DROP COLUMN via psql', 'psql -U postgres -d gis_v2 -c "ALTER TABLE vehicles DROP COLUMN foo"', 2],
  ['DROP TABLE via docker exec psql', 'docker exec -i gisv2-postgres-1 psql -U postgres -d gis_v2 -c "DROP TABLE IF EXISTS tmp_x"', 2],
  ['TRUNCATE via ssh + kubectl exec psql', 'ssh belive-tn "kubectl exec -n gisv2 postgres-0 -- psql -U postgres -d gis_v2 -c \'TRUNCATE gps_alerts\'"', 2],
  ['DELETE FROM dans un heredoc psql', 'kubectl exec -i -n gisv2 postgres-0 -- psql -U postgres -d gis_v2 <<SQL\nBEGIN;\ndelete from maintenance_logs where id = 7;\nCOMMIT;\nSQL', 2],
  ['grep "DELETE FROM" dans le repo (pas de psql)', 'grep -rn "DELETE FROM" services/src', 0],
  ['SELECT sur colonne deleted_at / date_trunc', 'psql -d gis_v2 -c "SELECT date_trunc(\'day\', deleted_at) FROM x"', 0],
  ['git stash pop', 'git stash pop', 2],
  ['git stash nu', 'git stash', 2],
  ['git stash -u nu', 'git stash -u', 2],
  ['git stash push -u -m x', 'git stash push -u -m x', 0],
  ['git stash list', 'git stash list', 0],
  ['git stash apply sans SHA', 'git stash apply', 2],
  ['git stash apply stash@{0}', 'git stash apply stash@{0}', 2],
  ['git stash apply <sha>', 'git stash apply 3f2a9c1d', 0],
  ['git stash apply abc123 (SHA court avec lettre)', 'git stash apply abc123', 0],
  ['git stash apply 123456 (purement numérique = index)', 'git stash apply 123456', 2],
  ['git stash clear', 'git stash clear', 2],
  ['git stash pop dans ssh', 'ssh belive-tn "cd ~/GISV2 && git stash pop"', 2],
  ['dotnet ef database update', 'cd services/GisAPI && dotnet ef database update', 2],
  ['dotnet ef migrations add', 'dotnet ef migrations add AddFoo --project ../src/GisAPI.Infrastructure', 2],
  ['dotnet-ef database update', 'dotnet-ef database update', 2],
  ['dotnet ef --verbose database update', 'dotnet ef --verbose database update', 2],
  ['dotnet ef migrations list', 'dotnet ef migrations list', 0],
  ['dotnet build', 'dotnet build services/GisAPI/GisAPI.csproj', 0],
  ['git push origin HEAD:master', 'git push origin HEAD:master', 0],
  ['git push --force', 'git push --force origin HEAD:master', 2],
  ['git push -f', 'git push -f origin master', 2],
  ['git push --force-with-lease', 'git push --force-with-lease', 2],
  ['git push +refspec', 'git push origin +HEAD:master', 2],
  ['git push -u origin branch', 'git push -u origin claude/trusting-hawking-293433', 0],
  ['ssh belive-tn git pull', 'ssh belive-tn "cd ~/GISV2 && git pull"', 2],
  ['ssh belive-tn git pull --rebase', 'ssh belive-tn "cd ~/GISV2 && git pull --rebase origin master"', 2],
  ['ssh belive-tn git fetch', 'ssh belive-tn "cd ~/GISV2 && git fetch origin master"', 0],
  ['ssh belive-tn git checkout par fichier', 'ssh belive-tn "cd ~/GISV2 && git checkout origin/master -- services/gis-frontend/src/app/app.routes.ts"', 0],
  ['ssh belive-tn git checkout -- .', 'ssh belive-tn "cd ~/GISV2 && git checkout -- ."', 2],
  ['ssh belive-tn git checkout origin/master -- .', 'ssh belive-tn "cd ~/GISV2 && git checkout origin/master -- ."', 2],
  ['ssh belive-tn git checkout -- ./', 'ssh belive-tn "git checkout -- ./"', 2],
  ['ssh belive-tn git fetch + checkout par fichier', 'ssh belive-tn "cd ~/GISV2 && git fetch origin master && git checkout origin/master -- services/x.ts"', 0],
  ['git checkout origin/master -- . local (hors ssh)', 'git checkout origin/master -- .', 0],
  ['ssh icosnet-dz git reset --hard', 'ssh icosnet-dz "cd ~/x && git reset --hard origin/master"', 2],
  ['ssh belive-tn kubectl get pods', 'ssh belive-tn "kubectl get pods -n gisv2"', 0],
  ['git pull local (hors ssh)', 'git pull origin master', 0],
  ['git reset --hard local (hors ssh)', 'git reset --hard HEAD', 0],
  ['stdin vide', null, 0],
  ['JSON invalide', '__INVALID_JSON__', 0],
  ['outil Read (pas Bash)', '__TOOL_READ__', 0],
];

function runSelfTest() {
  let failed = 0;
  const rows = [];
  for (const [label, cmd, expected] of TEST_CASES) {
    let raw;
    if (cmd === null) raw = '';
    else if (cmd === '__INVALID_JSON__') raw = '{not json';
    else if (cmd === '__TOOL_READ__') raw = JSON.stringify({ tool_name: 'Read', tool_input: { file_path: 'x' } });
    else raw = JSON.stringify({ tool_name: 'Bash', tool_input: { command: cmd } });
    const got = decide(raw).code;
    const ok = got === expected;
    if (!ok) failed++;
    rows.push((ok ? 'OK  ' : 'FAIL') + '  attendu=' + expected + ' obtenu=' + got + '  ' + label);
  }
  process.stdout.write(rows.join('\n') + '\n');
  process.stdout.write((failed === 0 ? 'Tous les cas passent' : failed + ' cas en échec') + ' (' + TEST_CASES.length + ' cas).\n');
  process.exitCode = failed === 0 ? 0 : 1;
}

if (require.main === module) {
  if (process.argv.includes('--test')) runSelfTest();
  else runHook();
}

module.exports = { evaluate, decide };
