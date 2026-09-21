import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { DriverToursPage } from './tours/driver-tours.page';
import { DriverTourDetailPage } from './tour-detail/driver-tour-detail.page';
import { DriverProfilePage } from './profile/driver-profile.page';

// Les gardes (AuthGuard + DriverGuard) sont posées sur le parent « driver »
// dans app-routing.module.ts : tout ce qui est ici est réservé au chauffeur.
const routes: Routes = [
  { path: '', redirectTo: 'tours', pathMatch: 'full' },
  { path: 'tours', component: DriverToursPage },
  { path: 'tours/:id', component: DriverTourDetailPage },
  { path: 'profile', component: DriverProfilePage }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class DriverPageRoutingModule {}
