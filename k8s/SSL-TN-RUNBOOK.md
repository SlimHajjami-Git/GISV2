# SSL sur TN (belive-calypso.com) — Runbook

Préparé le 2026-08-05. Objectif : servir l'application en HTTPS sur
`https://www.belive-calypso.com` (+ apex), avec certificat Let's Encrypt
renouvelé automatiquement.

## État des lieux (vérifié le 2026-08-05)

| Élément | État |
|---|---|
| DNS `www.belive-calypso.com` / `belive-calypso.com` | ✅ pointent sur 41.231.5.146 (Topnet ns24/ns54) |
| Traefik (K3s) | ✅ écoute 80 **et** 443 sur l'hôte |
| cert-manager | ✅ installé (155 j), ClusterIssuer `letsencrypt-prod` READY |
| Certificats | ❌ 3 certificats `False` depuis 155 j : challenges bloqués sur des domaines FICTIFS `*.calypso.com` |
| Frontend TN | ✅ `apiUrl: '/api'` relatif — HTTPS transparent, aucun rebuild |
| APK mobile v1.1.1 | ⚠ cible `http://41.231.5.146/api` EN DUR — l'accès HTTP par IP doit rester SANS redirection |
| `api.` / `mobile.belive-calypso.com` | ❌ n'existent pas en DNS → leurs ingress sont supprimés (c'était la cause des challenges bloqués) |

## Déroulé (sur TN : `ssh belive-tn`)

### 1. Récupérer les manifests
```bash
cd ~/GISV2 && git pull
# (un commit local aa7608d peut gêner le pull : git stash si besoin)
```

### 2. Nettoyer les restes des 155 jours (AVANT l'apply)
```bash
# Ingress des sous-domaines fictifs/inexistants
kubectl delete ingress api-ingress mobile-ingress -n gisv2 --ignore-not-found

# Certificats jamais émis (les challenges/solvers pending partent avec eux)
kubectl delete certificate gisv2-api-tls gisv2-mobile-tls gisv2-frontend-tls -n gisv2 --ignore-not-found
kubectl delete secret gisv2-api-tls gisv2-mobile-tls gisv2-frontend-tls -n gisv2 --ignore-not-found

# Vérifier qu'il ne reste ni challenge ni solver
kubectl get challenges,orders -n gisv2
kubectl get ingress,svc -n gisv2 | grep cm-acme    # doit être vide
```

### 3. Appliquer la nouvelle configuration
```bash
kubectl apply -f k8s/04-ingress.yaml
```
Ce que ça fait : met à jour l'issuer (e-mail réel `hajjami.selim@gmail.com`),
recrée `frontend-ingress` avec les 2 vrais hôtes (cert-manager régénère un
certificat SAN), pose la redirection HTTP→HTTPS **sur les hôtes nommés
uniquement**, et garde `ip-ingress` en HTTP simple (APK mobile).

### 4. Suivre l'émission du certificat (1 à 3 minutes)
```bash
kubectl get certificate -n gisv2 -w
# attendu : gisv2-frontend-tls  READY=True
# si ça traîne :
kubectl describe challenge -n gisv2 | tail -30
```

### 5. Vérifier
```bash
curl -I https://www.belive-calypso.com/            # 200, cert Let's Encrypt
curl -I https://belive-calypso.com/                # 200 (apex dans le SAN)
curl -I http://www.belive-calypso.com/             # 308 → https
curl -I http://41.231.5.146/                       # 200 en HTTP, PAS de redirection
curl -s https://www.belive-calypso.com/api/auth/login -X POST -H 'Content-Type: application/json' -d '{}' -o /dev/null -w '%{http_code}\n'   # 400 = API OK via TLS
```
Puis dans un navigateur : connexion + écran Monitoring (le temps réel passe
en `wss://` automatiquement — Traefik gère l'upgrade WebSocket).

### 6. Après bascule — liens des e-mails
Les e-mails (reset mot de passe…) construisent leurs liens sur
`App__PublicBaseUrl`, aujourd'hui absent sur TN → retombe sur un défaut mort
(`gpa.belive.tn`). Ajouter au deployment `gis-api` (dans le
`02-applications.yaml` LOCAL de TN, pour survivre aux `update.sh config`) :
```yaml
- name: App__PublicBaseUrl
  value: "https://www.belive-calypso.com"
```
puis `kubectl apply` / `bash k8s/update.sh api`.

## Suites (non bloquantes)
- **APK mobile** : prochaine release → `apiUrl: 'https://www.belive-calypso.com/api'`
  (et signalrUrl en https). Tant que la flotte n'est pas à jour, ne JAMAIS
  rediriger l'accès par IP.
- **HSTS** : à activer plus tard (middleware Traefik `customResponseHeaders`),
  une fois la flotte mobile migrée et le HTTPS rodé quelques semaines.
- **DZ** : ce fichier ne doit PAS y être appliqué (accès par IP, pas de domaine).

## Rollback
Le HTTP par IP n'est jamais coupé. Si le HTTPS pose problème :
```bash
kubectl delete ingress http-redirect -n gisv2     # rend le HTTP direct aux domaines
```
L'application reste servie comme avant sur http://.
