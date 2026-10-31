# API Calypso : lire les arrêts des véhicules avec un jeton à usage unique

Un jeton d'API permet à un système externe de lire les arrêts des véhicules entre deux dates, sans
transmettre de mot de passe. Le jeton est **à usage unique** : il ne sert qu'**une seule fois**,
expire au bout de **15 minutes**, et ne donne accès **qu'à la lecture des arrêts**.

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

## 4. Pour l'exploitation

- Migration `064_jetons_api.sql` à jouer sur la base **avant** le pod API (table `api_tokens`).
- Chaque génération est inscrite dans le journal d'audit (`api_token_generated`), sans le jeton.
- Un jeton n'est jamais conservé en clair : seule son empreinte SHA-256 est en base.
