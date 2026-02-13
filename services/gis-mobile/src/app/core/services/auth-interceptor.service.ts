import { Injectable } from '@angular/core';
import { HttpInterceptor, HttpRequest, HttpHandler, HttpEvent, HttpErrorResponse } from '@angular/common/http';
import { Observable, throwError, switchMap, catchError, of } from 'rxjs';
import { Router } from '@angular/router';
import { AuthService } from './auth.service';

@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  private isRefreshing = false;

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
}
