#!/usr/bin/env bash
# =============================================================================
# calypso-pre-backup.sh — commande PRÉ-SAUVEGARDE Acronis du serveur TN (prod)
# =============================================================================
#
# Constat : la sauvegarde hors site (Acronis Cyber Protect Cloud) copie des
# fichiers. Trois choses ne sont pas des fichiers « prêts à copier » :
#   1. les secrets et objets Kubernetes VIVANTS (patchés à chaud : ceux du dépôt
#      sont faux, et `update.sh config` les écraserait) ;
#   2. la configuration propre au serveur (k8s/ modifié localement,
#      environment.ts de TN) ;
#   3. la garantie que le dump PostgreSQL de la nuit existe, est complet et
#      récent — sans ce contrôle, Acronis sauvegarderait sans broncher un
#      répertoire de dumps périmés ou un dump en cours d'écriture.
#
# Ce script, lancé par Acronis AVANT chaque sauvegarde (option « Commande
# pré-sauvegarde » + « Faire échouer la sauvegarde si la commande échoue ») :
#   - contrôle la fraîcheur du dernier auto_*.dump (< 26 h, >= 500 Mo, ET
#     postérieur au dernier passage du CronJob db-backup, qui ne doit pas être
#     suspendu) et attend (20 min max) la fin d'un dump en cours (.auto_*.part
#     récent ou Job du CronJob encore actif) ;
#   - vérifie que les chemins des volumes n'ont pas changé (PVC recréé =
#     chemins du plan Acronis à mettre à jour) ;
#   - exporte en YAML les objets du namespace gisv2 (secrets compris) + les PV
#     dans EXPORT_DIR, fichiers 0600 root, répertoire 0700 root, remplacé de
#     façon quasi atomique (répertoire temporaire puis mv) ;
#   - copie k8s/ et environment.ts ;
#   - écrit MANIFEST.txt (dump retenu, sha256, objets et fichiers exportés) ;
#   - avec --link-latest-dump : publie dans LATEST_DUMP_DIR un LIEN DUR vers le
#     seul dump retenu (+ SHA256SUMS). Aucun octet copié. Le plan Acronis vise
#     ce répertoire et JAMAIS tout db-backups : / est en XFS, où les filtres
#     d'exclusion Acronis ne fonctionnent pas (un .part abandonné et les
#     weekly_* partiraient). Option OBLIGATOIRE pour le plan de TN.
#
# Durée bornée à 40 min (garde-fou `timeout`) : la doc Acronis ne documente
# aucun délai maximum pour une commande pré-sauvegarde.
#
# Le contenu des secrets n'est JAMAIS affiché : seuls des noms de fichiers et
# des comptages sortent sur la console et dans le journal.
#
# Installation (root, voir scripts/acronis/RUNBOOK-TN.md) :
#   sudo install -m 0700 -o root -g root pre-backup-tn.sh /usr/local/sbin/calypso-pre-backup.sh
#
# Usage :
#   calypso-pre-backup.sh [--link-latest-dump]   exécution réelle (root obligatoire)
#   calypso-pre-backup.sh --dry-run              contrôles seulement : n'écrit
#                                                rien, n'exporte rien,
#                                                utilisable sans root
#
# Codes de sortie : 0 = OK ; 1 = contrôle en échec ou erreur ; 2 = usage.
# =============================================================================
set -euo pipefail

readonly SCRIPT_VERSION="1.1 (2026-09-15)"

export PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
export LC_ALL=C
# Doc Acronis (commandes pré-sauvegarde Linux) : l'agent peut transmettre ses
# propres bibliothèques ; sha256sum, stat, find, flock… sont liés dynamiquement.
unset LD_LIBRARY_PATH LD_PRELOAD

# Garde-fou de durée : le script se relance une fois sous `timeout`. À
# l'expiration il reçoit TERM (trap -> code 143, cleanup exécuté), puis KILL
# 60 s plus tard ; `timeout` sort en 124 et la sauvegarde échoue.
readonly MAX_RUNTIME_S=2400
if [[ -z ${CALYPSO_PRE_BACKUP_WATCHDOG:-} ]] && command -v timeout >/dev/null 2>&1; then
  export CALYPSO_PRE_BACKUP_WATCHDOG=1
  exec timeout -k 60 "$MAX_RUNTIME_S" "$BASH" "$0" "$@"
fi

# --- Paramètres ---------------------------------------------------------------
readonly NAMESPACE="gisv2"
readonly EXPORT_DIR="/var/backups/calypso-config"
readonly LATEST_DUMP_DIR="/var/backups/calypso-db-latest"
readonly LOG_FILE="/var/log/calypso-pre-backup.log"
readonly LOG_MAX_BYTES=$((1024 * 1024))
readonly LOCK_FILE="/run/calypso-pre-backup.lock"
readonly K3S_KUBECONFIG="/etc/rancher/k3s/k3s.yaml"

