import { Injectable } from '@angular/core';
import { CanActivate, Router, ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { PermissionService, ModuleKey } from '../services/permission.service';
import { ToastService } from '../services/toast.service';

@Injectable({
  providedIn: 'root'
})
export class FeatureGuard implements CanActivate {
  constructor(
    private permissionService: PermissionService,
    private toastService: ToastService,
    private router: Router
  ) {}

  canActivate(route: ActivatedRouteSnapshot, state: RouterStateSnapshot): boolean {
    const requiredFeature = route.data['feature'] as ModuleKey;
    
    if (!requiredFeature) {
      // No feature required, allow access
      return true;
    }

    if (this.permissionService.hasModuleAccess(requiredFeature)) {
      return true;
    }

    // User doesn't have access to this feature
    this.toastService.error(
      'Accès non autorisé',
      'Cette fonctionnalité n\'est pas incluse dans votre abonnement'
    );
    
    this.router.navigate(['/dashboard']);
    return false;
  }
}
