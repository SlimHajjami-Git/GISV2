# API Calypso : lire les arrêts des véhicules avec un jeton à usage unique

Un jeton d'API permet à un système externe de lire les arrêts des véhicules entre deux dates, sans
transmettre de mot de passe. Le jeton est **à usage unique** : il ne sert qu'**une seule fois**,
expire au bout de **15 minutes**, et ne donne accès **qu'à la lecture des arrêts**.

Pour un logiciel qui lit les arrêts **tout seul** (tâche planifiée, crontab), créer plutôt une
**clé d'automatisation** une seule fois : le logiciel l'échange lui-même contre un jeton avant chaque
appel. Voir la section 4.

## 1. Générer un jeton (dans Calypso)

1. Ouvrir **Profil**, puis la section **Accès API**.
2. Ressaisir son **e-mail** et son **mot de passe** (ceux du compte connecté), puis cliquer sur
   **Générer un jeton**.
3. Copier le jeton (`cly_…`) : il n'est affiché **qu'une seule fois**. Calypso n'en conserve que
   l'empreinte. La page propose aussi un exemple d'appel prêt à copier.

Le jeton reprend les **droits et les véhicules du compte qui l'a généré**, ainsi que l'abonnement de la
société. Il faut donc que ce compte ait accès aux rapports d'arrêts. Un compte chauffeur ou
administrateur système ne peut pas en générer. Au plus 5 jetons peuvent attendre en même temps.

La section liste aussi les derniers jetons (en attente, utilisé, expiré), pour voir si l'un a servi.

## 2. Lire les arrêts

Envoyer le jeton dans l'en-tête `X-Api-Token` (ou `Authorization: Bearer cly_…`).

**Arrêts de plusieurs véhicules** (tous ceux du compte si `vehicleIds` est omis), regroupés par jour :

```bash
curl "https://belive-calypso.com/api/reports/stops?startDate=2026-10-01&endDate=2026-10-28" \
  -H "X-Api-Token: cly_xxxxxxxx"
```

**Un seul véhicule :**

```bash
curl "https://belive-calypso.com/api/reports/stops/123?startDate=2026-10-01&endDate=2026-10-28&minStopDurationSeconds=60" \
  -H "X-Api-Token: cly_xxxxxxxx"
```

**Liste à plat et paginée**, avec chauffeur, zone et type d'arrêt (droit « Suivi » requis) :

```
GET /api/vehiclestops?vehicleId=123&startDate=2026-10-01T00:00:00Z&endDate=2026-10-28T23:59:59Z&page=1&pageSize=500
```

| Paramètre | Sens |
|---|---|
| `startDate`, `endDate` | **Obligatoires.** Format `AAAA-MM-JJ` (le rapport raisonne au jour près) ou date-heure UTC. |
| `minStopDurationSeconds` | Durée minimale d'un arrêt, 60 par défaut (rapport). |
| `vehicleId`, `vehicleIds` | Véhicule(s) voulus ; sans eux, tous les véhicules du compte. |
| `page`, `pageSize` | Pagination de la liste à plat, `pageSize` ≤ 1000. |

Exemple de réponse du rapport (extrait) :

```json
{
  "vehicleId": 123,
  "vehicleName": "Camion 12",
  "plate": "123 TUN 4567",
  "days": [
    {
      "date": "2026-10-01T00:00:00Z",
      "stopCount": 2,
      "totalStopSeconds": 5400,
      "stops": [
        { "startTime": "2026-10-01T07:12:00Z", "endTime": "2026-10-01T07:57:00Z",
          "durationSeconds": 2700, "latitude": 36.8, "longitude": 10.18, "address": "…" }
      ]
    }
  ],
  "summary": { "totalStops": 2, "totalStopSeconds": 5400, "avgStopSeconds": 2700, "maxStopSeconds": 2700 }
}
```

Toutes les heures sont en UTC.

## 3. Règles du jeton

- **Un seul appel.** Le jeton est consommé dès que l'appel est accepté. Un second appel répond 401 : il
  faut en générer un nouveau. Un appel refusé par le verrou (mauvaise route, dates absentes, période
  trop longue) **ne consomme pas** le jeton.
- **Lecture seule des arrêts.** Tout autre appel, y compris toute écriture et la génération d'autres
  jetons, répond 403.
- **Période de 31 jours au plus.** Pour couvrir plus long, générer un jeton par tranche.
- **15 minutes** de validité, que le jeton ait servi ou non.
- Un compte désactivé ou devenu chauffeur ferme ses jetons en attente.

