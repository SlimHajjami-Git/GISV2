import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { IonicModule } from '@ionic/angular';
import { DriverPageRoutingModule } from './driver-routing.module';
import { DriverToursPage } from './tours/driver-tours.page';
import { DriverTourDetailPage } from './tour-detail/driver-tour-detail.page';
import { DriverProfilePage } from './profile/driver-profile.page';

/**
 * Espace chauffeur (« Mes tournées »), chargé à la demande pour un compte de type
 * driver : liste, fiche avec carte et déclarations, profil. Un chauffeur ne voit
 * rien d'autre de l'application.
 */
@NgModule({
  imports: [CommonModule, FormsModule, IonicModule, DriverPageRoutingModule],
  declarations: [DriverToursPage, DriverTourDetailPage, DriverProfilePage]
})
export class DriverPageModule {}
