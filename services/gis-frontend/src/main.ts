import { Component, OnInit, OnDestroy, provideZoneChangeDetection } from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter, RouterOutlet } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { routes } from './app.routes';
import { authInterceptor } from './services/auth.interceptor';
import { ToastContainerComponent } from './components/shared/toast-container.component';
import { GuidedHelpComponent } from './components/shared/guided-help.component';
import { NotificationToastService } from './services/notification-toast.service';

// La visite guidee vit ICI, une seule fois, et non dans <app-layout> : chaque
// page porte sa propre app-layout, et la visite, qui change de page d'une etape a
// l'autre, etait detruite puis relancee a l'etape 1 a chaque « Suivant ».
@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, ToastContainerComponent, GuidedHelpComponent],
  template: `<router-outlet></router-outlet><app-toast-container></app-toast-container><app-guided-help></app-guided-help>`,
})
export class App implements OnInit, OnDestroy {
  constructor(private notificationToast: NotificationToastService) {}

  ngOnInit(): void {
    this.notificationToast.initialize();
  }

  ngOnDestroy(): void {
    this.notificationToast.destroy();
  }
}

bootstrapApplication(App, {
  providers: [
    // Angular 21 est « zoneless » par defaut : sans ce fournisseur, zone.js est
    // charge mais IGNORE, et seule une interaction (clic, saisie) rafraichit la
    // vue — un retour HTTP ou un await laissent l ecran fige. Constate en prod
    // le 26/08/2026 (« Envoi… » bloque, page noire sur l aiguillage regional).
    // L application est ecrite en style zone : on reactive explicitement.
    provideZoneChangeDetection(),
    // Neuf composants declarent des animations (@fadeIn, @slideIn, @scaleIn :
    // Reparations, Echeances, Entretiens, Fournisseurs, Vehicules, les fenetres
    // Garage et Vehicule, le renouvellement de document, les marques) alors
    // qu'AUCUN moteur d'animation n'etait fourni. Le moteur de rendu levait
    // alors NG05105 sur la premiere propriete synthetique rencontree, ce qui
    // INTERROMPT la passe de detection AVANT l'ecriture des valeurs : la fenetre
    // s'ouvrait vide, intitules seuls, et ne se remplissait qu'au cycle suivant
    // — une ligne par cycle sur Echeances. Constate par Karim les 21 et
    // 22/09/2026, reproduit dans l'application.
    // NOOP et non provideAnimations() : les 35 liaisons d'animation n'ont JAMAIS
    // rien anime, ni ici ni en production. Les activer ajouterait 200 ms de
    // fondu et 250 ms de glissement a chaque ouverture de fenetre, ce que
    // personne n'a jamais vu ni demande. Ici l'apparence ne change pas d'un
    // pixel, seul le garde-fou est desarme.
    provideNoopAnimations(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([authInterceptor]))
  ],
});
