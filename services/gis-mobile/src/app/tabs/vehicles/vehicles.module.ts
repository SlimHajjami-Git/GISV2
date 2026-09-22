import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { IonicModule } from '@ionic/angular';
import { RouterModule } from '@angular/router';
import { VehiclesPage } from './vehicles.page';
import { PositionShareBarComponent } from '../../shared/position-share-bar.component';

@NgModule({
  imports: [
    CommonModule,
    FormsModule,
    IonicModule,
    PositionShareBarComponent,
    RouterModule.forChild([{ path: '', component: VehiclesPage }])
  ],
  declarations: [VehiclesPage]
})
export class VehiclesPageModule {}