readonly STORAGE_ROOT="/var/lib/rancher/k3s/storage"
readonly DB_BACKUP_PVC="db-backups"
readonly DB_BACKUP_CRONJOB="db-backup"
readonly DB_BACKUP_DIR="${STORAGE_ROOT}/pvc-82f90b84-4fc8-4c09-8736-13c42ed430e2_gisv2_db-backups"
readonly UPLOADS_PVC="gis-uploads"
readonly UPLOADS_DIR="${STORAGE_ROOT}/pvc-6c4e0b39-e1d0-48af-86e7-3e3c45688b50_gisv2_gis-uploads"

readonly REPO_K8S_DIR="/home/master/GISV2/k8s"
readonly ENVIRONMENT_TS="/home/master/GISV2/services/gis-frontend/src/environments/environment.ts"
# Fichiers de configuration K3s copiés s'ils existent (ne font pas échouer).
readonly OPTIONAL_HOST_FILES=(/etc/rancher/k3s/registries.yaml /etc/rancher/k3s/config.yaml)

readonly MAX_DUMP_AGE_S=$((26 * 3600))
readonly MIN_DUMP_BYTES=$((500 * 1024 * 1024))
readonly PART_ACTIVE_S=$((30 * 60))     # .part modifié il y a moins de 30 min = dump en cours
readonly PART_WAIT_MAX_S=$((20 * 60))   # attente maximale de la fin du dump
readonly PART_POLL_S=30
readonly DISK_WARN_PCT=80
readonly EXPORT_MIN_FREE_KB=$((100 * 1024))

# Objets exportés : "namespace|type|obligatoire". Namespace vide = objet de cluster.
# Les types facultatifs ne sont exportés que s'ils existent sur le cluster.
readonly EXPORTS=(
  "${NAMESPACE}|secrets|oui"
  "${NAMESPACE}|configmaps|oui"
  "${NAMESPACE}|deployments|oui"
  "${NAMESPACE}|statefulsets|oui"
  "${NAMESPACE}|cronjobs|oui"
  "${NAMESPACE}|services|oui"
  "${NAMESPACE}|ingresses|oui"
  "${NAMESPACE}|persistentvolumeclaims|oui"
  "|persistentvolumes|oui"
  "${NAMESPACE}|ingressroutes.traefik.io|non"
  "${NAMESPACE}|middlewares.traefik.io|non"
  "${NAMESPACE}|certificates.cert-manager.io|non"
  "kube-system|helmchartconfigs.helm.cattle.io|non"
  "|clusterissuers.cert-manager.io|non"
)

# --- État ---------------------------------------------------------------------
DRY_RUN=0
LINK_LATEST=0
TMP_LATEST=""
LOG_READY=0
FAILURES=0
WARNINGS=0
SKIPPED=0
KUBECTL=()
KUBECTL_OK=0
KUBECONFIG_DESC=""
API_RESOURCES=""
TMP_EXPORT=""
DUMP_DIR_READABLE=0
NEWEST_NAME=""
NEWEST_SIZE=0
NEWEST_MTIME=0
ACTIVE_PARTS=""
STALE_PARTS=""
DUMP_OK=0
CRON_READ=0
CRON_SUSPEND=""
CRON_SCHED_S=0
CRON_ACTIVE=""
declare -A EXPORT_COUNTS=()

# --- Journal ------------------------------------------------------------------
ts() { date -u +%Y-%m-%dT%H:%M:%SZ; }

log() {
  local level=$1 line
  shift
  line="$(ts) [${level}] $*"
  # Exécution réelle : erreurs sur stderr (visibles dans le journal d'activité
  # Acronis). --dry-run : tout sur stdout.
  if [[ $level == "ERREUR" ]] && ((!DRY_RUN)); then
    printf '%s\n' "$line" >&2
  else
    printf '%s\n' "$line"
  fi
  if ((LOG_READY)); then
    printf '%s\n' "$line" >>"$LOG_FILE" 2>/dev/null || true
  fi
}
info() { log "INFO" "$@"; }
warn() { WARNINGS=$((WARNINGS + 1)); log "ATTENTION" "$@"; }
fail() { FAILURES=$((FAILURES + 1)); log "ERREUR" "$@"; }
skip() { SKIPPED=$((SKIPPED + 1)); log "NON-VERIFIE" "$@"; }
die() { log "ERREUR" "$@"; exit 1; }

human_bytes() { numfmt --to=iec --suffix=o "$1" 2>/dev/null || printf '%s o' "$1"; }

human_duration() {
  local s=$1 sign=""
  if ((s < 0)); then sign="-"; s=$((-s)); fi
  printf '%s%d h %02d min' "$sign" $((s / 3600)) $(((s % 3600) / 60))
}

usage() {
  cat <<'EOF'
Usage : calypso-pre-backup.sh [--dry-run] [--link-latest-dump]

  (sans option)       exécution réelle, root obligatoire : contrôles, export de
                      la configuration Kubernetes dans /var/backups/calypso-config,
                      MANIFEST.txt, journal /var/log/calypso-pre-backup.log
  --link-latest-dump  en plus : /var/backups/calypso-db-latest/ ne contient
                      qu'un lien dur vers le dump retenu + SHA256SUMS
                      (OBLIGATOIRE pour le plan Acronis de TN)
  --dry-run, -n       contrôles seulement ; n'écrit rien, n'exporte aucun
                      secret ; utilisable sans root (contrôles partiels)
  --help, -h          cette aide
EOF
}

