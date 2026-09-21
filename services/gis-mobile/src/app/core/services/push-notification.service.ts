import { Injectable, NgZone } from '@angular/core';
import { Router } from '@angular/router';
import { Platform, ToastController } from '@ionic/angular';
import { PushNotifications, Token, PushNotificationSchema, ActionPerformed } from '@capacitor/push-notifications';
import { Device } from '@capacitor/device';
import { Subject, firstValueFrom } from 'rxjs';
import { ApiService } from './api.service';
import { AuthService } from './auth.service';
import { ImmobilizationApprovalService } from './immobilization-approval.service';

/** Types de push des tournées (FcmChannels.Tours côté serveur) et la tournée visée. */
export type TourPushType = 'tour_assigned' | 'tour_updated' | 'tour_cancelled';
export interface TourPushEvent {
  type: TourPushType;
  tourId: number;
  title?: string;
  message?: string;
}

const TOUR_PUSH_TYPES: ReadonlySet<string> = new Set(['tour_assigned', 'tour_updated', 'tour_cancelled']);

/**
 * FCM push notifications for the remote stop approval flow.
 *
 * Flow:
 * 1. On app startup (after login), request POST_NOTIFICATIONS permission
 * 2. Register with FCM → get device token
 * 3. POST token to /api/devicetokens so backend can target this device
 * 4. Handle incoming pushes in foreground (app open) and on tap (app background/killed)
 *
 * Pushes carrying `type=immobilization_request` are routed to
 * ImmobilizationApprovalService which renders the ACCEPTER/REFUSER alert.
 * This is what makes the flow work when the phone is locked or the app is killed.
 *
 * Real-time updates while the app is already open continue to arrive via SignalR;
 * FCM is the fallback when the SignalR connection isn't active.
 *
 * Tournées (compte chauffeur) : canal Android « tours » (importance HIGH), types
 * tour_assigned / tour_updated / tour_cancelled avec `tourId`. Au toucher → fiche
 * de la tournée ; au premier plan → toast « Voir » + événement tourPush$ pour que
 * la liste se recharge (un chauffeur n'a PAS SignalR : le push est sa seule source).
 */
@Injectable({
  providedIn: 'root'
})
export class PushNotificationService {
  private initialized = false;
  /** Dernier jeton FCM reçu : conservé d'une session à l'autre (FCM ne le redonne pas à chaque connexion). */
  private currentToken: string | null = null;
  /** Compte pour lequel ce jeton est inscrit côté serveur (null = désinscrit). */
  private registeredUserId: string | null = null;

  /** Push de tournée reçu (premier plan ou toucher) : les pages chauffeur rechargent. */
  readonly tourPush$ = new Subject<TourPushEvent>();

  constructor(
    private platform: Platform,
    private api: ApiService,
    private authService: AuthService,
    private immoApproval: ImmobilizationApprovalService,
    private router: Router,
    private zone: NgZone,
    private toastCtrl: ToastController
  ) {}

