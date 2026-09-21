import { Injectable } from '@angular/core';
import { CanActivate, Router, UrlTree } from '@angular/router';
import { AuthService } from '../services/auth.service';

/**
 * Aiguillage par type de compte (décision de Slim, 18 et 21/09/2026 : « le chauffeur
 * est un utilisateur »). Un compte chauffeur ne voit QUE ses tournées ; le serveur
 * répond 403 sur toute autre route, y compris le hub SignalR. Ces gardes évitent
 * donc d'ouvrir des écrans qui ne pourraient rien charger.
 *
 * Angular exécute les canActivate d'une même route EN PARALLÈLE, pas l'un après
 * l'autre : ces gardes ne peuvent donc pas compter sur l'AuthGuard pour avoir
 * restauré la session avant elles (constat 24 de la relecture du 21/09/2026). Elles
 * attendent elles-mêmes restoreSession() — partagée, donc un seul rafraîchissement —
 * avant de lire le type de compte : un jeton expiré dont le rafraîchissement apporte
 * accountType 'driver' (compte converti la veille) doit mener à « Mes tournées », pas
 * aux onglets gestionnaire où tout répondrait 403.
 */

/** Protège /driver/* : réservé aux comptes chauffeur ; un gestionnaire retourne aux onglets. */
@Injectable({ providedIn: 'root' })
export class DriverGuard implements CanActivate {
  constructor(private authService: AuthService, private router: Router) {}

  async canActivate(): Promise<boolean | UrlTree> {
    await this.authService.restoreSession();
    if (this.authService.isDriver()) return true;
    return this.router.createUrlTree(['/tabs/dashboard']);
  }
}

/** Protège /tabs/* : un chauffeur est renvoyé vers ses tournées. */
@Injectable({ providedIn: 'root' })
export class StaffGuard implements CanActivate {
  constructor(private authService: AuthService, private router: Router) {}

  async canActivate(): Promise<boolean | UrlTree> {
    await this.authService.restoreSession();
    if (!this.authService.isDriver()) return true;
    return this.router.createUrlTree(['/driver/tours']);
  }
}

/** Page d'accueil selon le compte : sert après connexion et pour les redirections. */
export function homeUrlFor(isDriver: boolean): string {
  return isDriver ? '/driver/tours' : '/tabs/dashboard';
}