cleanup() {
  local rc=$?
  if [[ -n $TMP_EXPORT && -d $TMP_EXPORT ]]; then
    rm -rf -- "$TMP_EXPORT"
  fi
  # Ne contient qu'un lien dur : le supprimer ne touche pas au dump d'origine.
  if [[ -n $TMP_LATEST && -d $TMP_LATEST ]]; then
    rm -rf -- "$TMP_LATEST"
  fi
  if ((!DRY_RUN)) && ((LOG_READY)); then
    if ((rc == 0)); then
      log "INFO" "fin : succès (code 0)"
    else
      log "ERREUR" "fin : ÉCHEC (code ${rc}) — la sauvegarde Acronis ne doit pas être lancée"
    fi
  fi
  exit "$rc"
}

# --- Arguments ----------------------------------------------------------------
while (($# > 0)); do
  case $1 in
    --dry-run | -n) DRY_RUN=1 ;;
    --link-latest-dump) LINK_LATEST=1 ;;
    --help | -h) usage; exit 0 ;;
    *) usage >&2; exit 2 ;;
  esac
  shift
done

IS_ROOT=0
if [[ $(id -u) -eq 0 ]]; then IS_ROOT=1; fi

# --- Initialisation -----------------------------------------------------------
umask 077
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

if ((!DRY_RUN)); then
  if ((!IS_ROOT)); then
    die "exécution réelle réservée à root (pour un test sans root : --dry-run)"
  fi
  # Acronis peut lancer la commande sans HOME : kubectl en a besoin pour son cache.
  export HOME="${HOME:-/root}"
  if [[ -f $LOG_FILE ]] && (($(stat -c %s -- "$LOG_FILE") > LOG_MAX_BYTES)); then
    mv -f -- "$LOG_FILE" "${LOG_FILE}.1"
  fi
  touch -- "$LOG_FILE"
  chmod 0600 -- "$LOG_FILE"
  LOG_READY=1
  exec 9>"$LOCK_FILE"
  if ! flock -n 9; then
    die "une autre exécution de calypso-pre-backup est déjà en cours (${LOCK_FILE})"
  fi
fi

if ((DRY_RUN)); then
  info "calypso-pre-backup ${SCRIPT_VERSION} — MODE --dry-run (contrôles seulement, rien n'est écrit)"
else
  info "calypso-pre-backup ${SCRIPT_VERSION} — exécution réelle"
fi
info "hôte $(hostname), utilisateur $(id -un)"
if ((!IS_ROOT)); then
  info "non root : les volumes sous ${STORAGE_ROOT} ne sont pas lisibles — lancer en root pour le contrôle complet"
fi

# --- kubectl ------------------------------------------------------------------
kc() { "${KUBECTL[@]}" --request-timeout=60s "$@"; }

setup_kubectl() {
  if command -v kubectl >/dev/null 2>&1; then
    KUBECTL=(kubectl)
  elif command -v k3s >/dev/null 2>&1; then
    KUBECTL=(k3s kubectl)
  else
    fail "ni kubectl ni k3s trouvés dans le PATH"
    return 0
  fi
  if [[ -r $K3S_KUBECONFIG ]]; then
    export KUBECONFIG=$K3S_KUBECONFIG
    KUBECONFIG_DESC=$K3S_KUBECONFIG
  elif ((DRY_RUN)); then
    KUBECONFIG_DESC="configuration par défaut de $(id -un) (${K3S_KUBECONFIG} illisible)"
  else
    fail "kubeconfig ${K3S_KUBECONFIG} illisible"
    return 0
  fi
  if kc get namespace "$NAMESPACE" -o name >/dev/null 2>&1; then
    KUBECTL_OK=1
    info "kubectl OK (${KUBECTL[*]}, kubeconfig : ${KUBECONFIG_DESC}), namespace ${NAMESPACE} présent"
    API_RESOURCES=$(kc api-resources -o name 2>/dev/null || true)
    if [[ -z $API_RESOURCES ]]; then
      fail "kubectl api-resources vide : types Traefik/cert-manager non exportables"
    fi
  else
    fail "kubectl ne joint pas l'API K3s ou le namespace ${NAMESPACE} est absent (kubeconfig : ${KUBECONFIG_DESC})"
  fi
  return 0
}

resource_exists() { grep -Fxq -- "$1" <<<"$API_RESOURCES"; }

