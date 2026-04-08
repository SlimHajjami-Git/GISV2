import { Component } from '@angular/core';
import { ImmobilizationApprovalService } from './core/services/immobilization-approval.service';
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
    private signalr: SignalRService,
    private authService: AuthService
  ) {
    this.immoApproval.init();
    // Start SignalR as soon as auth is ready so real-time notifications work on all pages
    this.authService.ready.then(() => {
      if (this.authService.isAuthenticated()) {
        this.signalr.startConnection();
      }
    });
  }
}
