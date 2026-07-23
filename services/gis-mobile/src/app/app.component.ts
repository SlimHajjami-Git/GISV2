import { Component, NgZone } from '@angular/core';
import { Router } from '@angular/router';
import { App, URLOpenListenerEvent } from '@capacitor/app';
import { ImmobilizationApprovalService } from './core/services/immobilization-approval.service';
import { PushNotificationService } from './core/services/push-notification.service';
import { SignalRService } from './core/services/signalr.service';
import { AuthService } from './core/services/auth.service';

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
    this.authService.getCurrentUser().subscribe(user => {
      if (user) {
        // Defer off first paint: the WebSocket handshake + FCM registration
        // (permission prompt, channel creation) were blocking startup.
        setTimeout(() => {
          this.signalr.startConnection();
          this.pushService.init();
        }, 0);
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
