import { Component, NgZone } from '@angular/core';
import { Router } from '@angular/router';
import { App, URLOpenListenerEvent } from '@capacitor/app';
import { ImmobilizationApprovalService } from './core/services/immobilization-approval.service';
import { PushNotificationService } from './core/services/push-notification.service';
import { SignalRService } from './core/services/signalr.service';
import { AuthService, AuthUser } from './core/services/auth.service';
import { TourTrackingService } from './core/services/tour-tracking.service';
import { DriverDeclarationsService } from './core/services/driver-declarations.service';
import { homeUrlFor } from './core/guards/driver.guard';

@Component({
  selector: 'app-root',
  templateUrl: 'app.component.html',
  styleUrls: ['app.component.scss'],
  standalone: false,
})
export class AppComponent {
  constructor(
    private immoApproval: ImmobilizationApprovalService,
    private pushService: PushNotificationService,
    private signalr: SignalRService,
    private authService: AuthService,
    private tourTracking: TourTrackingService,
    private declarations: DriverDeclarationsService,
    private router: Router,
    private zone: NgZone
  ) {
    // Deep link `calypso://vehicle/<id>?lat=..&lng=..` (QR du monitoring web):
    // ouvre la carte zoomée sur le véhicule. appUrlOpen couvre app déjà
    // lancée/en arrière-plan; getLaunchUrl couvre le démarrage à froid
    // (l'AuthGuard attend authService.ready, donc la session stockée est
    // restaurée avant que la navigation soit tranchée).
    App.addListener('appUrlOpen', (event: URLOpenListenerEvent) => this.handleDeepLink(event.url));
    App.getLaunchUrl().then(res => { if (res?.url) this.handleDeepLink(res.url); }).catch(() => {});

    // Defer off the first-paint critical path: the immobilisation-approval
    // service preloads an Audio element + opens a SignalR subscription, which
    // doesn't need to block the initial render.
    setTimeout(() => this.immoApproval.init(), 0);

    // Start SignalR + FCM push whenever there's an authenticated user.
    //
    // The previous code only fired once at boot via `authService.ready`,
    // which meant: first install → open app (unauthenticated) → login →
    // pushService.init() was NEVER called because the constructor had
    // already run with isAuthenticated() === false. The consequence in
    // v1.0.3 was that user_device_tokens stayed empty: no token ever
    // reached the backend, and lock-screen pushes had no target.
    //
    // Subscribing to currentUser$ instead fires init in all three cases:
    //  - cold start with stored creds (loadStoredAuth emits the user)
    //  - fresh install → login (login emits the user)
    //  - logout → login as different user (null then new user)
    // Both startConnection() and pushService.init() are idempotent so
    // re-emits of the same user are harmless.
    // Le BehaviorSubject émet d'abord null (session pas encore relue) : seul un vrai
    // passage « connecté → déconnecté » doit arrêter le suivi, sinon le démarrage à
    // froid effacerait l'état persisté que resume() vient justement relire.
    let previous: AuthUser | null = null;
    this.authService.getCurrentUser().subscribe(user => {
      if (user) {
        // Même compte, type changé côté serveur (converti chauffeur, ou repassé
        // gestionnaire) : le rafraîchissement du jeton vient de l'apprendre. L'écran
        // courant n'est plus le sien, et changer d'onglet ne rejoue pas les gardes du
        // parent : on le renvoie à SA page d'accueil (constat 24).
        const typeChanged = !!previous && previous.id === user.id && previous.accountType !== user.accountType;
        previous = user;
        // Defer off first paint: the WebSocket handshake + FCM registration
        // (permission prompt, channel creation) were blocking startup.
        setTimeout(() => {
          if (user.accountType === 'driver') {
            // Compte chauffeur : JAMAIS SignalR. Le serveur refuse le hub (403) et la
            // boucle de reconnexion viderait la batterie pour rien. Le push FCM est sa
            // seule source d'événements ; on reprend aussi un suivi interrompu et on
            // rejoue les déclarations restées en file. stopConnection() couvre le compte
            // passé chauffeur en cours de session (une connexion ouverte en « staff »).
            this.signalr.stopConnection();
            this.pushService.init();
            this.declarations.armAutoReplay();
            this.declarations.replay();
            this.tourTracking.resume();
          } else {
            this.declarations.disarmAutoReplay();
            this.signalr.startConnection();
            this.pushService.init();
          }
          if (typeChanged) {
            this.zone.run(() => this.router.navigateByUrl(homeUrlFor(user.accountType === 'driver'), { replaceUrl: true }))
              .catch(() => { /* navigation annulée */ });
          }
        }, 0);
      } else if (previous) {
        previous = null;
        // Plus de session. Déconnexion volontaire : la page profil a déjà tenté un dernier
        // envoi puis vidé les files. Session REFUSÉE par le serveur (jeton révoqué, compte
        // désactivé) : on arrête le capteur mais on GARDE l'état et la file de positions
        // persistés — rattachés au compte, ils repartent quand ce même chauffeur se
        // reconnecte, et aucun autre compte ne les lit (constat 12).
        this.tourTracking.stop({ keep: true });
        this.declarations.disarmAutoReplay();
        if (!this.router.url.startsWith('/login')) {
          this.zone.run(() => this.router.navigate(['/login'], { replaceUrl: true })).catch(() => { /* navigation annulée */ });
        }
      }
    });
  }

  /** Parse et route un deep link calypso://vehicle/<id>?lat=..&lng=.. */
  private handleDeepLink(url: string) {
    try {
      const m = url.match(/^calypso:\/\/vehicle\/(\d+)(?:\?(.*))?$/i);
      if (!m) return;
      const qs = new URLSearchParams(m[2] || '');
      const lat = parseFloat(qs.get('lat') || '');
      const lng = parseFloat(qs.get('lng') || '');
      const queryParams: any = { vehicleId: m[1] };
      // La page monitoring exige lat+lng+vehicleId pour zoomer — sans coords
      // valides on ouvre quand même la carte (vue d'ensemble).
      if (!isNaN(lat) && !isNaN(lng)) { queryParams.lat = lat; queryParams.lng = lng; }
      // Nonce: sans lui, re-scanner le même QR alors qu'on est déjà sur
      // /tabs/monitoring avec les mêmes params serait une navigation vers
      // une URL identique — ignorée par le Router, queryParams ne ré-émet
      // pas, et le re-scan ne re-zoomerait pas.
      queryParams.t = Date.now();
      // Les callbacks des plugins Capacitor arrivent hors zone Angular.
      this.zone.run(() => this.router.navigate(['/tabs/monitoring'], { queryParams }));
    } catch { /* URL malformée: ignorer */ }
  }
}
