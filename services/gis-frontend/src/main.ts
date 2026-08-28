import { Component, OnInit, OnDestroy, provideZoneChangeDetection } from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';
import { provideRouter, RouterOutlet } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { routes } from './app.routes';
import { authInterceptor } from './services/auth.interceptor';
import { ToastContainerComponent } from './components/shared/toast-container.component';
import { NotificationToastService } from './services/notification-toast.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, ToastContainerComponent],
  template: `<router-outlet></router-outlet><app-toast-container></app-toast-container>`,
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
    provideRouter(routes),
    provideHttpClient(withInterceptors([authInterceptor]))
  ],
});
