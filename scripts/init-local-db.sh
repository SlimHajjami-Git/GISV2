#!/usr/bin/env bash
# scripts/init-local-db.sh — initialise la base LOCALE de développement.
#
# Pourquoi ce script existe : la chaîne de migrations EF Core ne sait PAS créer
# le schéma sur une base vide (vérifié le 03/09/2026 : dotnet run casse à la
# migration 20260707190000_AddAlertPrefsConfigured, qui suppose des colonnes
# ajoutées par migrations/025_*.sql ; et le modèle EF mappe des colonnes que
# seuls les fichiers migrations/*.sql créent). Le schéma de référence est donc
# celui d'un serveur : on le copie (schéma SEUL, aucune donnée, plus la table
# __EFMigrationsHistory pour que l'API ne rejoue pas d'anciennes migrations),
# puis on rejoue les migrations SQL manuelles. Ensuite `dotnet run` trouve un
# schéma complet, n'a rien à migrer et crée les comptes de test (seed).
#
# Pré-requis : docker compose -f docker-compose.dev.yml up -d (conteneur
# gisv2-postgres), et un accès SSH au serveur source (alias ~/.ssh/config).
#
# Usage (Git Bash sous Windows, bash sous Linux/macOS), depuis la racine du dépôt :
#   bash scripts/init-local-db.sh                 # source = DZ (serveur de test), défaut
#   INIT_DB_SSH=belive-tn INIT_DB_KUBECTL=kubectl bash scripts/init-local-db.sh
#   bash scripts/init-local-db.sh --force         # écrase une base locale déjà remplie
#
# Variables :
#   INIT_DB_SSH      alias SSH du serveur source (défaut icosnet-dz)
#   INIT_DB_KUBECTL  commande kubectl côté serveur (défaut "sudo -n kubectl" : DZ a
#                    un sudo sans mot de passe ; sur TN, kubectl marche sans sudo)
#   INIT_DB_CONTAINER conteneur Postgres local (défaut gisv2-postgres)
set -euo pipefail

SSH_HOST="${INIT_DB_SSH:-icosnet-dz}"
KUBECTL="${INIT_DB_KUBECTL:-sudo -n kubectl}"
CONTAINER="${INIT_DB_CONTAINER:-gisv2-postgres}"
FORCE=0
[ "${1:-}" = "--force" ] && FORCE=1

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

psql_local() { docker exec -i "$CONTAINER" psql -U postgres -d gis_v2 "$@"; }

echo "[1/4] Conteneur Postgres local ($CONTAINER)"
if ! docker exec "$CONTAINER" pg_isready -U postgres -d gis_v2 >/dev/null 2>&1; then
  echo "  Postgres local injoignable. Lancez d'abord : docker compose -f docker-compose.dev.yml up -d" >&2
  exit 1
fi
TABLES=$(psql_local -tAc "SELECT count(*) FROM information_schema.tables WHERE table_schema='public'")
if [ "$TABLES" -gt 0 ] && [ "$FORCE" -eq 0 ]; then
  echo "  La base locale contient déjà $TABLES tables. Pour repartir de zéro :" >&2
  echo "    docker compose -f docker-compose.dev.yml down -v && docker compose -f docker-compose.dev.yml up -d" >&2
  echo "  ou relancez avec --force pour charger le schéma par-dessus." >&2
  exit 1
fi

DUMP="${TMPDIR:-/tmp}/gisv2-schema-${SSH_HOST}-$(date +%Y%m%d-%H%M%S).sql"
echo "[2/4] Copie du schéma (sans données) depuis $SSH_HOST → $DUMP"
# Deux pg_dump : le schéma seul, puis les LIGNES de __EFMigrationsHistory (sans elles,
# l'API croirait devoir rejouer toutes les migrations EF et planterait au démarrage).
ssh -o BatchMode=yes -o ConnectTimeout=20 "$SSH_HOST" \
  "$KUBECTL exec -n gisv2 postgres-0 -- pg_dump -U postgres -d gis_v2 --schema-only --no-owner --no-privileges \
   && $KUBECTL exec -n gisv2 postgres-0 -- pg_dump -U postgres -d gis_v2 --data-only --no-owner --no-privileges --table='\"__EFMigrationsHistory\"'" \
  > "$DUMP"
if ! grep -q 'CREATE TABLE public."__EFMigrationsHistory"' "$DUMP"; then
  echo "  Le dump ne contient pas __EFMigrationsHistory : copie incomplète, on s'arrête." >&2
  exit 1
fi
echo "  $(grep -c '^CREATE TABLE' "$DUMP") tables, $(wc -c < "$DUMP") octets"

echo "[3/4] Chargement du schéma dans la base locale"
# Les schémas tiger/tiger_data/topology existent déjà dans l'image postgis :
# ces trois "already exists" sont attendus et sans conséquence.
psql_local -q < "$DUMP" 2>&1 | grep 'ERROR' | grep -v -E 'schema "(tiger|tiger_data|topology)" already exists' || true

echo "[4/4] Migrations SQL manuelles (migrations/*.sql, idempotentes)"
for f in migrations/*.sql; do
  printf '  %s\n' "$f"
  psql_local -q -f - < "$f" 2>&1 | grep 'ERROR' | sed 's/^/    /' || true
done
echo "  (un « already exists » signifie que le serveur source l'avait déjà : sans gravité)"

echo
echo "Base locale prête : $(psql_local -tAc "SELECT count(*) FROM information_schema.tables WHERE table_schema='public'") tables."
echo "Étape suivante : cd services/GisAPI && dotnet run   (crée les comptes de test au premier démarrage)"