  async init(): Promise<void> {
    if (this.initialized) {
      // Déjà armé dans cette session de l'application : les écouteurs restent, mais après
      // une déconnexion (jeton désinscrit) puis une connexion — même compte ou autre —
      // il faut RÉINSCRIRE le jeton, sinon le nouveau compte ne reçoit aucun push.
      const userId = this.authService.getCurrentUserSync()?.id ?? null;
      if (this.currentToken && userId && this.registeredUserId !== userId) {
        this.registerTokenWithBackend(this.currentToken);
      }
      return;
    }
    if (!this.platform.is('capacitor')) return; // Only on native

    // Request permission FIRST, BEFORE setting `initialized = true`.
    //
    // v1.0.6 regression: we used to flip `initialized = true` on entry,
    // which meant that if the user denied the POST_NOTIFICATIONS prompt
    // once (or dismissed it), every subsequent login silently skipped
    // the whole setup — no channel created, no FCM token registered,
    // no lock-screen notifications. The symptom was "popups only work
    // when the app is in foreground" because SignalR kept working but
    // FCM was completely inactive.
    //
    // By gating `initialized` on permission success, a user who grants
    // permission later (via system settings → next login → init() runs
    // again) will complete the setup correctly.
    const perm = await PushNotifications.requestPermissions();
    if (perm.receive !== 'granted') {
      console.warn('[PushNotif] Permission denied — will retry on next init()');
      return;
    }

    // Create the Android NotificationChannel "immobilization" BEFORE registering.
    // The backend (FcmService.cs) sets channelId: "immobilization" on every
    // remote-stop push. On Android 8+ (API 26+), if the channel hasn't been
    // created by the app, the system silently drops the notification when the
    // screen is locked or the app is killed — which is exactly why pushes
    // never appeared on the lock screen in 1.0.3.
    // importance: 5 = MAX (heads-up banner + sound + vibration)
    // visibility: 1 = PUBLIC — content visible on lock screen (required so the
    //                          user can read "Véhicule X demande arrêt" without
    //                          unlocking to see ACCEPTER/REFUSER).
    if (this.platform.is('android')) {
      try {
        await PushNotifications.createChannel({
          id: 'immobilization',
          name: 'Arrêts à distance',
          description: 'Demandes d\'immobilisation nécessitant une décision immédiate',
          importance: 5,
          visibility: 1,
          sound: 'default',
          vibration: true,
          lights: true,
        });
        console.log('[PushNotif] Channel "immobilization" created/confirmed');
      } catch (err) {
        // Android <8 doesn't support channels; the call is still safe to make
        // on newer versions if the channel already exists (idempotent).
        console.warn('[PushNotif] createChannel(immobilization) failed:', err);
        // Do NOT abort: on some OEMs (Xiaomi/Huawei) the call can throw even
        // when the channel is live. Registration must still proceed.
      }

      // Canal « tours » : le serveur y envoie les tour_* (FcmChannels.Tours) en
      // priorité HIGH — ça sonne et s'affiche, sans interrompre le chauffeur
      // comme le fait le canal MAX des immobilisations. Sans ce canal créé par
      // l'application, Android 8+ jetterait la notification en silence.
      try {
        await PushNotifications.createChannel({
          id: 'tours',
          name: 'Tournées',
          description: 'Tournées envoyées, modifiées ou annulées par votre gestionnaire',
          importance: 4,
          visibility: 1,
          sound: 'default',
          vibration: true,
          lights: true,
        });
        console.log('[PushNotif] Channel "tours" created/confirmed');
      } catch (err) {
        console.warn('[PushNotif] createChannel(tours) failed:', err);
      }
    }

    // Listen for registration
    PushNotifications.addListener('registration', (token: Token) => {
      console.log('[PushNotif] FCM token received:', token.value.substring(0, 20) + '…');
      this.currentToken = token.value;
      this.registerTokenWithBackend(token.value);
    });

    PushNotifications.addListener('registrationError', (err) => {
      console.error('[PushNotif] Registration error:', err);
    });

    // Notification received while app is in foreground
    PushNotifications.addListener('pushNotificationReceived', (notification: PushNotificationSchema) => {
      console.log('[PushNotif] Push received in foreground:', notification);
      // The ImmobilizationApprovalService handles real-time via SignalR,
      // but if this push arrives and SignalR missed it, handle it here
      const data = notification.data;
      if (data?.type === 'immobilization_request') {
        this.immoApproval.handlePushNotification(data);
      } else if (this.isTourPush(data)) {
        this.handleTourPush(data, notification.title, notification.body, false);
      }
    });

    // User tapped on a notification (app was in background/closed)
    PushNotifications.addListener('pushNotificationActionPerformed', (action: ActionPerformed) => {
      console.log('[PushNotif] Push action performed:', action);
      const data = action.notification.data;
      if (data?.type === 'immobilization_request') {
        // Small delay to let the app fully initialize
        setTimeout(() => {
          this.immoApproval.handlePushNotification(data);
        }, 500);
      } else if (this.isTourPush(data)) {
        setTimeout(() => {
          this.handleTourPush(data, action.notification.title, action.notification.body, true);
        }, 500);
      }
    });

    // Register with FCM
    await PushNotifications.register();

    // Only NOW do we mark the service as initialized. If any of the above
    // threw (permission denied, createChannel crash, register failure),
    // the guard stays false and the next init() call retries the full flow.
    this.initialized = true;
    console.log('[PushNotif] init() complete — lock-screen notifications armed');
  }

