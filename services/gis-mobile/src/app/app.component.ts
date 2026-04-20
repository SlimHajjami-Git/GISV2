import { Component } from '@angular/core';
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
    private authService: AuthService
  ) {
    this.immoApproval.init();

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
        this.signalr.startConnection();
        this.pushService.init();
      }
    });
  }
}
