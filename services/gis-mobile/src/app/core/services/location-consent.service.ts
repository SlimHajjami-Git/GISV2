import { Injectable } from '@angular/core';
import { AlertController } from '@ionic/angular';
import { KvStore } from './kv-store.service';
import { LocationStatus, PhoneLocationService } from './phone-location.service';

export const LOCATION_CONSENT_KEY = 'driver_location_consent';

/**
 * Écran d'explication AVANT la demande de permission de localisation (exigence
 * Google Play, « prominent disclosure ») : l'invite système n'apparaît qu'après que
 * le chauffeur a lu POURQUOI, QUAND et POUR QUI sa position est utilisée, et a
 * touché « Continuer ». Un refus n'empêche jamais de déclarer « Je pars » : la
 * tournée est alors suivie par le seul boîtier du véhicule.
 *
 * Trois situations à ne pas confondre (relecture du 21/09/2026, constats 11, 19, 21),
 * chacune avec son message et SON action utile :
 *  - permission refusée → réglages de l'APPLICATION (« Autorisations › Position ») ;
 *  - position « approximative » seulement → nouvelle demande (Android propose alors
 *    « Précise »), puis réglages de l'application (« Utiliser la position exacte ») ;
 *  - localisation du TÉLÉPHONE coupée → boîte système « Activer la localisation »,
 *    sinon les réglages rapides. Les réglages de l'application n'y peuvent rien : on y
 *    envoyait le chauffeur, qui y trouvait la permission… accordée.
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

  static readonly DISABLED_HEADER = 'Localisation du téléphone désactivée';
  static readonly DISABLED_MESSAGE =
    'La localisation de ce téléphone est coupée : votre position ne peut pas être transmise. ' +
    'Touchez « Activer » puis « OK », ou allumez « Localisation » dans les réglages rapides ' +
    '(glissez depuis le haut de l\'écran).';
  static readonly STILL_DISABLED_MESSAGE =
    'La localisation est toujours coupée. Glissez depuis le haut de l\'écran et touchez « Localisation » pour l\'allumer.';
  static readonly COARSE_HEADER = 'Position précise nécessaire';
  static readonly COARSE_MESSAGE =
    'Vous avez accordé la position « approximative » : elle est floue de plusieurs kilomètres et ne permet pas ' +
    'de suivre la tournée. Dans les réglages de l\'application, ouvrez « Autorisations » › « Position » et ' +
    'activez « Utiliser la position exacte ».';
  static readonly DENIED_HEADER = 'Localisation non autorisée';
  static readonly DENIED_MESSAGE =
    'Sans cette autorisation, votre tournée n\'est suivie que par le boîtier du véhicule. Dans les réglages de ' +
    'l\'application, ouvrez « Autorisations » › « Position », choisissez « Autoriser seulement si l\'appli est ' +
    'en cours d\'utilisation » et activez « Utiliser la position exacte ».';
  static readonly APP_SETTINGS_BUTTON = 'Ouvrir les réglages de l\'application';

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
   * Garantit explication PUIS permission (position PRÉCISE, localisation allumée).
   * Rend vrai si le suivi peut tourner. Déjà acceptée + tout en ordre : rien ne s'affiche.
   */
  async ensure(): Promise<boolean> {
    const accepted = await this.hasAccepted();
    let status = await this.location.getLocationStatus();
    if (status === 'granted' && accepted) return true;

    let disclosed = false;
    const disclose = async (): Promise<boolean> => {
      if (disclosed) return true;
      disclosed = true;
      const ok = await this.showDisclosure();
      if (ok) await this.store.set(LOCATION_CONSENT_KEY, true);
      return ok;
    };

    if (!accepted && !(await disclose())) return false;

    if (status === 'disabled') {
      if (!(await this.askEnableLocation())) return false;
      status = await this.location.getLocationStatus();
      if (status === 'disabled') return false;
    }
    if (status === 'granted') return true;

    // Une invite système va suivre : l'explication la précède TOUJOURS, même déjà
    // acceptée. Un consentement restauré par la sauvegarde Android (réinstallation,
    // nouveau téléphone) ne doit pas faire apparaître l'invite sans elle (constat 29).
    if (!(await disclose())) return false;
    status = await this.location.requestPermission();
    if (status === 'granted') return true;
    if (status === 'disabled') {
      await this.askEnableLocation();
      return (await this.location.getLocationStatus()) === 'granted';
    }
    await this.explainRefusal(status);
    return false;
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

  /**
   * Localisation du téléphone coupée : « Activer » déclenche la boîte système de Google
   * Play Services (un seul toucher sur « OK »). Rend vrai si elle est allumée ensuite.
   */
  private async askEnableLocation(): Promise<boolean> {
    const wantsIt = await new Promise<boolean>(async (resolve) => {
      const alert = await this.alertCtrl.create({
        header: LocationConsentService.DISABLED_HEADER,
        message: LocationConsentService.DISABLED_MESSAGE,
        backdropDismiss: false,
        buttons: [
          { text: 'Plus tard', role: 'cancel', handler: () => resolve(false) },
          { text: 'Activer', handler: () => resolve(true) }
        ]
      });
      await alert.present();
    });
    if (!wantsIt) return false;
    const on = await this.location.promptEnableLocation();
    if (!on) {
      const alert = await this.alertCtrl.create({
        header: LocationConsentService.DISABLED_HEADER,
        message: LocationConsentService.STILL_DISABLED_MESSAGE,
        buttons: ['Compris']
      });
      await alert.present();
    }
    return on;
  }

  /** Permission pas (ou mal) accordée : message exact, et la fiche de l'application en action. */
  private async explainRefusal(status: LocationStatus): Promise<void> {
    const coarse = status === 'coarse';
    const alert = await this.alertCtrl.create({
      header: coarse ? LocationConsentService.COARSE_HEADER : LocationConsentService.DENIED_HEADER,
      message: coarse ? LocationConsentService.COARSE_MESSAGE : LocationConsentService.DENIED_MESSAGE,
      buttons: [
        { text: 'Plus tard', role: 'cancel' },
        { text: LocationConsentService.APP_SETTINGS_BUTTON, handler: () => { this.location.openSettings(); } }
      ]
    });
    await alert.present();
  }
}
