import { Injectable } from '@angular/core';
import { AlertController } from '@ionic/angular';
import { KvStore } from './kv-store.service';
import { PhoneLocationService } from './phone-location.service';

export const LOCATION_CONSENT_KEY = 'driver_location_consent';

/**
 * Écran d'explication AVANT la demande de permission de localisation (exigence
 * Google Play, « prominent disclosure ») : l'invite système n'apparaît qu'après que
 * le chauffeur a lu POURQUOI, QUAND et POUR QUI sa position est utilisée, et a
 * touché « Continuer ». Un refus n'empêche jamais de déclarer « Je pars » : la
 * tournée est alors suivie par le seul boîtier du véhicule.
 */
@Injectable({ providedIn: 'root' })
export class LocationConsentService {
  static readonly DISCLOSURE_HEADER = 'Votre position pendant la tournée';
  static readonly DISCLOSURE_MESSAGE =
    'Calypso collecte la position de ce téléphone UNIQUEMENT pendant une tournée en cours, ' +
    'y compris lorsque l\'application est fermée ou en arrière-plan, afin de transmettre ' +
    'votre avancement à votre gestionnaire et de prendre le relais du boîtier GPS du véhicule. ' +
    'Une notification « Tournée en cours » reste affichée tant que le suivi est actif. ' +
    'Le suivi s\'arrête à l\'arrivée à destination, à la déconnexion, ou au plus tard 12 h après le départ.';

  constructor(
    private store: KvStore,
    private location: PhoneLocationService,
    private alertCtrl: AlertController
  ) {}

  /** Le chauffeur a-t-il déjà lu et accepté l'explication ? */
  async hasAccepted(): Promise<boolean> {
    return (await this.store.get<boolean>(LOCATION_CONSENT_KEY)) === true;
  }

  /**
   * Garantit explication PUIS permission. Rend vrai si la localisation est utilisable.
   * Déjà acceptée + permission accordée : rien ne s'affiche.
   */
  async ensure(): Promise<boolean> {
    const accepted = await this.hasAccepted();
    if (accepted && await this.location.isPermissionGranted()) return true;

    if (!accepted) {
      const ok = await this.showDisclosure();
      if (!ok) return false;
      await this.store.set(LOCATION_CONSENT_KEY, true);
    }

    if (await this.location.isPermissionGranted()) return true;
    const granted = await this.location.requestPermission();
    if (!granted) await this.offerSettings();
    return granted;
  }

  private async showDisclosure(): Promise<boolean> {
    return new Promise<boolean>(async (resolve) => {
      const alert = await this.alertCtrl.create({
        header: LocationConsentService.DISCLOSURE_HEADER,
        message: LocationConsentService.DISCLOSURE_MESSAGE,
        backdropDismiss: false,
        buttons: [
          { text: 'Refuser', role: 'cancel', handler: () => resolve(false) },
          { text: 'Continuer', handler: () => resolve(true) }
        ]
      });
      await alert.present();
    });
  }

  private async offerSettings(): Promise<void> {
    const alert = await this.alertCtrl.create({
      header: 'Localisation non autorisée',
      message: 'Sans cette autorisation, votre tournée n\'est suivie que par le boîtier du véhicule. ' +
        'Vous pouvez l\'activer dans les réglages de l\'application.',
      buttons: [
        { text: 'Plus tard', role: 'cancel' },
        { text: 'Ouvrir les réglages', handler: () => { this.location.openSettings(); } }
      ]
    });
    await alert.present();
  }
}
