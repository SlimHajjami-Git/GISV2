import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

let isRefreshing = false;

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  // Skip auth header for public endpoints (login, register, refresh, device-check,
  // and the pre-login AI assistant). These must never carry a JWT or trigger the
  // 401 refresh/logout flow.
  if (req.url.includes('/auth/login') || req.url.includes('/auth/register') || req.url.includes('/auth/refresh') || req.url.includes('/devicecheck/') || req.url.includes('/assistant/')) {
    return next(req);
  }

  // Use admin_token for admin routes, auth_token for everything else.
  // /auth/impersonate est déclenché depuis l'espace /admin (super-admin) -> doit
  // utiliser le admin_token, sinon le backend reçoit un token non-system_admin (400).
  const isAdminRoute = req.url.includes('/api/admin') || req.url.includes('/auth/impersonate');
  const token = isAdminRoute
    ? (localStorage.getItem('admin_token') || localStorage.getItem('auth_token'))
    : (localStorage.getItem('auth_token') || localStorage.getItem('admin_token'));

  // Proactive refresh: if token is expiring soon, refresh before sending the request
  if (token && authService.isTokenExpiringSoon() && !isRefreshing) {
    isRefreshing = true;
    return authService.refreshAccessToken().pipe(
      switchMap(response => {
        isRefreshing = false;
        const newToken = localStorage.getItem('auth_token');
        const clonedRequest = req.clone({
          setHeaders: { Authorization: `Bearer ${newToken}` }
        });
        return next(clonedRequest);
      }),
      catchError(err => {
        isRefreshing = false;
        return next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }));
      })
    );
  }

  let request = req;
  if (token) {
    request = req.clone({
      setHeaders: { Authorization: `Bearer ${token}` }
    });
  }

  return next(request).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401 && !req.url.includes('/auth/')) {
        // Token expired — try to refresh
        if (!isRefreshing) {
          isRefreshing = true;
          return authService.refreshAccessToken().pipe(
            switchMap(response => {
              isRefreshing = false;
              if (response) {
                const newToken = localStorage.getItem('auth_token');
                const retryRequest = req.clone({
                  setHeaders: { Authorization: `Bearer ${newToken}` }
                });
                return next(retryRequest);
              }
              // Refresh failed — force logout
              authService.logout();
              router.navigate(['/login']);
              return throwError(() => error);
            }),
            catchError(refreshError => {
              isRefreshing = false;
              authService.logout();
              router.navigate(['/login']);
              return throwError(() => refreshError);
            })
          );
        }
        // Already refreshing, just fail
        return throwError(() => error);
      }
      return throwError(() => error);
    })
  );
};