# Le chemin réel du volume doit être celui que le script ET le plan Acronis visent.
check_volume_path() {
  local pvc=$1 expected=$2 vol path
  if ((!KUBECTL_OK)); then
    skip "chemin du volume ${pvc} : kubectl indisponible"
    return 0
  fi
  if ! vol=$(kc get pvc "$pvc" -n "$NAMESPACE" -o jsonpath='{.spec.volumeName}' 2>/dev/null) || [[ -z $vol ]]; then
    fail "PVC ${pvc} introuvable dans ${NAMESPACE}"
    return 0
  fi
  if ! path=$(kc get pv "$vol" -o jsonpath='{.spec.hostPath.path}{.spec.local.path}' 2>/dev/null); then
    fail "PV ${vol} (PVC ${pvc}) illisible"
    return 0
  fi
  if [[ $path != "$expected" ]]; then
    fail "le volume ${pvc} a changé de chemin : ${path} (attendu ${expected}) — mettre à jour CE script ET les chemins du plan Acronis"
  else
    info "volume ${pvc} : chemin conforme (${path})"
  fi
  return 0
}

# --- Contrôles de chemins et de disque ----------------------------------------
check_paths() {
  if [[ -d $REPO_K8S_DIR && -r $REPO_K8S_DIR ]]; then
    info "présent : ${REPO_K8S_DIR} ($(find "$REPO_K8S_DIR" -maxdepth 1 -type f -name '*.yaml' | wc -l) fichiers .yaml)"
  else
    fail "absent ou illisible : ${REPO_K8S_DIR}"
  fi
  if [[ -f $ENVIRONMENT_TS && -r $ENVIRONMENT_TS ]]; then
    info "présent : ${ENVIRONMENT_TS}"
  else
    fail "absent ou illisible : ${ENVIRONMENT_TS}"
  fi

  if [[ -r $STORAGE_ROOT && -x $STORAGE_ROOT ]]; then
    if [[ -d $DB_BACKUP_DIR && -r $DB_BACKUP_DIR && -x $DB_BACKUP_DIR ]]; then
      DUMP_DIR_READABLE=1
      info "présent : ${DB_BACKUP_DIR}"
    else
      fail "absent ou illisible : ${DB_BACKUP_DIR}"
    fi
    if [[ -d $UPLOADS_DIR && -r $UPLOADS_DIR ]]; then
      info "présent : ${UPLOADS_DIR} ($(find "$UPLOADS_DIR" -type f | wc -l) fichiers, $(du -sh -- "$UPLOADS_DIR" | cut -f1))"
    else
      fail "absent ou illisible : ${UPLOADS_DIR}"
    fi
  else
    skip "${DB_BACKUP_DIR} : non lisible par $(id -un) — lancer en root pour le contrôle complet"
    skip "${UPLOADS_DIR} : non lisible par $(id -un) — lancer en root pour le contrôle complet"
  fi

  local pct
  pct=$(df -P / | awk 'NR==2 { gsub("%", "", $5); print $5 }')
  if [[ $pct =~ ^[0-9]+$ ]] && ((pct >= DISK_WARN_PCT)); then
    warn "disque / occupé à ${pct} % (seuil ${DISK_WARN_PCT} %) — risque d'éviction K3s, purger les artefacts Docker"
  else
    info "disque / occupé à ${pct} %"
  fi

  # Le plan TN ne crée pas d'instantané : snapapi n'est pas nécessaire à cette sauvegarde.
  if [[ -d /usr/lib/Acronis ]] && ! grep -q '^snapapi' /proc/modules; then
    info "module noyau snapapi non chargé (noyau $(uname -r)) : sans effet sur ce plan (sauvegarde fichiers sans instantané)"
  fi
  # Lien dur (--link-latest-dump) : volumes et /var/backups sur le même système de fichiers.
  local dev_storage dev_backups
  if dev_storage=$(stat -c %d -- "$STORAGE_ROOT" 2>/dev/null) &&
    dev_backups=$(stat -c %d -- "${LATEST_DUMP_DIR%/*}" 2>/dev/null); then
    if [[ $dev_storage == "$dev_backups" ]]; then
      info "${STORAGE_ROOT} et ${LATEST_DUMP_DIR%/*} sur le même système de fichiers : --link-latest-dump possible"
    elif ((LINK_LATEST)); then
      fail "${STORAGE_ROOT} et ${LATEST_DUMP_DIR%/*} sur des systèmes de fichiers différents : --link-latest-dump impossible"
    else
      info "${STORAGE_ROOT} et ${LATEST_DUMP_DIR%/*} sur des systèmes de fichiers différents : --link-latest-dump serait impossible"
    fi
  elif ((LINK_LATEST)); then
    skip "système de fichiers de ${STORAGE_ROOT} non vérifiable — lancer en root"
  fi

  if [[ -e /var/run/reboot-required ]]; then
    warn "redémarrage en attente (nouveau noyau installé) : à planifier en fenêtre de maintenance, indépendamment d'Acronis"
  fi
  return 0
}

# --- Fraîcheur du dump PostgreSQL ---------------------------------------------
# Lignes « epoch taille chemin » pour auto_*.dump et .auto_*.part.
list_dump_files_local() {
  local f
  shopt -s nullglob
  for f in "$DB_BACKUP_DIR"/auto_*.dump "$DB_BACKUP_DIR"/.auto_*.part; do
    stat -c '%Y %s %n' -- "$f" 2>/dev/null || true
  done
  shopt -u nullglob
}

