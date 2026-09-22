import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IonicModule } from '@ionic/angular';
import { RouterModule } from '@angular/router';
import { DashboardPage } from './dashboard.page';
import { FleetStateSummaryComponent } from '../../shared/fleet-state-summary.component';

@NgModule({
  imports: [
    CommonModule,
    IonicModule,
    FleetStateSummaryComponent,
    RouterModule.forChild([{ path: '', component: DashboardPage }])
  ],
  declarations: [DashboardPage]
})
export class DashboardPageModule {}
