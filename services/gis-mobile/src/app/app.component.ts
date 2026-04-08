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
    // Start SignalR + FCM push as soon as auth is ready
    this.authService.ready.then(() => {
      if (this.authService.isAuthenticated()) {
        this.signalr.startConnection();
        this.pushService.init();
      }
    });
  }
}
