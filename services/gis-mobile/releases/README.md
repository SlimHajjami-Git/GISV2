# Calypso Mobile — Releases

App Bundles (AAB) signés prêts à être uploadés sur Google Play Console.

## Fichiers

| Fichier | Version | versionCode | Date | Signature |
|---|---|---|---|---|
| `calypso-v1.2.0-release.aab` | 1.2.0 | 14 | 2026-09-22 | `calypso` keystore (SHA-256 `C8:B1:B3:E7:…:3A:73:E0`) |
| `calypso-v1.1.0-release.aab` | 1.1.0 | 12 | 2026-07-23 | `calypso` keystore (SHA-1: `8D:93:06:90:97:39:7E:0D:37:D8:DB:77:C1:A6:89:E7:F3:FB:02:8D`) |
| `calypso-v1.0.9-release.aab` | 1.0.9 | 11 | 2026-04-22 | `calypso` keystore (SHA-1: `8D:93:06:90:97:39:7E:0D:37:D8:DB:77:C1:A6:89:E7:F3:FB:02:8D`) |

### Nouveautés v1.2.0 (versionCode 14)

- **Mode chauffeur** : un compte marqué « Chauffeur » dans Utilisateurs se connecte à
  l'application et n'y voit que ses tournées (« Mes tournées », fiche avec carte,
  « Je pars », « Je suis arrivé », « Je repars », navigation Google Maps/Waze).
- Suivi par le téléphone pendant une tournée (service au premier plan « Tournée en
  cours »), relais du boîtier GPS ; gestes et positions gardés hors ligne.
- Adresse du serveur en HTTPS sur le nom de domaine.

### AVANT d'envoyer la 1.2.0 sur Google Play (sinon version refusée)

La 1.2.0 déclare `FOREGROUND_SERVICE_LOCATION` : la Play Console exige, avant l'examen :

1. **Contenu de l'appli › Autorisations de service de premier plan** : type
   « Localisation ». Cas d'usage : suivi de la position du chauffeur pendant une tournée
   qu'il démarre lui-même (« Je pars »), transmise à son gestionnaire ; arrêt à l'arrivée,
   à la déconnexion ou 12 h après le départ ; notification « Tournée en cours » permanente.
   **Vidéo** (YouTube non répertoriée, 30-60 s) : connexion d'un compte chauffeur › Mes
   tournées › fiche › « Je pars » › écran d'explication › invite système (Précise,
   « Lorsque vous utilisez l'appli ») › notification « Tournée en cours » › écran verrouillé
   › « Je suis arrivé » à destination › la notification disparaît.
   Pas de localisation en arrière-plan à déclarer (ACCESS_BACKGROUND_LOCATION non demandée).
2. **Sécurité des données** : Position précise (et approximative) collectée, finalité
   « Fonctionnalité de l'appli », partagée avec l'employeur (la société cliente),
   chiffrée en transit, refus possible ; plus nom/e-mail du compte, identifiants de
   l'appareil (jeton FCM), niveau de batterie joint aux positions.
3. **Politique de confidentialité** : la page `/politique-de-confidentialite` décrit la
   position du téléphone du chauffeur depuis le 22/09/2026 ; ajouter la durée de
   conservation une fois décidée.
4. Vérifier dans « Catalogue d'appareils » que les appareils sans GPS restent compatibles
   (le manifeste déclare le GPS `required="false"`).

`calypso-v1.2.0-release.apk` (même version, signé) sert aux essais par installation
directe ; il n'est pas versionné.

### Nouveautés v1.1.0 (versionCode 12)

- Deep link `calypso://vehicle/<id>` : scanner le QR "Partager la position" du
  monitoring web ouvre l'appli zoomée sur le véhicule (retour après login si
  session expirée).
- Bouton **Partager la position** (WhatsApp, Messenger, SMS…) dans la
  bottom-sheet du Monitoring et la fiche Véhicule (position datée + lien
  Google Maps).

## Upload vers Google Play

1. Se connecter à [Google Play Console](https://play.google.com/console)
2. Sélectionner l'application **Calypso** (`tn.belive.gisv2`)
3. **Production** → **Créer une release**
4. Upload du fichier `.aab` correspondant
5. Remplir les notes de version puis **Examiner** → **Publier**

## Regénérer un AAB

Depuis `services/gis-mobile/` :

```bash
npm run build -- --configuration=production
npx cap sync android
cd android
./gradlew.bat bundleRelease
```

Output : `android/app/build/outputs/bundle/release/app-release.aab`
