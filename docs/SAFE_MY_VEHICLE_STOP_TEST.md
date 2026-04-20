# 🛑 Test safe de l'arrêt distant — Véhicule de test (device id=127624)

Ce document décrit le protocole pour tester le flux d'arrêt distant en **production**
sur un véhicule de test connu, sans risquer d'immobiliser un véhicule client par erreur.

## Véhicule de test (valeurs figées dans le code)

| Champ | Valeur attendue |
|---|---|
| `gps_devices.id` | `127624` |
| `gps_devices.device_uid` (IMEI) | `860141076682872` |
| `gps_devices.company_id` | `1` (Belive) |

Si **un seul** de ces champs ne matche pas, l'endpoint renvoie **400/404** et refuse
de dispatcher la commande. **Aucun autre véhicule** ne peut être arrêté via cet
endpoint, quoi qu'il arrive.

---

## Pré-requis

1. Déployer l'API .NET en prod (nouvelles images `gis-api` + `gps-ingest`).
2. Avoir un **JWT d'admin de la société 1** (ex: `admin@belive.tn`).
3. Le véhicule doit être **allumé** et dans une zone avec réseau GSM.
4. Tu dois être **physiquement à côté du véhicule** pour observer le moteur.

---

## Étape 1 — Preview (dry-run, 100% safe)

Vérifie que tous les garde-fous sont verts **sans** rien déclencher.

```bash
# Remplace $JWT par ton token et $API_URL par l'URL de prod
curl -X GET "$API_URL/api/gps/test/my-vehicle-stop/preview" \
  -H "Authorization: Bearer $JWT" \
  -H "Accept: application/json"
```

Réponse attendue :
```json
{
  "expected": {
    "deviceId": 127624,
    "imei": "860141076682872",
    "companyId": 1
  },
  "safetyChecks": {
    "callerIsAdminOfCompany1": true,
    "deviceExists": true,
    "deviceImeiMatches": true,
    "deviceHasVehicle": true,
    "deviceInCompany1": true,
    "notAlreadyStopped": true
  },
  "allGreen": true,
  "device": {
    "id": 127624,
    "imei": "860141076682872",
    "label": "…",
    "plate": "…",
    "brand": "…",
    "model": "…",
    "immobilizationActive": false,
    "lastCommunication": "2026-04-20T12:34:56Z"
  },
  "lastPosition": {
    "recordedAt": "2026-04-20T12:34:56Z",
    "latitude": 36.866493,
    "longitude": 10.256766,
    "speedKph": 0
  },
  "hint": "✅ All checks green — you can call POST /api/gps/test/my-vehicle-stop?stopDurationSeconds=10"
}
```

⚠️ **Si `allGreen` est `false`, corrige ce qui est rouge avant d'aller plus loin.**

Points à vérifier :
- `lastCommunication` doit être récent (< 5 min) — sinon le device est hors-ligne
  et la commande sera juste mise en pending sans effet immédiat.
- `notAlreadyStopped = true` — sinon il faut d'abord libérer via
  `POST /api/gps/devices/127624/go`.

---

## Étape 2 — Test réel avec auto-release

```bash
# Arrêt de 10 secondes (recommandé pour le premier test)
curl -X POST "$API_URL/api/gps/test/my-vehicle-stop?stopDurationSeconds=10" \
  -H "Authorization: Bearer $JWT" \
  -H "Accept: application/json"
```

Réponse attendue (immédiate, < 1s) :
```json
{
  "success": true,
  "testId": 42,
  "device": {
    "id": 127624, "imei": "860141076682872",
    "plate": "…", "brand": "…", "model": "…", "label": "…"
  },
  "stopCommand": {
    "commandId": 42,
    "text": "AJ+STOP#...\n",
    "pushedInstantly": true,
    "pushStatus": "Pushed",
    "pushMessage": "...",
    "note": "Command written to live TCP socket — engine should cut within 1–2 seconds"
  },
  "autoRelease": {
    "scheduledAt": "2026-04-20T12:34:56Z",
    "inSeconds": 10
  },
  "message": "🛑 Test vehicle stopped. Auto-release scheduled in 10s. Watch the engine — it should restart automatically."
}
```

