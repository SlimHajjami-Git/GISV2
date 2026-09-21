import { Injectable } from '@angular/core';
import { HttpInterceptor, HttpRequest, HttpHandler, HttpEvent, HttpErrorResponse } from '@angular/common/http';
import { Observable, throwError, switchMap, catchError, of } from 'rxjs';
import { Router } from '@angular/router';
import { AuthService } from './auth.service';

/** Code du 403 renvoyé à un compte chauffeur hors de ses routes (PermissionMiddleware). */
export const DRIVER_APP_ONLY_CODE = 'DRIVER_APP_ONLY';

@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  private isRefreshing = false;
  private switchingToDriver = false;

  constructor(
    private authService: AuthService,
    private router: Router
  ) {}

  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    // Skip auth header for auth endpoints
    if (req.url.includes('/auth/login') || req.url.includes('/auth/register') || req.url.includes('/auth/refresh')) {
      return next.handle(req);
    }

    const token = this.authService.getToken();

    // Proactive refresh
    if (token && this.authService.isTokenExpiringSoon() && !this.isRefreshing) {
      this.isRefreshing = true;
      return this.authService.refreshAccessToken().pipe(
        switchMap(() => {
          this.isRefreshing = false;
          const newToken = this.authService.getToken();
          const cloned = req.clone({ setHeaders: { Authorization: `Bearer ${newToken}` } });
          return next.handle(cloned);
        }),
        catchError(() => {
          this.isRefreshing = false;
          const cloned = req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
          return next.handle(cloned);
        })
      );
    }

    let request = req;
    if (token) {
      request = req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
    }

    return next.handle(request).pipe(
      catchError((error: HttpErrorResponse) => {
        // Compte devenu « chauffeur » après sa dernière connexion : la session stockée
        // se croit gestionnaire, le serveur refuse tout sauf /api/driver-app. On
        // rafraîchit (la réponse porte alors accountType: 'driver') et on bascule vers
        // « Mes tournées » ; l'erreur d'origine remonte quand même à l'écran appelant.
        if (error.status === 403 && error.error?.code === DRIVER_APP_ONLY_CODE && !this.authService.isDriver()) {
          this.switchToDriverSpace();
          return throwError(() => error);
        }
        if (error.status === 401 && !req.url.includes('/auth/')) {
          if (!this.isRefreshing) {
            this.isRefreshing = true;
            return this.authService.refreshAccessToken().pipe(
              switchMap(response => {
                this.isRefreshing = false;
                if (response) {
                  const newToken = this.authService.getToken();
                  const retryReq = req.clone({ setHeaders: { Authorization: `Bearer ${newToken}` } });
                  return next.handle(retryReq);
                }
                this.authService.logout();
                this.router.navigate(['/login']);
                return throwError(() => error);
              }),
              catchError(refreshError => {
                this.isRefreshing = false;
                this.authService.logout();
                this.router.navigate(['/login']);
                return throwError(() => refreshError);
              })
            );
          }
        }
        return throwError(() => error);
      })
    );
  }

  private switchToDriverSpace(): void {
    if (this.switchingToDriver) return;
    this.switchingToDriver = true;
    this.authService.refreshAccessToken().subscribe({
      next: (response) => {
        this.switchingToDriver = false;
        if (response && this.authService.isDriver()) {
          this.router.navigate(['/driver/tours'], { replaceUrl: true });
        } else if (!response) {
          this.router.navigate(['/login'], { replaceUrl: true });
        }
      },
      error: () => { this.switchingToDriver = false; }
    });
  }
}
