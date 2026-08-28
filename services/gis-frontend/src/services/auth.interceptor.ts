import { HttpInterceptorFn, HttpErrorResponse, HttpClient } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

let isRefreshing = false;
let isAdminRefreshing = false;

/** Purge la session ADMIN uniquement — sans toucher à la session utilisateur. */
function clearAdminSession(): void {
  localStorage.removeItem('admin_token');
  localStorage.removeItem('admin_user');
  localStorage.removeItem('admin_refresh_token');
}

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const router = inject(Router);
  const http = inject(HttpClient);

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
  // Le repli croisé était une FAILLE : une route client (non-admin) prenait le
  // jeton admin en secours. Si une session system_admin traînait dans le
  // navigateur (onglet /admin) et que le jeton client manquait, les requêtes
  // « client » partaient avec le jeton system_admin — qui CONTOURNE le blocage
  // d'abonnement et le cloisonnement par société. Symptôme (recette 28/08/2026) :
  // un compte suspendu « reprenait le contrôle » via Réessayer, en opérant en
  // réalité comme l'administrateur. Chaque espace n'utilise donc que SON jeton.
  const token = isAdminRoute
    ? localStorage.getItem('admin_token')
    : localStorage.getItem('auth_token');

  // Proactive refresh: if token is expiring soon, refresh before sending the request.
  // UNIQUEMENT hors espace admin : ce flux rafraîchit la session UTILISATEUR et
  // rejouerait une requête /api/admin avec le jeton utilisateur (→ 403 silencieux).
  if (!isAdminRoute && token && authService.isTokenExpiringSoon() && !isRefreshing) {
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
      // Société suspendue par le sys_admin ou abonnement expiré au-delà de la
      // grâce : le backend renvoie 403 { error: "subscription_..." } sur toutes
      // les requêtes → écran de blocage dédié (pas d'espace admin concerné).
      if (error.status === 403 && !isAdminRoute
          && typeof error.error?.error === 'string' && error.error.error.startsWith('subscription_')
          && !router.url.startsWith('/abonnement-suspendu')) {
        const reason = error.error.error.replace('subscription_', '');
        router.navigate(['/abonnement-suspendu'], { queryParams: { reason } });
        return throwError(() => error);
      }
      if (error.status === 401 && !req.url.includes('/auth/')) {
        // ── Session ADMIN expirée (jeton 24 h) ────────────────────────────
        // Cause du « dashboard admin à zéros » : l'ancien code lançait le
        // refresh UTILISATEUR puis rejouait la requête /api/admin avec le
        // jeton utilisateur → 403 avalé par les composants. La session admin
        // a SON refresh token (stocké au login admin) : on rafraîchit le
        // jeton ADMIN et on rejoue avec lui ; sinon retour à /admin/login.
        if (isAdminRoute) {
          const adminToken = localStorage.getItem('admin_token');
          const adminRefresh = localStorage.getItem('admin_refresh_token');
          if (adminToken && adminRefresh && !isAdminRefreshing) {
            isAdminRefreshing = true;
            return http.post<any>('/api/auth/refresh', { token: adminToken, refreshToken: adminRefresh }).pipe(
              switchMap(resp => {
                isAdminRefreshing = false;
                if (resp?.token) {
                  localStorage.setItem('admin_token', resp.token);
                  if (resp.refreshToken) localStorage.setItem('admin_refresh_token', resp.refreshToken);
                  return next(req.clone({ setHeaders: { Authorization: `Bearer ${resp.token}` } }));
                }
                clearAdminSession();
                router.navigate(['/admin/login']);
                return throwError(() => error);
              }),
              catchError(refreshErr => {
                isAdminRefreshing = false;
                clearAdminSession();
                router.navigate(['/admin/login']);
                return throwError(() => refreshErr);
              })
            );
          }
          // Pas de refresh possible (ancienne session sans refresh token) →
          // reconnexion admin propre, sans toucher à la session utilisateur.
          if (!isAdminRefreshing) {
            clearAdminSession();
            router.navigate(['/admin/login']);
          }
          return throwError(() => error);
        }

        // ── Session UTILISATEUR : flux existant ──
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
