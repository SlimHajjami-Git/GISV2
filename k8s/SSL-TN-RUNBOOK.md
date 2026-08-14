# SSL sur TN (belive-calypso.com) — Runbook

**APPLIQUÉ le 2026-08-14.** L'application est servie en HTTPS sur
`https://www.belive-calypso.com` (+ apex) avec un **certificat commercial
GlobalSign AlphaSSL**.

> Ce runbook décrivait initialement une émission Let's Encrypt (préparée le
> 2026-08-05, jamais aboutie : 155 jours de challenges bloqués sur des domaines
> fictifs). Un certificat a été **acheté** entre-temps. La piste ACME est
> abandonnée pour ces hôtes — voir « Renouvellement ».

## Le certificat

| | |
|---|---|
| Émetteur | GlobalSign GCC R46 AlphaSSL CA 2025 |
| Couvre (SAN) | `www.belive-calypso.com`, `belive-calypso.com` |
| Validité | 2026-08-10 → **2027-02-25** ⚠ |
| Secret K8s | `belive-calypso-tls` (ns `gisv2`, type `kubernetes.io/tls`) |
| Sources sur TN | `~/ssl/` — `belive-calypso-v2.key` (**la** clé qui correspond), `belive-calypso-2026.crt`, `intermediate.pem`, `fullchain.pem` |

⚠ Deux paires clé/CSR coexistent dans `~/ssl`. Seule **`belive-calypso-v2.key`**
correspond au certificat émis ; la paire sans `-v2` est une CSR abandonnée. En
cas de doute, comparer les empreintes de clé publique — elles doivent être
identiques :

```bash
openssl x509 -in belive-calypso-2026.crt -noout -pubkey | openssl pkey -pubin -outform DER | openssl dgst -sha256
openssl pkey -in belive-calypso-v2.key -pubout -outform DER | openssl dgst -sha256
```

Le secret n'est **pas** dans git : il contient une clé privée.

## État des lieux (vérifié le 2026-08-14)

| Élément | État |
|---|---|
| DNS `www.belive-calypso.com` / apex | ✅ 41.231.5.146 |
| Traefik (K3s) | ✅ écoute 80 **et** 443 |
| HTTPS | ✅ chaîne complète, `Verify return code: 0 (ok)` |
| HTTP sur hôtes nommés | ✅ 301 → https |
| **HTTP par IP** | ✅ **200, AUCUNE redirection** — l'APK v1.1.1 cible `http://41.231.5.146/api` en dur |
| HSTS | ✅ absent (obligatoire tant que la flotte mobile est en HTTP) |
| Frontend | ✅ `apiUrl: '/api'` relatif — HTTPS transparent, aucun rebuild |
| Liens e-mail | ✅ `App__PublicBaseUrl=https://www.belive-calypso.com` |

## Ce qui a été fait

```bash
# 1. Chaîne = feuille PUIS intermédiaire, dans cet ordre exactement
cd ~/ssl
curl -o gsgccr46alphasslca2025.der http://secure.globalsign.com/cacert/gsgccr46alphasslca2025.crt
openssl x509 -inform DER -in gsgccr46alphasslca2025.der -out intermediate.pem
cat belive-calypso-2026.crt intermediate.pem > fullchain.pem

# 2. Vérifier AVANT de poser quoi que ce soit
openssl verify -untrusted intermediate.pem belive-calypso-2026.crt   # doit dire OK

# 3. Secret (idempotent — même commande pour créer et remplacer)
kubectl create secret tls belive-calypso-tls -n gisv2 \
  --cert=fullchain.pem --key=belive-calypso-v2.key \
  --dry-run=client -o yaml | kubectl apply -f -

# 4. Ingress
kubectl apply -f k8s/04-ingress.yaml

# 5. Liens e-mail + CORS
kubectl set env deployment/gis-api -n gisv2 \
  App__PublicBaseUrl=https://www.belive-calypso.com \
  Cors__AllowedOrigins__1=https://www.belive-calypso.com \
  Cors__AllowedOrigins__2=https://belive-calypso.com
```

