# Scénarios de Test — Système Gestion Utilisateurs

## Prérequis
- Docker containers running (`docker compose up -d`)
- Compte admin Belive: `admin@belive.tn` / `Admin@2026`
- Au moins 1 société créée avec un abonnement actif
- Au moins 2 véhicules dans la société

---

## A. Tests Login & Réponse Auth

### A1. Login admin → pas de vehicleIds
```
POST /api/auth/login
{ "email": "admin@belive.tn", "password": "Admin@2026" }

✅ Attendu: response.user.assignedVehicleIds = null (admin voit tout)
✅ Attendu: response.user.permissions != null
✅ Attendu: response.user.subscriptionFeatures != null
```

### A2. Login utilisateur normal → vehicleIds retournés
```
1. Créer un user avec 2 véhicules assignés (via POST /api/users)
2. Login avec ce user

✅ Attendu: response.user.assignedVehicleIds = [id1, id2]
✅ Attendu: response.user.isCompanyAdmin = false
```

### A3. Login utilisateur sans véhicules assignés
```
1. Créer un user sans véhicules (assignedVehicleIds = [])
2. Login avec ce user

✅ Attendu: response.user.assignedVehicleIds = [] (tableau vide)
```

---

## B. Tests Création Utilisateurs (Company Admin)

### B1. Créer un user avec véhicules assignés
```
POST /api/users
{
  "firstName": "Test",
  "lastName": "User",
  "email": "test@company.com",
  "password": "Test1234!",
  "roleId": <roleId>,
  "assignedVehicleIds": [1, 2]
}

✅ Attendu: 201 Created
✅ Attendu: response.assignedVehicleIds = [1, 2]
✅ Vérifier en DB: SELECT * FROM user_vehicles WHERE user_id = <newUserId>
   → 2 enregistrements
```

### B2. Créer un user avec véhicule d'une autre société
```
POST /api/users
{
  ...
  "assignedVehicleIds": [999] // véhicule inexistant ou d'une autre société
}

✅ Attendu: 400 Bad Request
✅ Attendu: message = "Un ou plusieurs véhicules sont invalides"
```

### B3. Limite MaxUsers atteinte
```
1. Configurer subscription avec maxUsers = 2
2. Créer 2 users
3. Tenter de créer un 3ème user

✅ Attendu: 400 Bad Request
✅ Attendu: message contient "Limite d'utilisateurs atteinte"
```

### B4. Créer un user avec rôle d'une autre société
```
POST /api/users
{
  ...
  "roleId": <roleIdAutreSociete>
}

✅ Attendu: 400 Bad Request
✅ Attendu: message = "Rôle invalide"
```

### B5. Créer un user avec email existant
```
POST /api/users (même email qu'un user existant)

✅ Attendu: 400 Bad Request
✅ Attendu: message = "Cet email est déjà utilisé"
```

---

## C. Tests Modification Utilisateurs

### C1. Modifier les véhicules assignés
```
PUT /api/users/<id>
{
  "firstName": "Test",
  "lastName": "User",
  "email": "test@company.com",
  "assignedVehicleIds": [3] // remplacer [1,2] par [3]
}

✅ Attendu: 204 No Content
✅ Vérifier en DB: user_vehicles pour ce user → seulement vehicleId=3
✅ Les anciens assignments (1, 2) sont supprimés
```

### C2. Retirer tous les véhicules assignés
```
PUT /api/users/<id>
{
  ...
  "assignedVehicleIds": []
}

✅ Attendu: 204 No Content
✅ Vérifier en DB: aucun user_vehicles pour ce user
```

### C3. Ne pas toucher aux véhicules (null)
```
PUT /api/users/<id>
{
  "firstName": "NewName",
  "lastName": "User",
  "email": "test@company.com"
  // assignedVehicleIds absent ou null
}

✅ Attendu: 204 No Content
✅ Les véhicules assignés restent INCHANGÉS
```

---

## D. Tests Listing Utilisateurs

### D1. GET /api/users → inclut assignedVehicleIds
```
GET /api/users (en tant que company admin)

✅ Attendu: chaque user dans la liste a un champ assignedVehicleIds
✅ Admin: assignedVehicleIds peut être null ou []
✅ User normal: assignedVehicleIds = [ids des véhicules assignés]
```

### D2. GET /api/users/<id> → inclut assignedVehicleIds
```
✅ Attendu: response.assignedVehicleIds = [liste des ids]
```

### D3. GET /api/users/me → inclut assignedVehicleIds
```
(en tant qu'utilisateur normal)

✅ Attendu: response.assignedVehicleIds = [ses véhicules]
```

---

## E. Tests Filtrage Véhicules par Utilisateur

### E1. Admin voit tous les véhicules
```
GET /api/vehicles (en tant que company admin)

✅ Attendu: tous les véhicules de la société
```

### E2. User normal voit seulement ses véhicules assignés
```
1. Créer user avec assignedVehicleIds = [1, 3]
2. Login avec ce user
3. GET /api/vehicles

✅ Attendu: seulement véhicules 1 et 3
✅ Les autres véhicules de la société sont EXCLUS
```

### E3. User sans véhicules → liste vide
```
1. Créer user avec assignedVehicleIds = []
2. Login avec ce user
3. GET /api/vehicles

✅ Attendu: [] (liste vide)
```

### E4. Monitoring (with-positions) respecte le filtre
```
GET /api/vehicles/with-positions (en tant que user normal)

✅ Attendu: seulement les véhicules assignés avec leurs positions GPS
✅ Attendu: même résultats que E2 mais avec les données GPS
```

---

## F. Tests Admin Panel (Calypso)

### F1. Admin crée un user avec véhicules via admin panel
```
POST /api/admin/users
{
  "name": "New User",
  "email": "newuser@client.com",
  "password": "Pass123!",
  "companyId": <companyId>,
  "roleId": <roleId>,
  "assignedVehicleIds": [1, 2]
}

✅ Attendu: 201 Created
✅ Attendu: response.assignedVehicleIds = [1, 2]
✅ Vérifier en DB: user_vehicles créés
```

---

## G. Tests Rôles & Permissions

### G1. Créer un rôle qui dépasse les permissions de l'abonnement
```
POST /api/roles
{
  "name": "Super Role",
  "permissions": { "moduleGeofences": true }  // si abonnement n'inclut pas geofences
}

✅ Attendu: 400/Exception "permissions dépassent les limites de l'abonnement"
```

### G2. Modifier un rôle → même validation
```
PUT /api/roles/<id>
{
  "permissions": { "moduleReports": true }  // si abonnement ne le permet pas
}

✅ Attendu: erreur de validation
```

---

## H. Tests Frontend

### H1. Page Gestion Utilisateurs → modal avec véhicules
```
1. Ouvrir /users
2. Cliquer "Nouvel Utilisateur"

✅ Attendu: section "Véhicules assignés" visible avec checkboxes
✅ Attendu: si rôle admin sélectionné → section masquée
```

### H2. Éditer un utilisateur → véhicules pré-cochés
```
1. Cliquer "Modifier" sur un user avec 2 véhicules assignés

✅ Attendu: les 2 véhicules sont pré-cochés
✅ Décocher un véhicule et sauvegarder → véhicule retiré
```

### H3. Monitoring → user normal ne voit que ses véhicules
```
1. Login en tant qu'utilisateur non-admin
2. Ouvrir la carte /monitoring

✅ Attendu: seulement les véhicules assignés sur la carte
✅ Attendu: sidebar liste seulement les véhicules assignés
```