# Repli du --dry-run sans root : même liste, lue par le pod gis-api (/backups).
list_dump_files_pod() {
  kc exec -n "$NAMESPACE" deploy/gis-api -c gis-api -- sh -c \
    'for f in /backups/auto_*.dump /backups/.auto_*.part; do [ -e "$f" ] && stat -c "%Y %s %n" "$f"; done; true' \
    2>/dev/null
}

# Statut du CronJob : suspendu ? dernier passage planifié ? Job(s) en cours ?
read_cronjob_status() {
  local raw sched
  raw=$(kc get cronjob "$DB_BACKUP_CRONJOB" -n "$NAMESPACE" \
    -o jsonpath='{.spec.suspend}|{.status.lastScheduleTime}|{.status.active[*].name}' 2>/dev/null) || return 1
  IFS='|' read -r CRON_SUSPEND sched CRON_ACTIVE <<<"$raw"
  CRON_SCHED_S=0
  if [[ -n $sched ]]; then
    CRON_SCHED_S=$(date -u -d "$sched" +%s) || return 1
  fi
  CRON_READ=1
  return 0
}

# Ce qui est encore en cours : fichiers .part récents et/ou Job du CronJob.
pending_desc() {
  local d=${ACTIVE_PARTS% }
  if [[ -n $CRON_ACTIVE ]]; then d+="${d:+, }job ${CRON_ACTIVE}"; fi
  printf '%s' "$d"
}

analyse_listing() {
  local listing=$1 now mtime size path name
  now=$(date +%s)
  NEWEST_NAME=""
  NEWEST_SIZE=0
  NEWEST_MTIME=0
  ACTIVE_PARTS=""
  STALE_PARTS=""
  while read -r mtime size path; do
    [[ -n ${path:-} && $mtime =~ ^[0-9]+$ && $size =~ ^[0-9]+$ ]] || continue
    name=${path##*/}
    case $name in
      .auto_*.part)
        if ((now - mtime < PART_ACTIVE_S)); then
          ACTIVE_PARTS+="${name} "
        else
          STALE_PARTS+="${name} "
        fi
        ;;
      auto_*.dump)
        if ((mtime > NEWEST_MTIME)); then
          NEWEST_MTIME=$mtime
          NEWEST_SIZE=$size
          NEWEST_NAME=$name
        fi
        ;;
    esac
  done <<<"$listing"
  return 0
}