| Code | Cause |
|---|---|
| 400 | `startDate` ou `endDate` manquant, période de plus de 31 jours, ou `pageSize` supérieur à 1000. |
| 401 | Jeton absent, inconnu, déjà utilisé ou expiré, ou compte désactivé. |
| 403 | Appel autre qu'une lecture d'arrêts, ou rapport « arrêts » non inclus dans l'abonnement ou non autorisé au compte. |
| 429 | Trop de tentatives de génération depuis ce réseau (5 par minute). |

## 4. Automatiser : la clé d'automatisation (tâche planifiée)

Une tâche planifiée ne peut pas générer un jeton à la main chaque jour. La **clé d'automatisation**
(`clyk_…`) la remplace dans ce rôle, et seulement dans ce rôle : elle **ne lit aucune donnée** par
elle-même. Avant chaque appel, le logiciel l'échange contre un jeton à usage unique, identique à un
jeton généré à la main (mêmes règles qu'en section 3).

### Créer la clé (une seule fois, dans Calypso)

1. **Profil** > **Accès API** > **Clés d'API**.
2. Donner un nom à la clé (par exemple « mon application »), ressaisir son **e-mail** et son
   **mot de passe**, puis **Créer une clé**.
3. Copier la clé : elle n'est affichée **qu'une seule fois**. La conserver comme un mot de passe. La
   page propose aussi un exemple d'appel prêt à copier.

Seul l'administrateur de la société peut créer une clé, si Belive a activé l'accès API de la
société. Les jetons obtenus reprennent **ses droits et ses véhicules**.

### L'échanger puis lire les arrêts (dans le logiciel)

```bash
# 1. Échanger la clé contre un jeton (sans session, en-tête X-Api-Key)
curl -X POST "https://belive-calypso.com/api/api-tokens/echange" -H "X-Api-Key: clyk_xxxxxxxx"
# → {"id":…,"token":"cly_…","tokenPrefix":"…","expiresAt":"…","expiresInSeconds":900}

# 2. Lire les arrêts avec le jeton obtenu (un seul appel)
curl "https://belive-calypso.com/api/reports/stops?startDate=2026-11-17&endDate=2026-11-17" \
  -H "X-Api-Token: cly_…"
```

Script complet pour un serveur Linux (arrêts de la veille de tous les véhicules) :

```bash
#!/bin/sh
CLE="clyk_xxxxxxxx"
URL="https://belive-calypso.com"
JOUR=$(date -d yesterday +%F)

JETON=$(curl -fsS -X POST "$URL/api/api-tokens/echange" -H "X-Api-Key: $CLE" \
  | sed -E 's/.*"token":"([^"]+)".*/\1/')

curl -fsS "$URL/api/reports/stops?startDate=$JOUR&endDate=$JOUR" \
  -H "X-Api-Token: $JETON" -o "arrets-$JOUR.json"
```

Ligne de crontab (tous les jours à 6 h 30) : `30 6 * * * /chemin/vers/arrets-calypso.sh`

Un jeton ne sert qu'à **un** appel, donc à une page de résultats : pour paginer ou lire véhicule par
véhicule, échanger la clé une fois par appel.

### Règles de la clé

- **Valable 12 mois**, puis en recréer une. **Révocable** à tout moment dans le profil, par tout
  administrateur de la société.
- **2 clés actives au plus** par société, pour pouvoir en changer sans coupure.
- **100 échanges par 24 heures** glissantes et par clé ; au plus 5 jetons obtenus et pas encore
  utilisés à la fois.
- La clé cesse de fonctionner d'elle-même si le compte qui l'a créée est désactivé ou n'est plus
  administrateur, ou si Belive coupe l'accès API de la société.
- La clé ne fonctionne **que** sur la route d'échange : présentée à toute autre route, elle répond 401.

| Code (échange) | Cause, donnée dans le champ `message` de la réponse |
|---|---|
| 401 | Clé absente, inconnue, révoquée ou expirée. |
| 403 | Le compte qui a créé la clé ne peut plus utiliser l'API, ou l'accès API de la société est coupé. |
| 429 | 100 échanges atteints sur 24 heures, 5 jetons en attente, ou plus de 30 échanges par minute depuis ce réseau. |

## 5. Pour l'exploitation

- Migration `064_jetons_api.sql` à jouer sur la base **avant** le pod API (table `api_tokens`).
- Migration `069_cles_api_automatisation.sql` (table `api_keys`, colonne `api_tokens.api_key_id`) à
  jouer **avant** le pod API qui la mappe.
- Chaque génération est inscrite dans le journal d'audit (`api_token_generated`), sans le jeton ;
  chaque création et révocation de clé aussi (`api_key_created`, `api_key_revoked`), sans la clé.
- Chaque échange laisse une ligne dans `api_tokens` (`api_key_id`, `created_ip`, `created_at`) : c'est
  le journal des échanges et le compteur du quota.
- Ni un jeton ni une clé ne sont conservés en clair : seule leur empreinte SHA-256 est en base.