⚠ **Aucune annotation `cert-manager.io/cluster-issuer` sur `frontend-ingress`** :
elle ferait écraser le certificat payant par une tentative ACME. Le secret porte
volontairement un nom distinct (`belive-calypso-tls`) de ceux que visent les
objets `Certificate` hérités.

## Vérifications

```bash
curl -I https://www.belive-calypso.com/                     # 200
curl -I https://belive-calypso.com/                         # 200 (apex dans le SAN)
curl -I http://www.belive-calypso.com/                      # 301 → https
curl -I http://41.231.5.146/                                # 200, PAS de redirection
echo | openssl s_client -connect www.belive-calypso.com:443 \
  -servername www.belive-calypso.com 2>/dev/null | grep "Verify return code"   # 0 (ok)
curl -s -o /dev/null -w '%{http_code}\n' -X POST \
  https://www.belive-calypso.com/api/auth/login \
  -H 'Content-Type: application/json' -d '{}'               # 400 = API OK via TLS
```

Puis dans un navigateur : connexion + écran Monitoring (le temps réel passe en
`wss://` automatiquement — Traefik gère l'upgrade WebSocket).

## Renouvellement — AVANT le 2027-02-25

Le certificat n'est **pas** auto-renouvelé. Deux options :

1. **Racheter** chez le même fournisseur. Réutiliser la CSR existante
   (`~/ssl/belive-calypso-v2.csr`) permet de garder la même clé privée : il
   suffit alors de refaire les étapes 1 à 3 ci-dessus avec le nouveau
   certificat, puis `kubectl rollout restart deployment/traefik -n kube-system`
   n'est **pas** nécessaire (Traefik recharge le secret tout seul).
2. **Basculer sur Let's Encrypt** (gratuit, auto-renouvelé). Le blocage
   historique était l'usage de domaines fictifs, aujourd'hui corrigé : les
   vrais hôtes sont en DNS et le port 80 est ouvert, donc un challenge HTTP-01
   aboutirait. Il faudrait recréer un `ClusterIssuer` ACME et remettre
   l'annotation sur `frontend-ingress` — en supprimant d'abord le secret
   statique pour éviter que les deux se disputent le même nom.

Poser un rappel : le certificat expire un **jeudi 25 février 2027**.

## Restes à nettoyer (non bloquant, inertes)

Vestiges des domaines fictifs, sans effet sur le trafic — ils ne servent rien
et ne pointent pas sur le secret utilisé :

```bash
kubectl delete ingress api-ingress mobile-ingress -n gisv2 --ignore-not-found
kubectl delete certificate gisv2-api-tls gisv2-mobile-tls gisv2-frontend-tls -n gisv2 --ignore-not-found
kubectl delete secret gisv2-api-tls gisv2-mobile-tls gisv2-frontend-tls -n gisv2 --ignore-not-found
kubectl get ingress -n gisv2 | grep cm-acme   # les solveurs partent avec les certificats
```

## Suites

- **APK mobile** : prochaine release → `apiUrl: 'https://www.belive-calypso.com/api'`
  (et signalrUrl en https). Tant que la flotte n'est pas à jour, ne JAMAIS
  rediriger l'accès par IP.
- **HSTS** : seulement une fois la flotte mobile migrée. Poser l'en-tête
  interdirait définitivement le HTTP aux navigateurs — irréversible côté client
  pendant la durée du max-age.
- **DZ** : ce fichier ne doit PAS y être appliqué (accès par IP, pas de domaine).

## Rollback

Le HTTP par IP n'est jamais coupé. Si le HTTPS pose problème :

```bash
kubectl delete ingress http-redirect -n gisv2     # rend le HTTP direct aux domaines
```

L'application reste servie comme avant sur `http://`.