check_db_dump() {
  local source listing waited=0 age pending
  if ((DUMP_DIR_READABLE)); then
    source="local"
  elif ((DRY_RUN && !IS_ROOT && KUBECTL_OK)); then
    source="pod"
    info "contrôle du dump INDIRECT : liste lue via kubectl exec deploy/gis-api (/backups), lecture seule"
  else
    skip "fraîcheur du dump : ${DB_BACKUP_DIR} non lisible — lancer en root pour le contrôle complet"
    return 0
  fi

  while :; do
    if [[ $source == "local" ]]; then
      listing=$(list_dump_files_local)
    elif ! listing=$(list_dump_files_pod); then
      skip "fraîcheur du dump : lecture via le pod gis-api impossible — lancer en root pour le contrôle complet"
      return 0
    fi
    analyse_listing "$listing"
    # Un Job en retard (image en cours de téléchargement) n'a pas encore créé son .part :
    # le statut du CronJob est lu à chaque tour.
    if ((KUBECTL_OK)) && ! read_cronjob_status; then
      fail "statut du CronJob ${DB_BACKUP_CRONJOB} illisible (kubectl get cronjob ${DB_BACKUP_CRONJOB} -n ${NAMESPACE})"
      return 0
    fi

    if [[ -z $ACTIVE_PARTS && -z $CRON_ACTIVE ]]; then
      break
    fi
    pending=$(pending_desc)
    if ((DRY_RUN)); then
      warn "dump en cours (${pending}) : en exécution réelle, attente jusqu'à $((PART_WAIT_MAX_S / 60)) min"
      break
    fi
    if ((waited >= PART_WAIT_MAX_S)); then
      fail "dump toujours en cours après $((PART_WAIT_MAX_S / 60)) min (${pending}) — CronJob ${DB_BACKUP_CRONJOB} anormalement long ?"
      return 0
    fi
    info "dump en cours (${pending}) : nouvelle vérification dans ${PART_POLL_S} s (attente cumulée ${waited} s)"
    sleep "$PART_POLL_S"
    waited=$((waited + PART_POLL_S))
  done

  if [[ -n $STALE_PARTS ]]; then
    warn "fichier(s) partiel(s) abandonné(s), ignoré(s) (tentative interrompue ; hors plan Acronis, qui ne vise que le lien dur) : ${STALE_PARTS% }"
  fi
  if [[ -z $NEWEST_NAME ]]; then
    fail "aucun fichier auto_*.dump dans le répertoire des sauvegardes de la base"
    return 0
  fi

  age=$(($(date +%s) - NEWEST_MTIME))
  info "dump le plus récent : ${NEWEST_NAME}, $(human_bytes "$NEWEST_SIZE"), $(date -u -d "@${NEWEST_MTIME}" +%Y-%m-%dT%H:%M:%SZ), âge $(human_duration "$age")"
  DUMP_OK=1
  if ((age > MAX_DUMP_AGE_S)); then
    fail "dump trop ancien ($(human_duration "$age") > $((MAX_DUMP_AGE_S / 3600)) h) — le CronJob db-backup a échoué ? (kubectl get jobs -n ${NAMESPACE})"
    DUMP_OK=0
  elif ((age < -300)); then
    warn "date du dump dans le futur ($(human_duration "$age")) : horloge du serveur à vérifier"
  fi
  if ((NEWEST_SIZE < MIN_DUMP_BYTES)); then
    fail "dump trop petit ($(human_bytes "$NEWEST_SIZE") < $(human_bytes "$MIN_DUMP_BYTES")) — dump tronqué ou base vide ?"
    DUMP_OK=0
  fi
  # Seuil de 26 h insuffisant seul : si le Job de la nuit échoue, le dump de la
  # veille a 24 h 45 à 25 h 15 quand la sauvegarde part, et serait recopié.
  if ((CRON_READ)); then
    if [[ $CRON_SUSPEND == "true" ]]; then
      fail "CronJob ${DB_BACKUP_CRONJOB} suspendu (spec.suspend=true) : plus aucun dump nocturne"
      DUMP_OK=0
    fi
    if ((CRON_SCHED_S == 0)); then
      fail "CronJob ${DB_BACKUP_CRONJOB} : aucun passage enregistré (status.lastScheduleTime vide)"
      DUMP_OK=0
    elif ((NEWEST_MTIME < CRON_SCHED_S)); then
      fail "aucun auto_*.dump depuis le dernier passage du CronJob ($(date -u -d "@${CRON_SCHED_S}" +%Y-%m-%dT%H:%M:%SZ)) — kubectl get jobs -n ${NAMESPACE}"
      DUMP_OK=0
    else
      info "CronJob ${DB_BACKUP_CRONJOB} : dernier passage $(date -u -d "@${CRON_SCHED_S}" +%Y-%m-%dT%H:%M:%SZ), dump postérieur, aucun Job actif"
    fi
  elif ((DUMP_OK)); then
    skip "statut du CronJob ${DB_BACKUP_CRONJOB} : kubectl indisponible"
  fi
  if ((DUMP_OK)); then
    info "fraîcheur du dump : OK (< $((MAX_DUMP_AGE_S / 3600)) h, >= $(human_bytes "$MIN_DUMP_BYTES"), postérieur au CronJob)"
  fi
  return 0
}

# --- Dry-run : comptage des objets (aucun contenu lu) ---------------------------
count_objects() {
  local entry ns kind required n err
  local -a nsargs
  if ((!KUBECTL_OK)); then
    skip "comptage des objets Kubernetes : kubectl indisponible"
    return 0
  fi
  for entry in "${EXPORTS[@]}"; do
    IFS='|' read -r ns kind required <<<"$entry"
    if [[ $required != "oui" ]] && ! resource_exists "$kind"; then
      info "type ${kind} absent du cluster : ignoré"
      continue
    fi
    nsargs=()
    if [[ -n $ns ]]; then nsargs=(-n "$ns"); fi
    if n=$(kc get "$kind" "${nsargs[@]}" --no-headers 2>/dev/null | wc -l); then
      info "kubectl get ${kind} ${ns:+-n ${ns} }--no-headers | wc -l = ${n}"
      if [[ $required == "oui" && $kind =~ ^(secrets|deployments)$ ]] && ((n == 0)); then
        fail "aucun objet ${kind} dans ${ns} : mauvais cluster ou kubeconfig ?"
      fi
    else
      err=$(kc get "$kind" "${nsargs[@]}" -o name 2>&1 >/dev/null | head -n 1 || true)
      if [[ $required == "oui" ]]; then
        fail "kubectl get ${kind} impossible : ${err}"
      else
        warn "kubectl get ${kind} impossible : ${err}"
      fi
    fi
  done
  return 0
}

# --- Exécution réelle : export ------------------------------------------------
export_kubernetes() {
  local dest=$1 entry ns kind required file count errfile
  local -a nsargs
  errfile="${dest}/.kubectl.err"
  mkdir -p -- "${dest}/kubernetes"
  for entry in "${EXPORTS[@]}"; do
    IFS='|' read -r ns kind required <<<"$entry"
    if [[ $required != "oui" ]] && ! resource_exists "$kind"; then
      info "type ${kind} absent du cluster : ignoré"
      continue
    fi
    nsargs=()
    if [[ -n $ns ]]; then nsargs=(-n "$ns"); fi
    file="${dest}/kubernetes/${ns:-cluster}_${kind}.yaml"
    # La sortie YAML part DIRECTEMENT dans le fichier 0600 : jamais sur la console.
    if kc get "$kind" "${nsargs[@]}" -o yaml >"$file" 2>"$errfile"; then
      count=$(grep -c '^- apiVersion:' "$file" || true)
      EXPORT_COUNTS["${ns:-cluster} ${kind}"]=$count
      info "exporté : kubernetes/${file##*/} (${count} objet(s))"
      if [[ $required == "oui" && $kind =~ ^(secrets|deployments)$ ]] && ((count == 0)); then
        fail "aucun objet ${kind} exporté : mauvais cluster ou kubeconfig ?"
      fi
    else
      rm -f -- "$file"
      if [[ $required == "oui" ]]; then
        fail "export ${kind} impossible : $(head -c 300 "$errfile" | tr '\n' ' ')"
      else
        warn "export ${kind} impossible : $(head -c 300 "$errfile" | tr '\n' ' ')"
      fi
    fi
  done
  rm -f -- "$errfile"
  return 0
}