  private isTourPush(data: any): boolean {
    return !!data && TOUR_PUSH_TYPES.has(String(data.type)) && !!data.tourId;
  }

  /**
   * Push de tournée. `tapped` = l'utilisateur a touché la notification (application en
   * arrière-plan ou fermée) → on ouvre la fiche. Au premier plan, un toast avec « Voir ».
   * Les callbacks des plugins arrivent hors zone Angular : tout passe par zone.run.
   */
  private handleTourPush(data: any, title?: string, body?: string, tapped = false): void {
    const event: TourPushEvent = {
      type: data.type as TourPushType,
      tourId: parseInt(String(data.tourId), 10),
      title: title || data.title,
      message: body || data.message
    };
    if (!event.tourId) return;

    this.zone.run(async () => {
      this.tourPush$.next(event);
      if (tapped) {
        this.openTour(event);
        return;
      }
      const toast = await this.toastCtrl.create({
        header: event.title || 'Tournée',
        message: event.message || '',
        duration: 6000,
        position: 'top',
        icon: event.type === 'tour_cancelled' ? 'close-circle-outline' : 'map-outline',
        color: event.type === 'tour_cancelled' ? 'warning' : 'primary',
        buttons: [{ text: 'Voir', handler: () => this.openTour(event) }]
      });
      await toast.present();
    });
  }

  private openTour(event: TourPushEvent): void {
    // Une tournée annulée n'est plus dans la liste active : la fiche répond 404 seulement
    // si elle n'est plus envoyée ; sinon elle s'ouvre avec son statut « annulée ».
    // Seul un compte chauffeur possède cet écran ; pour un gestionnaire, la garde renvoie aux onglets.
    if (!this.authService.isDriver()) return;
    this.router.navigate(['/driver/tours', event.tourId]);
  }

  private async registerTokenWithBackend(token: string): Promise<void> {
    const platform = this.platform.is('android') ? 'android' : 'ios';
    // Identifiant STABLE de l'appareil (survit aux réinstallations) : permet au
    // backend de désactiver les anciens jetons FCM du même téléphone — sans lui,
    // chaque réinstallation empile un jeton livrable et chaque notification
    // arrive en N exemplaires.
    let deviceId: string | undefined;
    try {
      deviceId = (await Device.getId()).identifier;
    } catch (e) {
      console.warn('[PushNotif] Device.getId() failed, registering without deviceId', e);
    }
    const userId = this.authService.getCurrentUserSync()?.id ?? null;
    this.api.registerDeviceToken(token, platform, deviceId).subscribe({
      next: () => { this.registeredUserId = userId; console.log('Device token registered with backend'); },
      error: (err) => console.error('Failed to register device token:', err)
    });
  }

  /**
   * Désinscrit le jeton FCM du compte (DELETE /api/devicetokens) — à appeler AVANT
   * authService.logout(), tant que le jeton d'accès est encore là. Attend la réponse
   * (meilleur effort, jamais bloquant) pour que la déconnexion ne coupe pas l'appel.
   *
   * Dès qu'un jeton FCM est connu, qu'il ait été inscrit PENDANT cette session ou non :
   * le serveur garde la ligne active des sessions précédentes, et une inscription ratée
   * au démarrage (hors ligne, 401 pendant un rafraîchissement) laissait registeredUserId
   * à null — le téléphone recevait alors encore les alertes du compte déconnecté
   * (relecture du 21/09/2026, constat 25). Le DELETE est idempotent côté serveur.
   */
  async unregister(): Promise<void> {
    if (!this.currentToken) return;
    const token = this.currentToken;
    this.registeredUserId = null;
    try {
      await firstValueFrom(this.api.unregisterDeviceToken(token));
    } catch (e) {
      console.warn('[PushNotif] unregister failed (token may stay active until stale)', e);
    }
  }
}
