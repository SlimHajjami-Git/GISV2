import { Component, OnInit, OnDestroy } from '@angular/core';
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
    provideRouter(routes),
    provideHttpClient(withInterceptors([authInterceptor]))
  ],
});