copy_host_files() {
  local dest=$1 f
  mkdir -p -- "${dest}/serveur"
  if cp -R --preserve=timestamps -- "$REPO_K8S_DIR" "${dest}/serveur/k8s"; then
    info "copié : ${REPO_K8S_DIR} -> serveur/k8s/"
  else
    fail "copie de ${REPO_K8S_DIR} impossible"
  fi
  if cp --preserve=timestamps -- "$ENVIRONMENT_TS" "${dest}/serveur/environment.ts"; then
    info "copié : ${ENVIRONMENT_TS} -> serveur/environment.ts"
  else
    fail "copie de ${ENVIRONMENT_TS} impossible"
  fi
  for f in "${OPTIONAL_HOST_FILES[@]}"; do
    if [[ -f $f ]]; then
      if cp --preserve=timestamps -- "$f" "${dest}/serveur/${f##*/}"; then
        info "copié : ${f} -> serveur/${f##*/}"
      else
        warn "copie de ${f} impossible"
      fi
    fi
  done
  return 0
}

write_manifest() {
  local dest=$1 sha=$2 key dump_path
  dump_path="${DB_BACKUP_DIR}/${NEWEST_NAME}"
  {
    echo "Calypso — export de configuration pré-sauvegarde Acronis (serveur TN)"
    echo "======================================================================"
    echo "Date (UTC)          : $(ts)"
    echo "Hôte                : $(hostname)"
    echo "Script              : calypso-pre-backup ${SCRIPT_VERSION}"
    echo "Kubeconfig          : ${KUBECONFIG_DESC}"
    echo
    echo "Dump PostgreSQL retenu (le plus récent) :"
    echo "  fichier           : ${dump_path}"
    echo "  taille            : ${NEWEST_SIZE} octets ($(human_bytes "$NEWEST_SIZE"))"
    echo "  date (UTC)        : $(date -u -d "@${NEWEST_MTIME}" +%Y-%m-%dT%H:%M:%SZ)"
    echo "  sha256            : ${sha}"
    if ((LINK_LATEST)); then
      echo "  lien dur publié   : ${LATEST_DUMP_DIR}/${NEWEST_NAME} (+ SHA256SUMS)"
    else
      echo "  lien dur publié   : non (option --link-latest-dump absente)"
    fi
    echo
    echo "Objets Kubernetes exportés (nombre par type) :"
    for key in "${!EXPORT_COUNTS[@]}"; do
      printf '  %-50s %s\n' "$key" "${EXPORT_COUNTS[$key]}"
    done | sort
    echo
    echo "Fichiers exportés (noms seulement) :"
    (cd -- "$dest" && find . -type f ! -name MANIFEST.txt -printf '  %P\n' | sort)
    echo "  MANIFEST.txt"
  } >"${dest}/MANIFEST.txt"
  return 0
}

# Remplace target par src : ancien -> .<nom>.old.PID, src -> target, suppression de l'ancien.
swap_into_place() {
  local src=$1 target=$2 old=""
  if [[ -e $target ]]; then
    old="${target%/*}/.${target##*/}.old.$$"
    mv -T -- "$target" "$old"
  fi
  mv -T -- "$src" "$target"
  chmod 0700 -- "$target"
  if [[ -n $old ]]; then
    rm -rf -- "$old"
  fi
  return 0
}

# Répertoire temporaire contenant UNIQUEMENT un lien dur vers le dump retenu.
# Pas de chmod/chown sur le lien : il partage l'inode du fichier d'origine, que
# le pod gis-api liste et télécharge (écran /admin/database).
prepare_latest_link() {
  local sha=$1 parent tmp
  parent=${LATEST_DUMP_DIR%/*}
  if [[ $(stat -c %d -- "$DB_BACKUP_DIR") != "$(stat -c %d -- "$parent")" ]]; then
    die "${DB_BACKUP_DIR} et ${parent} ne sont pas sur le même système de fichiers : lien dur impossible (retirer --link-latest-dump)"
  fi
  tmp=$(mktemp -d "${parent}/.${LATEST_DUMP_DIR##*/}.tmp.XXXXXX")
  TMP_LATEST=$tmp
  chmod 0700 -- "$tmp"
  ln -- "${DB_BACKUP_DIR}/${NEWEST_NAME}" "${tmp}/${NEWEST_NAME}"
  printf '%s  %s\n' "$sha" "$NEWEST_NAME" >"${tmp}/SHA256SUMS"
  chmod 0600 -- "${tmp}/SHA256SUMS"
  return 0
}

