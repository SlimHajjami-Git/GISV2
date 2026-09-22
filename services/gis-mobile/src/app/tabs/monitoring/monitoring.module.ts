import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IonicModule } from '@ionic/angular';
import { RouterModule } from '@angular/router';
import { MonitoringPage } from './monitoring.page';
import { PositionShareBarComponent } from '../../shared/position-share-bar.component';

@NgModule({
  imports: [
    CommonModule,
    IonicModule,
    PositionShareBarComponent,
    RouterModule.forChild([{ path: '', component: MonitoringPage }])
  ],
  declarations: [MonitoringPage]
})
export class MonitoringPageModule {}
