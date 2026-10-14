import { Injectable } from '@angular/core';
import { CanActivate, Router } from '@angular/router';
import { PermissionService } from '../services/permission.service';

/**
 * Écran Abonnement : même règle que l'entrée de menu (PermissionService.canManageOwnSubscription).
 * Sans lui, un non-administrateur qui tape /subscription voyait les boutons de commande,
 * que l'API lui refuse désormais.
 */
@Injectable({
  providedIn: 'root'
})
export class SubscriptionGuard implements CanActivate {
  constructor(
    private permissionService: PermissionService,
    private router: Router
  ) {}

  canActivate(): boolean {
    if (this.permissionService.canManageOwnSubscription()) {
      return true;
    }
    this.router.navigate(['/dashboard']);
    return false;
  }
}