run_export() {
  local parent tmp sha size_after mtime_after free_kb leftover
  parent=${EXPORT_DIR%/*}
  mkdir -p -- "$parent"
  free_kb=$(df -Pk "$parent" | awk 'NR==2 { print $4 }')
  if ((free_kb < EXPORT_MIN_FREE_KB)); then
    die "espace libre insuffisant dans ${parent} (${free_kb} Ko)"
  fi

  # Restes d'une exécution interrompue (le verrou garantit qu'aucune autre ne tourne).
  shopt -s nullglob
  for leftover in "${parent}/.${EXPORT_DIR##*/}".tmp.* "${parent}/.${EXPORT_DIR##*/}".old.* \
    "${parent}/.${LATEST_DUMP_DIR##*/}".tmp.* "${parent}/.${LATEST_DUMP_DIR##*/}".old.*; do
    rm -rf -- "$leftover"
  done
  shopt -u nullglob

  tmp=$(mktemp -d "${parent}/.${EXPORT_DIR##*/}.tmp.XXXXXX")
  TMP_EXPORT=$tmp
  chmod 0700 -- "$tmp"

  export_kubernetes "$tmp"
  copy_host_files "$tmp"
  if ((FAILURES > 0)); then
    die "${FAILURES} erreur(s) pendant l'export — export précédent conservé, sauvegarde à ne pas lancer"
  fi

  info "calcul du sha256 de ${NEWEST_NAME} ($(human_bytes "$NEWEST_SIZE"))…"
  sha=$(sha256sum -- "${DB_BACKUP_DIR}/${NEWEST_NAME}" | cut -d' ' -f1)
  size_after=$(stat -c %s -- "${DB_BACKUP_DIR}/${NEWEST_NAME}")
  mtime_after=$(stat -c %Y -- "${DB_BACKUP_DIR}/${NEWEST_NAME}")
  if [[ $size_after != "$NEWEST_SIZE" || $mtime_after != "$NEWEST_MTIME" ]]; then
    die "${NEWEST_NAME} a été modifié pendant le calcul du sha256 — relancer"
  fi
  info "sha256 ${NEWEST_NAME} = ${sha}"

  write_manifest "$tmp" "$sha"
  find "$tmp" -type d -exec chmod 0700 {} +
  find "$tmp" -type f -exec chmod 0600 {} +
  chown -R root:root -- "$tmp"

  if ((LINK_LATEST)); then
    prepare_latest_link "$sha"
  fi

  swap_into_place "$tmp" "$EXPORT_DIR"
  TMP_EXPORT=""
  info "export publié : ${EXPORT_DIR} ($(find "$EXPORT_DIR" -type f | wc -l) fichiers, MANIFEST.txt compris)"

  if ((LINK_LATEST)); then
    swap_into_place "$TMP_LATEST" "$LATEST_DUMP_DIR"
    TMP_LATEST=""
    info "lien publié : ${LATEST_DUMP_DIR}/${NEWEST_NAME} (lien dur, aucun octet copié) + SHA256SUMS"
  fi
  return 0
}

# --- Déroulé ------------------------------------------------------------------
setup_kubectl
check_paths
check_volume_path "$DB_BACKUP_PVC" "$DB_BACKUP_DIR"
check_volume_path "$UPLOADS_PVC" "$UPLOADS_DIR"
check_db_dump

if ((DRY_RUN)); then
  count_objects
  info "bilan --dry-run : ${FAILURES} contrôle(s) en échec, ${WARNINGS} avertissement(s), ${SKIPPED} contrôle(s) non effectué(s)"
  if ((SKIPPED > 0)); then
    info "contrôles non effectués : lancer en root pour le contrôle complet (sudo /usr/local/sbin/calypso-pre-backup.sh --dry-run)"
  fi
  if ((FAILURES > 0)); then
    exit 1
  fi
  exit 0
fi

if ((!LINK_LATEST)); then
  if [[ -e $LATEST_DUMP_DIR ]]; then
    fail "option --link-latest-dump absente : ${LATEST_DUMP_DIR} garderait un dump PÉRIMÉ que le plan Acronis enverrait — ajouter l'argument dans la commande pré-sauvegarde"
  else
    warn "option --link-latest-dump absente : aucun dump publié pour le plan Acronis (argument obligatoire sur TN)"
  fi
fi
if ((FAILURES > 0)); then
  die "${FAILURES} contrôle(s) en échec — rien n'est exporté, la sauvegarde ne doit pas être lancée"
fi
if ((SKIPPED > 0 || !DUMP_OK)); then
  die "contrôle incomplet (${SKIPPED} non effectué(s)) — la sauvegarde ne doit pas être lancée"
fi

run_export
info "pré-sauvegarde terminée : ${WARNINGS} avertissement(s)"
exit 0
