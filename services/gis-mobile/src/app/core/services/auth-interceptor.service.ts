import { Injectable, NgZone } from '@angular/core';
import { HttpInterceptor, HttpRequest, HttpHandler, HttpEvent, HttpErrorResponse } from '@angular/common/http';
import { Observable, throwError, switchMap, catchError, defer, from, firstValueFrom } from 'rxjs';
import { Router } from '@angular/router';
import { AuthService } from './auth.service';
import { homeUrlFor } from '../guards/driver.guard';

/** Code du 403 renvoyé à un compte chauffeur hors de ses routes (PermissionMiddleware). */
export const DRIVER_APP_ONLY_CODE = 'DRIVER_APP_ONLY';
/** Code du 403 de /api/driver-app pour un compte sans fiche chauffeur (DriverAppController). */
export const NO_DRIVER_PROFILE_CODE = 'NO_DRIVER_PROFILE';

/**
 * Jeton d'accès sur chaque requête, et son renouvellement.
 *
 * Tout passe par LE rafraîchissement partagé d'AuthService (un seul en vol pour toute
 * l'application, constat 1 de la relecture du 21/09/2026) :
 *  - une requête émise pendant un rafraîchissement ATTEND son résultat puis part avec le
 *    nouveau jeton (auparavant elle partait avec l'ancien, prenait un 401 et ce 401
 *    remontait tel quel : un « Je suis arrivé » rejoué était abandonné, constat 6) ;
 *  - jeton sur le point d'expirer : rafraîchissement proactif, partagé lui aussi ;
 *  - 401 : si un autre appel a déjà remplacé le jeton, on rejoue avec le nouveau ; sinon
 *    un rafraîchissement (partagé), puis un seul nouvel essai.
 * L'intercepteur NE DÉCONNECTE JAMAIS : seul AuthService efface la session, et seulement
 * sur un refus explicite du jeton en place. Un rafraîchissement impossible (réseau, 5xx)
 * laisse la session intacte et rend l'erreur d'origine (constat 5).
 */
@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  static readonly ACCOUNT_CHECK_COOLDOWN_MS = 60_000;
  private switchingSpace = false;
  private lastAccountCheck = 0;

  constructor(
    private authService: AuthService,
    private router: Router,
    private zone: NgZone
  ) {}

  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    // Les appels d'authentification portent leurs propres jetons (et /auth/refresh ne doit
    // jamais s'attendre lui-même).
    if (req.url.includes('/auth/login') || req.url.includes('/auth/register') || req.url.includes('/auth/refresh')) {
      return next.handle(req);
    }
    return defer(() => from(this.beforeSend())).pipe(switchMap(() => this.send(req, next, false)));
  }

  /** Attend le rafraîchissement en vol, ou le lance (partagé) si le jeton expire bientôt. */
  private async beforeSend(): Promise<void> {
    const pending = this.authService.pendingRefresh;
    if (pending) {
      await pending;
      return;
    }
    if (this.authService.getToken() && this.authService.isTokenExpiringSoon()) {
      await firstValueFrom(this.authService.refreshAccessToken());
    }
  }

  private send(req: HttpRequest<any>, next: HttpHandler, retried: boolean): Observable<HttpEvent<any>> {
    const tokenUsed = this.authService.getToken();
    const request = tokenUsed ? req.clone({ setHeaders: { Authorization: `Bearer ${tokenUsed}` } }) : req;

    return next.handle(request).pipe(
      catchError((error: HttpErrorResponse) => {
        if (error.status === 403) this.onForbidden(error, req);
        if (error.status === 401 && !retried && tokenUsed) {
          return from(this.renewAfter401(tokenUsed)).pipe(
            switchMap(renewed => renewed ? this.send(req, next, true) : throwError(() => error))
          );
        }
        return throwError(() => error);
      })
    );
  }

  /** Après un 401 : le jeton a-t-il été remplacé (par un autre appel, ou par un rafraîchissement) ? */
  private async renewAfter401(tokenUsed: string): Promise<boolean> {
    const pending = this.authService.pendingRefresh;
    if (pending) {
      await pending;
    } else if (this.authService.getToken() === tokenUsed) {
      await firstValueFrom(this.authService.refreshAccessToken());
    }
    const current = this.authService.getToken();
    return !!current && current !== tokenUsed;
  }

  /**
   * Type de compte changé côté serveur depuis la dernière connexion (constat 24) :
   *  - DRIVER_APP_ONLY : un chauffeur appelle une route gestionnaire. Session encore
   *    « staff » → rafraîchir (la réponse porte accountType 'driver', app.component bascule
   *    alors vers « Mes tournées ») ; déjà « driver » → l'écran courant n'est pas le sien :
   *    retour direct à ses tournées.
   *  - NO_DRIVER_PROFILE sur /driver-app alors que la session se croit chauffeur : le
   *    compte a peut-être été repassé gestionnaire ; le rafraîchissement le dira.
   * L'erreur d'origine remonte quand même à l'écran appelant.
   */
  private onForbidden(error: HttpErrorResponse, req: HttpRequest<any>): void {
    const code = error.error?.code;
    if (code === DRIVER_APP_ONLY_CODE) {
      if (this.authService.isDriver()) this.goHomeIfElsewhere();
      else this.refreshAccountType();
    } else if (code === NO_DRIVER_PROFILE_CODE && this.authService.isDriver() && req.url.includes('/driver-app/')) {
      this.refreshAccountType();
    }
  }

  private refreshAccountType(): void {
    // Un écran qui recharge en boucle ne doit pas déclencher un rafraîchissement par 403.
    if (this.switchingSpace || Date.now() - this.lastAccountCheck < AuthInterceptor.ACCOUNT_CHECK_COOLDOWN_MS) return;
    this.lastAccountCheck = Date.now();
    this.switchingSpace = true;
    this.authService.refreshAccessToken().subscribe({
      next: () => { this.switchingSpace = false; this.goHomeIfElsewhere(); },
      error: () => { this.switchingSpace = false; }
    });
  }

  private goHomeIfElsewhere(): void {
    if (!this.authService.getToken()) return;   // session refusée : app.component mène à la connexion
    const home = homeUrlFor(this.authService.isDriver());
    const space = home.startsWith('/driver') ? '/driver' : '/tabs';
    if (this.router.url.startsWith(space)) return;
    this.zone.run(() => this.router.navigateByUrl(home, { replaceUrl: true })).catch(() => { /* navigation annulée */ });
  }
}
