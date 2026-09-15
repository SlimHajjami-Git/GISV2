# Sauvegarde hors site Acronis — serveur TN (production)

Offre « Topnet Pro » = **Acronis Cyber Protect Cloud**, console `https://eu-cloud.acronis.com` (tenant Topnet).
**Réponses de Topnet (15/09/2026)** : stockage cloud hébergé **en Tunisie** (données clients sur le territoire, INPDP) ;
quota **100 Go** — trois fois le besoin estimé (§9). L'hôte de stockage se lira dans la console après la première sauvegarde et le port 40440 ne concerne que le côté stockage (§11) ;
reste à demander si le **stockage immuable** est activé.
Périmètre : **TN uniquement** (alias SSH `belive-tn`, hôte `vm-belive-1`). **Le serveur DZ n'est jamais utilisé**, ni pour la sauvegarde ni pour le test de restauration (décision de Slim, 15/09/2026) : le test se fait sur le PC de Slim.

> Toutes les commandes `sudo` / root de ce document sont à lancer **par Slim** (pas de sudo non interactif sur TN).
> Aucun mot de passe, jeton ou IP ici : les emplacements sont notés `<JETON>`, `<LIEN>`, `<MOT_DE_PASSE_CHIFFREMENT>`.

## Sommaire

0. [En bref et décisions à prendre](#0-en-bref-et-décisions-à-prendre)
1. [Constats sur TN (15/09/2026)](#1-constats-sur-tn-15092026)
2. [Préparation du serveur](#2-préparation-du-serveur)
3. [Installation de l'agent](#3-installation-de-lagent)
4. [Script pré-sauvegarde](#4-script-pré-sauvegarde)
5. [Plan de protection dans la console](#5-plan-de-protection-dans-la-console)
6. [Première sauvegarde manuelle](#6-première-sauvegarde-manuelle)
7. [Test de restauration sur le PC](#7-test-de-restauration-sur-le-pc)
8. [Surveillance](#8-surveillance)
9. [Estimation du quota](#9-estimation-du-quota)
10. [Désinstallation](#10-désinstallation)
11. [Dépannage](#11-dépannage)
12. [Doc et sources](#12-doc-et-sources)

---

## 0. En bref et décisions à prendre

**Principe** : sauvegarde **au niveau fichiers** (pas image disque), **sans instantané**, de ce qui ne se reconstruit
pas depuis git :

| Donnée | Chemin sur TN | Pourquoi |
|---|---|---|
| Dump PostgreSQL de la nuit | `/var/backups/calypso-db-latest/` = **lien dur** vers le dump du jour, publié par le script | seule copie **cohérente** de la base (28 Go en ligne) |
| Fichiers clients | volume `gis-uploads` (14 Mo) | logos, justificatifs, photos |
| Objets K8s vivants (secrets patchés à chaud, configmaps, deployments…) | `/var/backups/calypso-config/` (produit par le script) | `update.sh config` et le dépôt ne les reflètent pas |
| Config propre au serveur | `/home/master/GISV2/k8s/`, `environment.ts` | modifiées localement sur TN |

**Non sauvegardé, volontairement** : le volume `postgres-data` brut (copier les fichiers d'une base en marche ne donne pas
une copie fiable ; le dump de 02:30 le remplace), le répertoire `db-backups` entier (voir ci-dessous), les images Docker
(reconstruites depuis `master`), Valhalla/Nominatim (régénérables, volumineux), Redis/RabbitMQ (transitoires).

**Pourquoi jamais tout `db-backups`** : `/` est en **XFS**, et la doc Acronis indique que les filtres d'exclusion de
fichiers **ne fonctionnent pas sur XFS**. Viser tout le répertoire enverrait chaque nuit les 6 dumps, un éventuel
`.auto_*.part` abandonné et les copies `weekly_*`. Le lien dur (`--link-latest-dump`) est donc **obligatoire**.

**Réglages cibles** : stockage Cloud Acronis, tous les jours à 03:30 UTC **exactement** (pas de répartition aléatoire),
rétention 7 quotidiennes / 4 hebdomadaires / 6 mensuelles, chiffrement AES-256, aucun instantané, commande pré-sauvegarde
qui **fait échouer la sauvegarde** si le dump est absent, périmé, tronqué, antérieur au dernier passage du CronJob, ou
encore en cours.

**Décisions à prendre (Slim)** :

1. **Version de l'agent** (§3) : n'installer qu'une version **≥ 26.8.43120**. Le disque de TN est multiqueue ; 26.8.43120
   corrige un plantage noyau au déchargement du pilote d'instantané (« par exemple pendant une mise à jour de l'agent »).
   Le 15/09, eu-cloud distribue encore 26.8.42957 : **attendre**, et désactiver la mise à jour automatique dès
   l'enregistrement.
2. **Module noyau ou pas** (§2.2) : voie documentée = installer `gcc make dkms libelf-dev rpm` et laisser l'installeur
   compiler `snapapi`. Voie non documentée = ne pas installer `gcc` (seulement `rpm`) : aucun module noyau chargé sur la
   prod, sauvegarde fichiers « très probablement » fonctionnelle mais **à tester**.
3. **Espace disque** (§2.1) : `/` est entre 81 et 83 % ; libérer de la place avant l'installation (2 Go requis).
4. **Redémarrage en attente** (§2.3) : un noyau 6.8.0-139 attend ; à traiter dans sa propre fenêtre, **pas** couplé à Acronis.
5. **Test de restauration** (§7) : sur le **PC de Slim** (Docker Desktop, conteneur jetable sans port publié), jamais sur DZ.
   Le dump contient les données de tous les clients : disque du PC chiffré (BitLocker) recommandé, nettoyage le jour même.

---

## 1. Constats sur TN (15/09/2026)

Relevés en lecture seule (utilisateur `master`) :

| Point | Valeur |
|---|---|
| Système | Ubuntu 24.04.3 LTS, x86_64, VM VMware (`open-vm-tools`) |
| Noyau en cours | `6.8.0-124-generic`, compilé avec **gcc-13 13.3.0** (`/proc/version`) |
| Noyau installé en attente | `6.8.0-139-generic` (+ ses en-têtes) ; `/var/run/reboot-required` présent |
| Mises à jour Ubuntu | `unattended-upgrades` **actif** : les noyaux arrivent seuls, `dkms` reconstruira `snapapi` au démarrage suivant |
| Démarrage | BIOS (pas de `/sys/firmware/efi`, pas de `mokutil`) → **pas de Secure Boot**, pas d'enrôlement MOK |
| Disque système | `sda`, **multiqueue** (`/sys/block/sda/mq`, ordonnanceur `mq-deadline`) |
| Système de fichiers `/` | **xfs** sur LVM (`/dev/mapper/ubuntu--vg-root`) ; `/var/lib/rancher` et `/var/backups` sur le même volume |
| Disque `/` | 97 Go, **81 à 83 %** utilisés selon l'heure (17,7 à 19 Go libres) — au-dessus du seuil de 80 % fixé après la panne du 09/09 |
| Paquets | `perl` et `linux-headers-6.8.0-124-generic` présents ; **`gcc`, `make`, `dkms`, `libelf-dev`, `rpm` absents** |
| Simulation `apt-get -s --no-install-recommends install gcc make dkms libelf-dev rpm linux-headers-$(uname -r)` | 49 nouveaux paquets, **0 mis à jour** |
| Fuseau horaire | `Etc/UTC` |
| Flux sortants (test de connexion TCP) | **ouverts** : `eu-cloud.acronis.com` 443, 8443, 44445, 7770, 7780, 7800 ; `agents-eu-cloud` 443 ; `rs-eu-cloud` 8443 ; `bc-baas` 443 et 8443 ; `fes-baas` 44445 ; `dl.managed-protection.com` 443 ; `cloud-wr-eu1` 5060. **Non testé** : 40440 (hôte de stockage inconnu, voir §11) |
| kubectl | `/usr/local/bin/kubectl` → `k3s` v1.34.4+k3s1 ; `/etc/rancher/k3s/k3s.yaml` lisible |
| Base `gis_v2` | 28 Go (dont `gps_positions` 26 Go), PostgreSQL 16.4, PostGIS 3.4.3, 88 tables |
| CronJob `db-backup` | 02:30 UTC ; `lastScheduleTime` 2026-09-15T02:30:00Z, `lastSuccessfulTime` 02:45:56Z ; écrit `.auto_*.part`, `pg_restore -l`, puis `mv` |
| Dumps (`/backups` vu depuis `gis-api`) | `auto_20260913` 1,79 Go ; `auto_20260914` 1,82 Go ; `auto_20260915-023001` **1,85 Go, terminé à 02:45:52 UTC** ; `weekly_20260907` 1,65 Go ; `weekly_20260914` 1,82 Go (copie du lundi) ; `manual_20260721` 1,94 Go — **total 10,9 Go** |
| Objets gisv2 | 6 secrets, 4 configmaps, 6 deployments, 3 statefulsets, 1 cronjob, 12 services, 8 ingresses, 7 PVC ; 7 PV ; plus 1 IngressRoute, 2 Middlewares Traefik, 3 Certificates cert-manager, 1 HelmChartConfig `traefik` (kube-system), 1 ClusterIssuer |

Le secret `belive-calypso-tls` contient **la clé privée du certificat GlobalSign acheté** : l'export de configuration est
donc aussi sensible qu'un coffre de mots de passe. Le chiffrement du plan n'est pas une option.

---

## 2. Préparation du serveur

### 2.1 Espace disque

L'installation de l'agent demande **2 Go libres** (doc « System requirements »). Avant, appliquer la purge habituelle
(voir CLAUDE.md, panne du 09/09) :

```bash
df -h /
docker builder prune -af
docker images --format '{{.Repository}}:{{.Tag}} {{.CreatedSince}}' | grep 'gisv2/'
kubectl get deploy -n gisv2 -o jsonpath='{range .items[*]}{.metadata.name}{"\t"}{.spec.template.spec.containers[*].image}{"\n"}{end}'
# supprimer (docker rmi) les vieux tags gisv2/* en gardant le tag déployé et le précédent
df -h /        # objectif : sous 80 %
```

Refaire `df -h /` après l'installation (agent, journaux et cache sous `/usr/lib/Acronis`, `/var/lib/Acronis`, `/var/log`).

### 2.2 Paquets

La doc Acronis « Linux packages » exige : en-têtes du noyau **de la même version que le noyau en cours**, **gcc de la même
version que celle qui a compilé le noyau** (gcc-13 ici ; c'est le `gcc` par défaut d'Ubuntu 24.04), `make`, `perl`,
`libelf-dev` (noyaux ≥ 4.15 compilés avec `CONFIG_UNWINDER_ORC=y`), et **`rpm` sur Ubuntu/Debian**. `dkms` n'est pas dans
cette liste, mais la KB 62490 indique que c'est `dkms_autoinstaller` qui reconstruit `snapapi` sur un nouveau noyau :
recommandé, puisque `unattended-upgrades` installe des noyaux seul.

**Voie documentée (par défaut)** :

```bash
sudo apt-get update
sudo apt-get -s --no-install-recommends install gcc make dkms libelf-dev rpm "linux-headers-$(uname -r)" \
  | grep -E '^(Inst|[0-9]+ upgraded)'          # attendu : « 0 upgraded » (49 paquets le 15/09)
sudo apt-get install -y --no-install-recommends gcc make dkms libelf-dev rpm "linux-headers-$(uname -r)"
gcc --version | head -1                        # attendu : gcc (Ubuntu 13.3.0-...) 13.3.0
dpkg -l "linux-headers-$(uname -r)" linux-headers-6.8.0-139-generic | grep ^ii   # les deux présents
```

**Voie sans module noyau (non documentée, décision §0)** : n'installer que `rpm`
(`sudo apt-get install -y --no-install-recommends rpm`). L'installeur finira par « Failed to build the SnapAPI kernel
module. Operations with disk-level backups will not be available. » : seules les sauvegardes disque/machine sont alors
perdues selon la KB 62490, ce qui ne concerne pas ce plan — mais Acronis n'écrit nulle part qu'une sauvegarde fichiers
fonctionne sans module. **À valider par la première sauvegarde (§6).**

### 2.3 Redémarrage en attente (noyau 6.8.0-139)

Ne **pas** coupler le redémarrage de la prod à l'installation d'Acronis (redémarrage de toute la prod K3s, soucis réseau
K3s déjà vécus après un redémarrage de Docker) : l'agent s'installe sur le noyau **en cours** ; au redémarrage suivant,
dans sa propre fenêtre de maintenance, `dkms` reconstruit `snapapi` pour 6.8.0-139 (vérification : `dkms status`, §11).

Le plan **ne crée aucun instantané** (§5.4) : les fichiers visés sont figés au moment de la sauvegarde (dump renommé à la
fin de son écriture, export écrit par le script avant le départ). `snapapi` n'est donc **pas nécessaire** à cette
sauvegarde, et le pilote d'instantané n'intervient pas sur le disque multiqueue pendant les envois.

### 2.4 Secure Boot

Pas d'UEFI sur cette VM : aucun enrôlement MOK à prévoir (cette étape n'existe que sur les machines UEFI avec Secure Boot).

---

## 3. Installation de l'agent

### 3.1 Dans la console

1. `https://eu-cloud.acronis.com` → **Appareils** (Devices) → **Tous les appareils** → **Ajouter** → canal **Latest**
   → **Linux** : copier le lien du bouton de téléchargement : `<LIEN>`. Le fichier s'appelle aujourd'hui
   `CyberProtect_AgentForLinux_x86_64.bin`.
2. Même écran, **Jeton d'enregistrement** → **Générer** : durée de vie courte (1 jour suffit), utilisateur = celui sous
   lequel la machine doit apparaître, **aucun plan** associé → **Générer le jeton** → **Copier** : `<JETON>`
   (format `XXXX-XXXX-XXXX`). Le jeton n'entre ni dans le dépôt, ni dans un ticket, ni dans une conversation.

### 3.2 Sur TN (root, par Slim)

Pourquoi ce déroulé : `sudo ./agent.bin … --token=…` écrirait la ligne complète, jeton compris, dans `/var/log/auth.log`
(lisible par le groupe `adm`) et le journal système, et la rendrait visible dans `ps`. Avec `sudo -i`, sudo ne journalise
que `/bin/bash` ; `set +o history` évite l'historique ; `--options-file` (recommandé par Acronis) tient le jeton hors de
la ligne de commande.

```bash
sudo -i
set +o history
umask 077
cd /root
wget -O CyberProtect_AgentForLinux_x86_64.bin '<LIEN>'
chmod 700 CyberProtect_AgentForLinux_x86_64.bin

# 1) Version : EXIGER >= 26.8.43120 (décision §0.1). Sinon : STOP, supprimer le .bin, attendre.
./CyberProtect_AgentForLinux_x86_64.bin --product-info

# 2) Options de CETTE version : confirmer --options-file, --id, --rain, --token ; noter si --disable-file-protector existe
./CyberProtect_AgentForLinux_x86_64.bin --help | grep -E -- '--options-file|--id|--rain|--token|--disable-file-protector|--language'

# 3) Fichier d'options (0600 root grâce à umask 077) — remplacer <JETON>
#    Si le --help liste --disable-file-protector, ajouter la ligne --disable-file-protector (module noyau inutile ici).
cat > /root/acronis-install.opts <<'EOF'
--id=BackupAndRecoveryAgent
--rain=https://eu-cloud.acronis.com
--token=<JETON>
--language=fr
EOF

# 4) Installation silencieuse de l'agent Linux seul + enregistrement par jeton
./CyberProtect_AgentForLinux_x86_64.bin -a --options-file=/root/acronis-install.opts; echo "code=$?"
shred -u /root/acronis-install.opts
```

`--id=BackupAndRecoveryAgent` limite l'installation à l'agent Linux (sans lui : Virtuozzo, Oracle, MySQL et Proxmox en
plus). `--rain` = URL exacte du datacenter affichée après connexion. `--token` est incompatible avec `--login`/`--password`.
L'installeur fait son propre test de connectivité ; journal : `/var/log/trueimage-setup.log`.

**Si l'installeur refuse le fichier d'options** (format « une option par ligne » non documenté) : installer puis enregistrer
en deux temps, toujours dans le même shell root sans historique. Le jeton est alors visible dans `ps` le temps de
l'enregistrement (quelques secondes, utilisateurs locaux seulement), jamais dans `auth.log` ni l'historique :

```bash
shred -u /root/acronis-install.opts 2>/dev/null
./CyberProtect_AgentForLinux_x86_64.bin -a --id=BackupAndRecoveryAgent --skip-registration --language=fr; echo "code=$?"
read -rs TOKEN            # coller <JETON> puis Entrée (rien ne s'affiche)
/usr/lib/Acronis/RegisterAgentTool/RegisterAgent -o register -t cloud -a https://eu-cloud.acronis.com --token "$TOKEN"; echo "code=$?"
unset TOKEN
```

### 3.3 Vérifications (toujours root)

```bash
systemctl status acronis_mms --no-pager | head -5
systemctl list-units --type=service | grep -Ei 'acronis|aakore|mms'   # services de l'agent actifs
lsmod | grep -E 'snapapi|file_protector'                              # voie documentée : snapapi ; file_protector vide si désactivé
dkms status
dmesg -T | tail -n 30                                                 # aucune trace d'oops/BUG
kubectl get nodes; kubectl get pods -n gisv2                          # prod intacte
df -h /
rm -f /root/CyberProtect_AgentForLinux_x86_64.bin
exit
```

### 3.4 Dans la console, **tout de suite après l'enregistrement**

1. La machine `vm-belive-1` apparaît dans **Appareils**.
2. **Paramètres → Agents** → sélectionner `vm-belive-1` → **désactiver la mise à jour automatique** (défaut : canal
   Latest, fenêtre 23:00–08:00 heure machine = UTC, soit la nuit même). Les mises à jour se feront **à la main**, dans une
   fenêtre de maintenance, avec contrôle de `dmesg` et de K3s après coup.
3. **Révoquer le jeton** s'il est encore valide.

---

## 4. Script pré-sauvegarde

Source : `scripts/acronis/pre-backup-tn.sh` (bash, fins de ligne LF), version 1.1. Installé en
`/usr/local/sbin/calypso-pre-backup.sh`. Indépendant de l'agent : peut être mis en place **avant** l'installation.

### 4.1 Ce qu'il fait

| Étape | Détail | En cas de problème |
|---|---|---|
| Environnement | `PATH` fixe, `LC_ALL=C`, `unset LD_LIBRARY_PATH LD_PRELOAD` (consigne Acronis), `HOME` forcé | — |
| Durée | relancé une fois sous `timeout -k 60 2400` : **40 min maximum** (la doc Acronis ne documente aucun délai) | TERM → nettoyage → code **124** → sauvegarde en échec |
| Chemins | `k8s/`, `environment.ts`, volumes `db-backups` et `gis-uploads` | échec |
| Volumes | le chemin réel du PV (via kubectl) doit être celui codé dans le script **et** dans le plan | échec : « mettre à jour CE script ET les chemins du plan » |
| Dump en cours | un `.auto_*.part` modifié il y a moins de 30 min **ou un Job du CronJob `db-backup` encore actif** → nouvelle vérification toutes les 30 s, 20 min maximum | échec après 20 min |
| Fraîcheur | le plus récent `auto_*.dump` a moins de **26 h**, pèse au moins **500 Mo**, et est **postérieur au dernier passage du CronJob** (`status.lastScheduleTime`) ; CronJob non suspendu | échec (une nuit sans dump ne passe plus : le dump de la veille a moins de 26 h à 03:30 mais est antérieur à 02:30) |
| Types facultatifs | `kubectl api-resources` vide (délai dépassé) | échec (sinon Traefik/cert-manager seraient sautés sans alerte) |
| Export K8s | secrets, configmaps, deployments, statefulsets, cronjobs, services, ingresses, PVC de `gisv2`, PV ; + IngressRoutes/Middlewares Traefik, Certificates, HelmChartConfig `traefik`, ClusterIssuers s'ils existent | échec (export précédent conservé) |
| Copies | `k8s/`, `environment.ts`, `/etc/rancher/k3s/registries.yaml` (et `config.yaml` s'il existe) | échec (sauf fichiers K3s facultatifs) |
| Empreinte | sha256 du dump retenu, taille et date revérifiées après le calcul | échec si le fichier a bougé |
| `MANIFEST.txt` | date, dump retenu (nom, taille, date, sha256), nombre d'objets par type, **noms** des fichiers exportés | — |
| Publication | répertoire temporaire → `mv` vers `/var/backups/calypso-config` (0700 root, fichiers 0600) | — |
| `--link-latest-dump` (**obligatoire**) | `/var/backups/calypso-db-latest/` = **lien dur** vers le dump retenu + `SHA256SUMS` ; aucun octet copié ; le lien n'est jamais `chmod` (il partage l'inode du fichier lu par `gis-api`) | échec si pas le même système de fichiers ; **échec si l'option manque alors que `calypso-db-latest` existe** (le plan enverrait un dump périmé) |
| Avertissements | disque `/` ≥ 80 %, redémarrage en attente, `.part` abandonné | journal seulement |
| Information | module `snapapi` non chargé (sans effet : aucun instantané) | journal seulement |

Le contenu des secrets n'apparaît **jamais** dans la sortie ni dans le journal (`/var/log/calypso-pre-backup.log`, 0600,
basculé en `.1` au-delà de 1 Mo). Codes de sortie : `0` OK, `1` échec, `2` option inconnue, `124` durée dépassée.
Un verrou (`/run/calypso-pre-backup.lock`) empêche deux exécutions simultanées.

`--dry-run` ne fait que les contrôles, n'écrit rien et n'exporte rien. Sans root, les volumes ne sont pas lisibles :
le script le signale (« lancer en root pour le contrôle complet ») et lit la liste des dumps via
`kubectl exec deploy/gis-api -- stat /backups/...` (lecture seule).

Dry-run du 15/09 à 12:00 UTC (master, v1.1) : **0 échec**, 2 avertissements (disque 81 %, redémarrage en attente),
2 contrôles non effectués (volumes illisibles sans root) ; dump `auto_20260915-023001.dump` 1,8 Go, postérieur au passage
du CronJob de 02:30:00Z ; comptages identiques au §1 ; code 0.

### 4.2 Mise en place (Slim)

```bash
# depuis le PC, dans un checkout à jour
scp scripts/acronis/pre-backup-tn.sh belive-tn:/tmp/pre-backup-tn.sh

# sur TN
grep -c $'\r' /tmp/pre-backup-tn.sh          # doit afficher 0 (sinon : sed -i 's/\r$//' /tmp/pre-backup-tn.sh)
bash -n /tmp/pre-backup-tn.sh && echo syntaxe OK
sudo install -m 0700 -o root -g root /tmp/pre-backup-tn.sh /usr/local/sbin/calypso-pre-backup.sh
rm /tmp/pre-backup-tn.sh

sudo /usr/local/sbin/calypso-pre-backup.sh --dry-run                    # contrôle complet, n'écrit rien
sudo /usr/local/sbin/calypso-pre-backup.sh --link-latest-dump           # 1re exécution réelle
sudo ls -la /var/backups/calypso-config /var/backups/calypso-db-latest
sudo cat /var/backups/calypso-config/MANIFEST.txt                       # noms et comptages uniquement
sudo tail -n 40 /var/log/calypso-pre-backup.log
```

Ne **jamais** afficher `kubernetes/*.yaml` de l'export à l'écran : les secrets y sont en base64, donc en clair.

---

## 5. Plan de protection dans la console

Les libellés ci-dessous sont ceux de la console en français (entre parenthèses : l'anglais de la doc) ; ils peuvent varier
légèrement selon la version.

### 5.1 Quoi sauvegarder

**Éléments à sauvegarder** (What to back up) : *Fichiers/dossiers* → *Éléments à sauvegarder* → *Spécifier* →
*Directement* → *Dossier local* ; saisir chaque chemin puis cliquer sur la flèche :

| Chemin |
|---|
| `/var/backups/calypso-db-latest/` |
| `/var/lib/rancher/k3s/storage/pvc-6c4e0b39-e1d0-48af-86e7-3e3c45688b50_gisv2_gis-uploads/` |
| `/var/backups/calypso-config/` |
| `/home/master/GISV2/k8s/` |
| `/home/master/GISV2/services/gis-frontend/src/environments/environment.ts` |

**Ne pas** ajouter le répertoire du volume `db-backups` : `/` est en XFS et les **filtres de fichiers (exclusions) n'y
fonctionnent pas** (doc « File filters »). **Aucune exclusion** n'est donc configurée : elle serait sans effet.

`k8s/` et `environment.ts` figurent aussi dans `calypso-config` ; les garder à part coûte 120 Ko et permet de les
récupérer sans toucher à l'export qui contient les secrets. Les dumps `manual_*` et `presuppr_*` ne partent pas hors site
(à sauvegarder à part au besoin).

### 5.2 Où, quand, combien de temps

| Réglage | Valeur |
|---|---|
| Nom du plan | `TN-prod-hors-site` |
| **Où** | *Stockage cloud* |
| Modèle de sauvegarde | *Toujours incrémentielle (fichier unique)* (Always incremental, single-file) — **non modifiable après création** |
| **Planification** | *Quotidienne*, tous les jours, à **03:30** — heure de la machine, soit **UTC** sur TN (04:30 à Tunis) |
| Options → *Planification* | **Démarrer toutes les sauvegardes exactement à l'heure planifiée** (au lieu du défaut « Répartir les heures de début dans une fenêtre », retard jusqu'à 30 min) |
| Exécuter les tâches manquées au démarrage | désactivé (défaut) : l'alerte « aucune sauvegarde depuis 2 jours » (§8) couvre ce cas sans charger un redémarrage de la prod |
| **Durée de conservation** | *Par ancienneté des sauvegardes* : **mensuelles 6 mois**, **hebdomadaires 4 semaines**, **quotidiennes 7 jours** ; appliquer **après la sauvegarde** |
| **Chiffrement** | activé (défaut), **AES 256**, mot de passe `<MOT_DE_PASSE_CHIFFREMENT>` — **ne peut plus être désactivé** une fois le plan appliqué |

Marge horaire : le dump de 02:30 s'est terminé à 02:45:52 le 15/09 ; à 03:30 il reste 44 min, plus les 20 min d'attente
du script (qui attend aussi un Job encore actif). Si le dump approche une heure (base en croissance), décaler à 04:00.

**Mot de passe de chiffrement** : long et généré, rangé dans le gestionnaire de mots de passe de Slim **et** dans une
copie hors ligne (coffre / enveloppe). Acronis ne le stocke pas : perdu = toutes les sauvegardes illisibles. Il peut être
changé plus tard (Gestion → Plans de protection → Détails → Changer le mot de passe) sous conditions (format TIBX v12,
deux sauvegardes réussies entre deux changements). Jamais dans le dépôt, ni dans CLAUDE.md, ni dans une conversation.
Si Topnet confirme que le tenant est en *Compliance mode*, le mot de passe se règle **sur la machine** en root
(`/usr/sbin/acropsh -m manage_creds --set-password`) et non dans le plan.

### 5.3 Commande pré-sauvegarde

*Options de sauvegarde* → **Commandes pré/post** → *Exécuter une commande avant la sauvegarde* :

| Champ | Valeur |
|---|---|
| Commande ou fichier batch | `/usr/local/sbin/calypso-pre-backup.sh` |
| Répertoire de travail | `/` |
| Arguments | `--link-latest-dump` (**obligatoire**) |
| ☑ | **Faire échouer la sauvegarde si l'exécution de la commande échoue** |
| ☑ | **Ne pas sauvegarder tant que l'exécution de la commande n'est pas terminée** |

Selon la doc, avec les deux cases cochées (défaut) la sauvegarde ne démarre qu'après une exécution réussie et échoue
sinon ; une commande échoue quand son code de sortie n'est pas nul. Ne **pas** utiliser l'option voisine « Commandes
pré/post de capture de données ». Aucune commande post-sauvegarde n'est nécessaire.

Ce que la doc **ne dit pas** : l'utilisateur qui exécute la commande (a priori root : l'agent `acronis_mms` tourne en
root) et le délai maximum. Le script s'en protège (refus d'une exécution réelle sans root ; `unset LD_*` ; garde-fou de
40 min). Vérifier après la première exécution par Acronis :

```bash
sudo grep 'utilisateur' /var/log/calypso-pre-backup.log | tail -1     # attendu : « …, utilisateur root »
```

### 5.4 Autres options

- *Instantané pour la sauvegarde au niveau fichier* (File-level backup snapshot) : **Ne pas créer d'instantané**
  (défaut Linux). Ni « si possible », ni « toujours ».
- *Gestion des erreurs* : défaut (30 tentatives espacées de 30 s) pour la première sauvegarde ; si le test d'échec (§6.6)
  montre que la commande pré-sauvegarde est elle aussi relancée, réduire (par exemple 3 tentatives, 10 min) pour ne pas
  enchaîner des exécutions pouvant durer 20 min chacune.
- *Notifications par e-mail* : voir §8.
- *Performances et fenêtre de sauvegarde* : défaut ; limiter le débit seulement si l'envoi déborde sur la journée.
- **Désactiver dans le plan les modules autres que la sauvegarde** (protection antimalware, évaluation des
  vulnérabilités, gestion des correctifs, carte de protection des données, etc.) s'ils sont proposés : charge et
  interactions imprévues avec K3s/containerd sur la prod, sans lien avec l'objectif.

---

## 6. Première sauvegarde manuelle

1. `sudo /usr/local/sbin/calypso-pre-backup.sh --dry-run` → `0 contrôle(s) en échec`.
2. Console → **Appareils** → `vm-belive-1` → plan `TN-prod-hors-site` → **Exécuter maintenant**.
3. Suivre **Activités** : la commande pré-sauvegarde dure moins d'une minute (sha256 d'un dump de 1,8 Go + exports),
   puis l'envoi démarre. Premier envoi : environ **1,9 Go**.
4. Pendant l'envoi, sur TN : `df -h /` (stable), `uptime`, `dmesg -T | tail`, et l'application répond
   (`curl -s -o /dev/null -w '%{http_code}\n' https://belive-calypso.com/`).
5. À la fin : `sudo tail -n 20 /var/log/calypso-pre-backup.log`, contrôle « utilisateur root » (§5.3) et, dans la console,
   le point de restauration du jour (5 éléments). Voie sans module noyau (§2.2) : c'est **cette** sauvegarde qui la valide.
6. **Tester le chemin d'échec, sans risque** : mettre temporairement `--test-echec` dans *Arguments* → le script sort
   en code 2 dès la lecture des options, sans rien toucher → la sauvegarde doit apparaître **en échec** et l'e-mail
   d'alerte doit arriver (§8). **Noter** si Acronis relance la commande (gestion des erreurs, §5.4). Remettre ensuite
   `--link-latest-dump` et relancer.

---

## 7. Test de restauration sur le PC

> **Jamais sur DZ** (décision de Slim, 15/09/2026). Le dump contient les données de tous les clients (positions GPS,
> sociétés, utilisateurs) : on le restaure sur le PC de Slim, dans un conteneur Docker jetable **sans port publié**, et on
> efface tout le jour même (§7.5). Espace nécessaire sur le PC : ~2 Go pour le dump + ~35 Go pour la base restaurée
> (153 Go libres sur C: le 15/09). Disque chiffré (BitLocker) recommandé.

À faire après la première sauvegarde, puis au moins **une fois par trimestre**.

### 7.1 Récupérer le dump depuis Acronis (sur TN, dossier temporaire)

Le bouton **Télécharger** de la console est limité à 100 Mo (et refuse les dossiers). On restaure donc via l'agent vers
un dossier temporaire de TN (compter 2 Go ; vérifier `df -h /` avant), puis on rapatrie le fichier sur le PC.
Alternative sans passer par TN : **Web Restore console** (Récupération → *Autres moyens de récupérer* → *Télécharger des
fichiers*, KB 71125), téléchargement direct dans le navigateur du PC — taille maximale non documentée.

Console → `vm-belive-1` → **Récupération** → point de la veille → **Récupérer** → **Fichiers/dossiers** → cocher :

- `/var/backups/calypso-db-latest/` (dump + `SHA256SUMS`) ;
- `/var/backups/calypso-config/MANIFEST.txt`.

→ **Récupérer** → *Emplacement personnalisé* : `/var/tmp/restore-test` → sans écraser → démarrer.

### 7.2 Contrôle d'intégrité sur TN

Acronis écrit en root : le dossier de sortie destiné au transfert est créé explicitement au nom de `master`.

```bash
sudo find /var/tmp/restore-test -type f -printf '%s %p
'      # Acronis peut recréer l'arborescence d'origine
D=$(sudo find /var/tmp/restore-test -name 'auto_*.dump' | head -1); echo "$D"
sudo sh -c "cd '$(dirname "$D")' && sha256sum -c SHA256SUMS"   # attendu : OK
sudo grep -E 'fichier|sha256' "$(sudo find /var/tmp/restore-test -name MANIFEST.txt | head -1)"   # même sha256
sudo install -d -o master -g master -m 0700 /var/tmp/restore-out
sudo mv "$D" /var/tmp/restore-out/
sudo sh -c 'cd /var/tmp/restore-out && sha256sum auto_*.dump > SHA256SUMS.txt'
sudo chown master:master /var/tmp/restore-out/*
sudo rm -rf /var/tmp/restore-test
```

### 7.3 Rapatriement sur le PC (Git Bash)

```bash
mkdir -p ~/restore-test
scp 'belive-tn:/var/tmp/restore-out/*' ~/restore-test/        # ~1,9 Go
cd ~/restore-test && sha256sum -c SHA256SUMS.txt               # attendu : OK
ssh belive-tn 'rm -rf /var/tmp/restore-out && df -h /'        # TN nettoyé dès que le fichier est vérifié sur le PC
```

### 7.4 Restauration dans une base de test isolée (Docker Desktop sur le PC)

Conteneur jetable avec l'image du CronJob (`postgis/postgis:16-3.4-alpine`), **sans port publié** (d'où
l'authentification `trust`, limitée au conteneur). Ne pas réutiliser ni toucher les conteneurs `gisv2-*` existants.

```bash
docker run -d --name gis-restore-test   -e POSTGRES_HOST_AUTH_METHOD=trust   -v "$(cygpath -w ~/restore-test):/restore:ro"   postgis/postgis:16-3.4-alpine
until docker exec gis-restore-test pg_isready -U postgres; do sleep 2; done

# base vierge créée depuis template0 (évite les extensions ajoutées par l'image)
docker exec gis-restore-test createdb -U postgres -T template0 gis_v2_restore_test

DUMP=$(docker exec gis-restore-test sh -c 'ls /restore/auto_*.dump')
docker exec gis-restore-test pg_restore -l "$DUMP" | wc -l                   # table des matières lisible
time docker exec gis-restore-test pg_restore -U postgres -d gis_v2_restore_test   --no-owner --no-privileges -j 4 "$DUMP" 2> ~/restore-test/pg_restore.err; echo "code=$?"
wc -l ~/restore-test/pg_restore.err                                        # attendu : 0 ou quelques avertissements à lire
```

Comparaisons (sur la base restaurée, puis les mêmes requêtes sur TN **en lecture seule** via `/prod-db`) :

```bash
docker exec -i gis-restore-test psql -U postgres -d gis_v2_restore_test -At <<'SQL'
SELECT 'tables', count(*) FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE';
SELECT 'societes', count(*) FROM societes;
SELECT 'vehicles', count(*) FROM vehicles;
SELECT 'gps_positions max(recorded_at)', max(recorded_at) FROM gps_positions;
SELECT 'taille', pg_size_pretty(pg_database_size('gis_v2_restore_test'));
SQL
```

| Contrôle | Attendu |
|---|---|
| sha256 TN = MANIFEST = PC | identiques |
| `pg_restore` | code 0, `pg_restore.err` vide ou explicable |
| tables `public` | 88 (valeur du 15/09) ou le nombre actuel en prod |
| `societes`, `vehicles` | égaux à la prod, à l'écart près des créations depuis la nuit |
| `max(recorded_at)` | juste avant l'heure du dump (≈ 02:30 UTC du jour du point) |
| taille | du même ordre que la prod (28 Go le 15/09), un peu moins (pas de fragmentation) |

Noter le résultat (date, point restauré, durée du `pg_restore`, contrôles) dans `k8s/DEPLOYMENT-LOG.md` ou équivalent.

### 7.5 Nettoyage (le jour même, sur le PC)

Ce sont des **copies de test** des données clients : rien ne doit rester sur le PC.

```bash
docker rm -f -v gis-restore-test          # conteneur + son volume anonyme (la base de test)
docker volume ls -f dangling=true         # rien ne doit rester de ce test
rm -rf ~/restore-test
ssh belive-tn 'ls /var/tmp/restore-test /var/tmp/restore-out 2>/dev/null; df -h /'   # déjà vidés en 7.2 / 7.3
```

---

## 8. Surveillance

- **E-mails Acronis** (console → profil utilisateur / paramètres de notification, et **Alertes**) : activer au minimum
  les notifications de **sauvegarde échouée** et **avec avertissements**, et l'alerte « aucune sauvegarde réussie
  depuis N jours » (N = 2). Destinataire : une adresse lue tous les jours.
- **Chaque semaine** : console → dernier point OK et **utilisation du quota** ; sur TN
  `sudo tail -n 50 /var/log/calypso-pre-backup.log` (lignes `ATTENTION`) et `kubectl get cronjob,jobs -n gisv2`.
- **Mises à jour de l'agent** : automatiques désactivées (§3.4). Vérifier chaque mois la version proposée (notes de
  version de l'agent) et mettre à jour à la main en fenêtre de maintenance, puis `dmesg -T | tail` et `kubectl get pods -n gisv2`.
- **Après un redémarrage de TN** (noyau neuf via `unattended-upgrades`) : `dkms status` et `systemctl status acronis_mms`
  (santé de l'agent ; `snapapi` n'est pas nécessaire au plan).
- **Chaque trimestre** : test de restauration complet (§7).
- **Espace disque** : le lien dur garde en vie un dump supprimé ailleurs (rotation du CronJob, écran `/admin/database`, ou
  plus d'exécution du script depuis 3 jours) : jusqu'à 1,85 Go restent occupés. Si le disque manque :
  `sudo ls /var/backups/calypso-db-latest/` puis, **seulement si ce dump n'est plus dans `db-backups`**,
  `sudo rm -rf /var/backups/calypso-db-latest` (libère le dump retenu ; le script le recrée à la sauvegarde suivante).
- **Messages du script à connaître** :
  - « aucun auto_*.dump depuis le dernier passage du CronJob » ou « dump trop ancien » → le Job `db-backup` de la nuit a
    échoué : `kubectl get jobs -n gisv2`, `kubectl logs job/<nom> -n gisv2` ;
  - « CronJob db-backup suspendu » → `spec.suspend` à `true` : plus aucun dump nocturne ;
  - « le volume … a changé de chemin » → un PVC a été recréé : mettre à jour les constantes du script **et** les chemins du plan ;
  - « dump toujours en cours après 20 min » → dump anormalement long (disque, charge, image en téléchargement) ou horaire à décaler ;
  - « option --link-latest-dump absente » → l'argument manque dans la commande pré-sauvegarde du plan ;
  - « kubectl ne joint pas l'API K3s » / « api-resources vide » → `systemctl status k3s` ;
  - code 124 dans Acronis → garde-fou de 40 min atteint : lire le journal du script.

---

## 9. Estimation du quota

Hypothèses : dump de 1,85 Go le 15/09 (+25 Mo/jour environ sur les trois derniers jours, mais le dump de juillet était
plus gros : il y a eu des purges) ; fichiers zstd **incompressibles** ; pas de déduplication supposée côté stockage cloud
(à confirmer avec Topnet) ; uploads 14 Mo et configuration < 1 Mo négligeables. **Quota acheté : 100 Go** (Topnet, 15/09).

Avec « 7 quotidiennes / 4 hebdomadaires / 6 mensuelles », on garde à terme environ **15 points de restauration
distincts** : 7 quotidiens + ~3 hebdomadaires plus anciens que 7 jours + ~5 mensuels plus anciens que 4 semaines.
Un seul dump par point (lien dur) :

| Étape | Volume |
|---|---|
| Premier envoi | ~2 Go |
| Envoi quotidien | ~2 Go |
| Après 1 semaine | ~14 Go |
| Après 1 mois | ~20-22 Go |
| Régime établi (6 mois) | **~30 Go** |
| Si le dump atteint 2,5 Go | ~38 Go |

Pour mémoire, sauvegarder tout `db-backups` coûterait 60 à 90 Go, sans possibilité d'exclure quoi que ce soit sur XFS.
Avec 100 Go, le régime établi (~30 Go) laisse de la marge jusqu'à un dump d'environ 6 Go ; **alerte de quota à régler
dans la console à 80 %** (80 Go). Relever l'utilisation réelle après deux semaines et ajuster.

---

## 10. Désinstallation

1. Console : retirer le plan de la machine. **Décider explicitement** du sort des sauvegardes déjà dans le cloud :
   la désinstallation ne les supprime pas et elles **restent facturées** ; les supprimer (onglet *Stockage de
   sauvegarde*) est définitif. Puis supprimer l'appareil de la liste s'il n'a pas disparu seul.
2. Sur TN, agent (root) :

   ```bash
   sudo /usr/lib/Acronis/BackupAndRecovery/uninstall/uninstall -a              # retrait complet (journaux, tâches, configuration)
   sudo /usr/lib/Acronis/BackupAndRecovery/uninstall/uninstall -a --no-purge   # variante : garde l'ID pour réinstaller plus tard
   # ./CyberProtect_AgentForLinux_x86_64.bin -a -u : seulement avec le MÊME .bin, de même version, si le script
   # d'uninstall est inaccessible (le .bin a été supprimé au §3.3 : le retélécharger dans la même version)
   dkms status; lsmod | grep -E 'snapapi|file_protector'; systemctl list-units | grep -i acronis   # rien ne doit rester
   ```

3. Script et exports (l'export contient des copies des secrets : ne pas le laisser traîner) :

   ```bash
   sudo rm -f /usr/local/sbin/calypso-pre-backup.sh /run/calypso-pre-backup.lock
   sudo rm -rf /var/backups/calypso-config
   sudo rm -rf /var/backups/calypso-db-latest     # ne retire qu'un lien dur : le dump reste dans db-backups s'il y est encore
   sudo rm -f /var/log/calypso-pre-backup.log /var/log/calypso-pre-backup.log.1
   ```

4. Facultatif : `sudo apt-get remove gcc make dkms libelf-dev rpm` (sans effet sur la prod).

---

## 11. Dépannage

| Symptôme | Piste |
|---|---|
| Installation : « Failed to build the SnapAPI kernel module » | voie documentée : en-têtes du noyau en cours présents (`dpkg -l "linux-headers-$(uname -r)"`), `gcc --version` = 13.x, `libelf-dev` installé ; relancer l'installation (KB 67148). Sans effet sur ce plan (aucun instantané) ; attendu dans la voie sans module |
| Installation qui échoue tôt | `/var/log/trueimage-setup.log` ; `rpm` installé ? ; 2 Go libres ? |
| Après redémarrage : « SnapAPI kernel module is not loaded » | `dkms status` ; en-têtes du nouveau noyau présents ; KB 62490. Sans effet sur la sauvegarde fichiers sans instantané |
| Sauvegarde en échec, étape pré-commande | `sudo tail -n 50 /var/log/calypso-pre-backup.log` ; relancer à la main `sudo /usr/local/sbin/calypso-pre-backup.sh --dry-run` |
| Connexion refusée à l'enregistrement ou à l'envoi | refaire le test de ports ci-dessous (en `master`, lecture seule) |
| Disque qui remonte vers 85 % | `docker builder prune -af`, vieux tags `gisv2/*`, `sudo du -xsh /var/lib/Acronis /var/log/*`, lien dur retenu (§8) |

Test de ports sortants :

```bash
for hp in eu-cloud.acronis.com:443 eu-cloud.acronis.com:8443 eu-cloud.acronis.com:44445 \
          eu-cloud.acronis.com:7770 eu-cloud.acronis.com:7780 eu-cloud.acronis.com:7800 \
          agents-eu-cloud.acronis.com:443 rs-eu-cloud.acronis.com:8443 bc-baas.acronis.com:443 \
          bc-baas.acronis.com:8443 fes-baas.acronis.com:44445 dl.managed-protection.com:443; do
  if timeout 6 bash -c "</dev/tcp/${hp%:*}/${hp#*:}" 2>/dev/null; then echo "OUVERT $hp"; else echo "FERMÉ  $hp"; fi
done
```

Tous ouverts le 15/09. **Port 40440 — précision de l'assistant Acronis (15/09)** : il ne sert qu'à *accéder aux
sauvegardes supprimées* d'un **stockage immuable**, et il s'ouvre **côté stockage** (Backup Gateway d'Acronis Cyber
Infrastructure, automatique à partir de la version 4.7.1). Rien à faire sur TN ; seulement si la première sauvegarde
échoue sur l'accès au stockage, tester `hôte:44445` et `hôte:40440` comme ci-dessus.

**Hôte de stockage** : avec un stockage hébergé par le partenaire (Topnet, en Tunisie), le plan pointe simplement sur
« Stockage cloud » : aucun hôte à saisir. L'emplacement réel apparaît dans la console (**Backup storage → Backups →
Locations**, vide tant qu'aucune sauvegarde n'a tourné) après la première sauvegarde ; c'est là qu'on lira l'hôte à
tester si besoin.

**Stockage immuable** (à demander à Topnet) : option par tenant qui garde N jours (14 par défaut) toute sauvegarde
supprimée, y compris par un attaquant ou une erreur de rétention — protection utile contre les rançongiciels. Si elle est
active en *Compliance mode*, voir §5.2 (mot de passe de chiffrement réglé sur la machine).

---

## 12. Doc et sources

**Commande d'installation (§3.2).** Doc « Unattended installation or uninstallation parameters » (Linux) :
`-a|--auto` (silencieux), `-i|--id=BackupAndRecoveryAgent` (agent Linux seul), `--token=<jeton>` avec
`-C|--rain=<URL exacte du datacenter>` (incompatible avec `--login`/`--password`), `--options-file=<chemin>` (recommandé
pour ne pas exposer de données sensibles dans `ps`), `--skip-registration`, `--language`, `--product-info`, `--help`.
Enregistrement séparé : `RegisterAgent -o register -t cloud -a <URL> --token <jeton>` (doc « Registering workloads with a
registration token, CLI »). `--reg-token` / `--reg-address` sont des options de l'installeur **Windows** ; `--reg-server`
n'existe pas ; `--reg-transport` existe sur Linux mais n'est pas nécessaire. **La sortie de `--help` du fichier téléchargé
fait foi** (notamment pour le format de `--options-file` et `--disable-file-protector`, vu dans le `--help` de 2023).

**Désinstallation (§10).** `uninstall -a` (retrait complet), `uninstall -a --no-purge` (garde l'ID) ; `--purge` est
indiqué « obsolète, équivalent à `--uninstall` » dans le `--help`.

**Téléchargement.** Le lien de la console fait référence. Un lien public non documenté répond aussi (302 vers
`…/download/u/baas/4.0/26.8.42957/CyberProtect_AgentForLinux_x86_64.bin` le 15/09) :
`https://eu-cloud.acronis.com/bc/api/ams/links/agents/redirect?language=multi&system=linux&architecture=64&productType=enterprise`
— il peut changer et ne permet pas de choisir « Previous stable ».

Liens :

- Paramètres d'installation sans interface (Linux) : <https://www.acronis.com/en/support/documentation/CyberProtectionService/unattended-installation-or-uninstallation-parameters-linux.html>
- Installation / désinstallation sans interface (Linux) : <https://www.acronis.com/en/support/documentation/CyberProtectionService/unattended-installation-or-uninstallation-in-linux.html>
- Enregistrement par jeton en ligne de commande : <https://www.acronis.com/en/support/documentation/CyberProtectionService/registering-workloads-registration-token-cli.html>
- Génération du jeton : <https://www.acronis.com/en/support/documentation/CyberProtectionService/generating-registration-token.html>
- Paquets Linux requis (dont `rpm`) : <https://www.acronis.com/en/support/documentation/CyberProtectionService/linux-packages.html>
- Configuration requise (2 Go) : <https://www.acronis.com/en/support/documentation/CyberProtectionService/system-requirements-agents.html>
- Notes de version de l'agent (26.8.42957, 26.8.43120) : <https://dl.acronis.com/u/baas/rn/agent/en-US/AcronisCyberProtectionAgent_relnotes.htm>
- Mises à jour automatiques des agents : <https://www.acronis.com/en/support/documentation/CyberProtectionService/updating-agents-automatically.html>
- Filtres de fichiers (non disponibles sur XFS) : <https://www.acronis.com/en/support/documentation/CyberProtectionService/file-filter-values.html>
- Instantané de sauvegarde fichiers : <https://www.acronis.com/en/support/documentation/CyberProtectionService/file-level-backup-snapshot.html>
- Commande pré-sauvegarde : <https://www.acronis.com/en/support/documentation/CyberProtectionService/pre-backup-command.html>
- Planification (répartition des heures de début) : <https://www.acronis.com/en/support/documentation/CyberProtectionService/scheduling.html>
- Modèles de sauvegarde : <https://www.acronis.com/en/support/documentation/CyberProtectionService/backup-schemes.html>
- Rétention : <https://www.acronis.com/en/support/documentation/CyberProtectionService/configuring-retention-rules-protection-plan.html>
- Chiffrement : <https://www.acronis.com/en/support/documentation/CyberProtectionService/encryption.html>
- Gestion des erreurs : <https://www.acronis.com/en/support/documentation/CyberProtectionService/error-handling.html>
- Ports et noms d'hôtes (KB 47189) : <https://care.acronis.com/s/article/47189-Acronis-Cyber-Protect-Cloud-access-ports-and-hostnames?language=en_US>
- SnapAPI non chargé / dkms (KB 62490) : <https://kb.acronis.com/content/62490>
- Échec de compilation SnapAPI (KB 67148) : <https://kb.acronis.com/content/67148>
- Récupérer des fichiers depuis le stockage cloud, Web Restore (KB 71125) : <https://www.acronis.com/en/support/documentation/CyberProtectionService/downloading-files-from-cloud-storage.html>, <https://kb.acronis.com/content/71125>
- Désinstallation des agents : <https://www.acronis.com/en/support/documentation/CyberProtectionService/uninstalling-agents.html>
