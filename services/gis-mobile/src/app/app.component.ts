import { Component } from '@angular/core';
import { ImmobilizationApprovalService } from './core/services/immobilization-approval.service';

@Component({
  selector: 'app-root',
  templateUrl: 'app.component.html',
  styleUrls: ['app.component.scss'],
  standalone: false,
})
export class AppComponent {
  constructor(private immoApproval: ImmobilizationApprovalService) {
    this.immoApproval.init();
  }
}
