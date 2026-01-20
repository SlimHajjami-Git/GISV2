import { Injectable } from '@angular/core';
import { CanActivate, Router, ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { AdminService } from '../services/admin.service';

@Injectable({
  providedIn: 'root'
})
export class AdminGuard implements CanActivate {
  constructor(
    private adminService: AdminService,
    private router: Router
  ) {}

  canActivate(route: ActivatedRouteSnapshot, state: RouterStateSnapshot): boolean {
    if (this.adminService.isAuthenticated()) {
      const user = this.adminService.getAdminUser();
      
      // Check if user has admin role
      if (user && (user.role === 'super_admin' || user.role === 'admin')) {
        return true;
      }
      
      // Redirect to dashboard if not admin
      this.router.navigate(['/dashboard']);
      return false;
    }

    // Redirect to login if not authenticated
    this.router.navigate(['/admin/login']);
    return false;
  }
}