**À observer sur le véhicule** :
1. **Dans les 1–2 secondes** après l'appel : le moteur se coupe.
2. **Après 10 secondes** (durée configurée) : le moteur redémarre automatiquement.

**Durées recommandées** :
- `stopDurationSeconds=5` → smoke test rapide
- `stopDurationSeconds=10` → confirmation visuelle
- `stopDurationSeconds=30` → observation longue (limite max = 60s)

---

## Étape 3 — Que faire si le moteur ne redémarre pas automatiquement ?

L'auto-release est protégée par un `try/catch` qui log en `CRITICAL` s'il échoue.
Vérifie les logs de l'API :

```bash
docker logs -f gis-api 2>&1 | grep "\[SAFE-TEST\]"
```

Tu devrais voir au moins :
```
[SAFE-TEST] Vehicle STOP test STARTED by user 1 (admin@belive.tn), auto-release in 10s, deviceId=127624, deviceImei=860141076682872
[SAFE-TEST] STOP dispatched: commandId=42, pushStatus=Pushed
[SAFE-TEST] AUTO-RELEASE GO dispatched: commandId=43, pushStatus=Pushed
```

**Si le 3e log est absent** ou si tu vois `AUTO-RELEASE FAILED`, envoie un GO
manuel immédiatement :
```bash
curl -X POST "$API_URL/api/gps/devices/127624/go" \
  -H "Authorization: Bearer $JWT" \
  -H "Content-Type: application/json" \
  -d '{"password":"TON_MOT_DE_PASSE"}'
```

---

## Les 5 garde-fous du endpoint

Code de référence : `services/GisAPI/Controllers/GpsController.cs` (méthode `SafeTestMyVehicleStop`).

| # | Check | Si faux → |
|---|---|---|
| 1 | `5 ≤ stopDurationSeconds ≤ 60` | 400 |
| 2 | Caller est admin de company 1 | 403 |
| 3 | `device.id == 127624` **ET** `device.device_uid == "860141076682872"` **ET** `device.company_id == 1` | 400/404 |
| 4 | `device.immobilization_active == false` (pas déjà arrêté) | 409 Conflict |
| 5 | Auto-release fire-and-forget via `IServiceScopeFactory` | `CRITICAL` log si fail |

---

## Ce que cet endpoint **ne fait pas**

- ❌ **Ne peut pas arrêter un autre véhicule** — tout est hard-codé sur
  device id=127624 + IMEI=860141076682872 + company=1. La double vérif id+IMEI
  rend quasi-impossible qu'un autre véhicule matche (il faudrait que quelqu'un
  change manuellement l'IMEI du device 127624 en DB, ce qui n'arrive pas).
- ❌ **Ne bypass pas** les logs/journaux standards — la commande apparaît dans
  `device_commands` avec `source='safe-test'` pour auditabilité.
- ❌ **Ne désactive pas** l'endpoint normal `/api/gps/devices/127624/stop` — les
  admins peuvent toujours arrêter le véhicule via le flux standard.

---

## Supprimer l'endpoint après la phase de test

Une fois le déploiement validé en prod sur plusieurs véhicules, supprime les 2
endpoints `test/my-vehicle-stop` et `test/my-vehicle-stop/preview` dans
`GpsController.cs` :

```bash
# Git grep pour trouver le bloc à supprimer
git grep -n "SAFE PRODUCTION TEST" services/GisAPI/Controllers/GpsController.cs
```

Le bloc est délimité par un commentaire banner
`// ==================== SAFE PRODUCTION TEST — ONE TEST VEHICLE ONLY ====================`.
