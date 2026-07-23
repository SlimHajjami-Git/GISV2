# Calypso Mobile — Releases

App Bundles (AAB) signés prêts à être uploadés sur Google Play Console.

## Fichiers

| Fichier | Version | versionCode | Date | Signature |
|---|---|---|---|---|
| `calypso-v1.1.0-release.aab` | 1.1.0 | 12 | 2026-07-23 | `calypso` keystore (SHA-1: `8D:93:06:90:97:39:7E:0D:37:D8:DB:77:C1:A6:89:E7:F3:FB:02:8D`) |
| `calypso-v1.0.9-release.aab` | 1.0.9 | 11 | 2026-04-22 | `calypso` keystore (SHA-1: `8D:93:06:90:97:39:7E:0D:37:D8:DB:77:C1:A6:89:E7:F3:FB:02:8D`) |

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
