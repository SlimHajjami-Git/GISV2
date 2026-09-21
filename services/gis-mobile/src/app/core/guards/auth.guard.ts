import { Injectable } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivate, Router, RouterStateSnapshot } from '@angular/router';
import { AuthService } from '../services/auth.service';

@Injectable({
  providedIn: 'root'
})
export class AuthGuard implements CanActivate {
  constructor(
    private authService: AuthService,
    private router: Router
  ) {}

  async canActivate(route: ActivatedRouteSnapshot, state: RouterStateSnapshot): Promise<boolean> {
    // Jeton d'accès valide, ou jeton expiré rafraîchi EN SILENCE grâce au refresh
    // token stocké : on ne renvoie vers la connexion que si la session est vraiment
    // perdue (aucun jeton, ou refus du serveur).
    if (await this.authService.restoreSession()) {
      return true;
    }
    // Conserver la cible (ex: deep link QR /tabs/monitoring?vehicleId=..)
    // pour y revenir après connexion au lieu de la perdre.
    this.router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
    return false;
  }
}
