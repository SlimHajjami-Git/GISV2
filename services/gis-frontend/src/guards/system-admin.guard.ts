import { Injectable } from '@angular/core';
import { CanActivate, Router, ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { PermissionService } from '../services/permission.service';
import { ToastService } from '../services/toast.service';

@Injectable({
  providedIn: 'root'
})
export class SystemAdminGuard implements CanActivate {
  constructor(
    private permissionService: PermissionService,
    private toastService: ToastService,
    private router: Router
  ) {}

  canActivate(route: ActivatedRouteSnapshot, state: RouterStateSnapshot): boolean {
    if (this.permissionService.isSystemAdmin()) {
      return true;
    }

    // User is not a system admin
    this.toastService.error(
      'Accès refusé',
      'Seuls les administrateurs système peuvent accéder à cette section'
    );
    
    this.router.navigate(['/dashboard']);
    return false;
  }
}
