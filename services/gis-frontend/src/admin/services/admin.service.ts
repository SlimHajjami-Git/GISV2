import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { BehaviorSubject, Observable, of, throwError } from 'rxjs';
import { catchError, map, tap } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface AdminUser {
  id: string;
  email: string;
  name: string;
  role: 'super_admin' | 'admin' | 'support';
  permissions: string[];
  lastLogin?: Date;
}

export interface Client {
  id: number;
  name: string;
  email: string;
  phone?: string;
  type: string;
  subscriptionId?: number;
  subscriptionName?: string;
  maxVehicles: number;
  currentVehicles: number;
  currentUsers: number;
  status: 'active' | 'suspended' | 'pending' | 'cancelled';
  createdAt: Date;
  lastActivity?: Date;
  settings?: ClientSettings;
}

export interface ClientSettings {
  enabledFeatures: string[];
  disabledPages: string[];
  maxUsers: number;
  maxDevices: number;
  apiAccess: boolean;
  customBranding: boolean;
}

export interface SystemUser {
  id: number;
  name: string;
  email: string;
  phone?: string;
  companyId: number;
  companyName: string;
  roles: string[];
  permissions: string[];
  status: 'active' | 'inactive' | 'suspended';
  lastLogin?: Date;
  createdAt: Date;
  isOnline: boolean;
}

export interface ServiceHealth {
  name: string;
  status: 'healthy' | 'degraded' | 'down';
  responseTime: number;
  lastCheck: Date;
  uptime: number;
  details?: Record<string, any>;
}

export interface DashboardStats {
  totalClients: number;
  activeClients: number;
  totalUsers: number;
  usersOnline: number;
  totalVehicles: number;
  activeDevices: number;
  totalPositionsToday: number;
  alertsToday: number;
  revenueThisMonth: number;
  newClientsThisMonth: number;
}

export interface FeatureUsage {
  feature: string;
  usageCount: number;
  uniqueUsers: number;
  trend: number;
}

export interface ActivityLog {
  id: string;
  userId: number;
  userName: string;
  companyId: number;
  companyName: string;
  action: string;
  details: string;
  ipAddress: string;
  timestamp: Date;
}

export interface Estimate {
  id: string;
  clientId?: number;
  clientName: string;
  clientEmail: string;
  items: EstimateItem[];
  subtotal: number;
  tax: number;
  total: number;
  status: 'draft' | 'sent' | 'accepted' | 'rejected' | 'expired';
  validUntil: Date;
  createdAt: Date;
  createdBy: string;
  notes?: string;
}

export interface EstimateItem {
  description: string;
  quantity: number;
  unitPrice: number;
  total: number;
}

export interface MaintenanceMode {
  enabled: boolean;
  pages: string[];
  message: string;
  scheduledEnd?: Date;
}

@Injectable({
  providedIn: 'root'
})
export class AdminService {
  private apiUrl = environment.apiUrl;
  private adminUserSubject = new BehaviorSubject<AdminUser | null>(null);
  adminUser$ = this.adminUserSubject.asObservable();

  private mockClients: Client[] = [
    {
      id: 1, name: 'Transport Express', email: 'contact@transport-express.tn', phone: '+216 71 234 567',
      type: 'transport', subscriptionId: 2, subscriptionName: 'Parc GPS', maxVehicles: 50, currentVehicles: 32,
      currentUsers: 8, status: 'active', createdAt: new Date('2023-06-15'), lastActivity: new Date()
    },
    {
      id: 2, name: 'Location Tunis', email: 'info@location-tunis.tn', phone: '+216 71 345 678',
      type: 'location', subscriptionId: 3, subscriptionName: 'Parc GPS Install', maxVehicles: 100, currentVehicles: 78,
      currentUsers: 15, status: 'active', createdAt: new Date('2022-03-20'), lastActivity: new Date()
    },
    {
      id: 3, name: 'Livraison Rapide', email: 'admin@livraison-rapide.tn', phone: '+216 71 456 789',
      type: 'transport', subscriptionId: 1, subscriptionName: 'Parc Basic', maxVehicles: 20, currentVehicles: 18,
      currentUsers: 4, status: 'active', createdAt: new Date('2024-01-10'), lastActivity: new Date()
    },
    {
      id: 4, name: 'Auto Location Sfax', email: 'contact@als.tn', phone: '+216 74 123 456',
      type: 'location', subscriptionId: 2, subscriptionName: 'Parc GPS', maxVehicles: 30, currentVehicles: 25,
      currentUsers: 6, status: 'suspended', createdAt: new Date('2023-09-01'), lastActivity: new Date('2024-10-15')
    },
    {
      id: 5, name: 'Cargo Sud', email: 'info@cargo-sud.tn', phone: '+216 75 234 567',
      type: 'transport', subscriptionId: 3, subscriptionName: 'Parc GPS Install', maxVehicles: 200, currentVehicles: 156,
      currentUsers: 25, status: 'active', createdAt: new Date('2021-11-05'), lastActivity: new Date()
    },
  ];

  private mockUsers: SystemUser[] = [
    { id: 1, name: 'Ahmed Ben Ali', email: 'ahmed@transport-express.tn', companyId: 1, companyName: 'Transport Express', roles: ['admin'], permissions: ['dashboard', 'monitoring', 'vehicles', 'reports'], status: 'active', lastLogin: new Date(), createdAt: new Date('2023-06-15'), isOnline: true },
    { id: 2, name: 'Sami Trabelsi', email: 'sami@transport-express.tn', companyId: 1, companyName: 'Transport Express', roles: ['manager'], permissions: ['dashboard', 'monitoring', 'vehicles'], status: 'active', lastLogin: new Date(), createdAt: new Date('2023-07-20'), isOnline: true },
    { id: 3, name: 'Leila Mansour', email: 'leila@location-tunis.tn', companyId: 2, companyName: 'Location Tunis', roles: ['admin'], permissions: ['dashboard', 'monitoring', 'vehicles', 'reports', 'maintenance'], status: 'active', lastLogin: new Date(), createdAt: new Date('2022-03-20'), isOnline: false },
    { id: 4, name: 'Mohamed Gharbi', email: 'mohamed@livraison-rapide.tn', companyId: 3, companyName: 'Livraison Rapide', roles: ['admin'], permissions: ['dashboard', 'monitoring', 'vehicles'], status: 'active', lastLogin: new Date('2024-12-18'), createdAt: new Date('2024-01-10'), isOnline: true },
    { id: 5, name: 'Fatma Saidi', email: 'fatma@cargo-sud.tn', companyId: 5, companyName: 'Cargo Sud', roles: ['admin', 'supervisor'], permissions: ['dashboard', 'monitoring', 'vehicles', 'reports', 'geofences', 'maintenance'], status: 'active', lastLogin: new Date(), createdAt: new Date('2021-11-05'), isOnline: true },
  ];

  constructor(private http: HttpClient) {
    this.checkAdminSession();
  }

  private checkAdminSession(): void {
    const stored = localStorage.getItem('admin_user');
    if (stored) {
      try {
        this.adminUserSubject.next(JSON.parse(stored));
      } catch {
        localStorage.removeItem('admin_user');
      }
    }
  }

  login(email: string, password: string): Observable<AdminUser> {
    return this.http.post<any>(`${this.apiUrl}/auth/login`, { email, password }).pipe(
      map(response => {
        const user: AdminUser = {
          id: response.user.id.toString(),
          email: response.user.email,
          name: response.user.name,
          role: response.user.roles?.includes('admin') ? 'super_admin' : 'admin',
          permissions: response.user.permissions || ['*'],
          lastLogin: new Date()
        };
        localStorage.setItem('admin_user', JSON.stringify(user));
        localStorage.setItem('admin_token', response.token);
        this.adminUserSubject.next(user);
        return user;
      }),
      catchError(err => {
        console.error('Login error:', err);
        return throwError(() => new Error('Invalid credentials'));
      })
    );
  }

  logout(): void {
    localStorage.removeItem('admin_user');
    localStorage.removeItem('admin_token');
    this.adminUserSubject.next(null);
  }

  isAuthenticated(): boolean {
    return !!localStorage.getItem('admin_token');
  }

  getAdminUser(): AdminUser | null {
    return this.adminUserSubject.value;
  }

  getDashboardStats(): Observable<DashboardStats> {
    return of({
      totalClients: 47,
      activeClients: 42,
      totalUsers: 312,
      usersOnline: 87,
      totalVehicles: 1248,
      activeDevices: 1156,
      totalPositionsToday: 2456789,
      alertsToday: 234,
      revenueThisMonth: 125000,
      newClientsThisMonth: 5
    });
  }

  getFeatureUsage(): Observable<FeatureUsage[]> {
    return of([
      { feature: 'Real-time Monitoring', usageCount: 15234, uniqueUsers: 245, trend: 12 },
      { feature: 'Reports', usageCount: 8456, uniqueUsers: 189, trend: 8 },
      { feature: 'Geofencing', usageCount: 4521, uniqueUsers: 134, trend: 15 },
      { feature: 'Vehicle Management', usageCount: 3245, uniqueUsers: 201, trend: 5 },
      { feature: 'Maintenance', usageCount: 2134, uniqueUsers: 98, trend: -3 },
      { feature: 'Driver Scores', usageCount: 1876, uniqueUsers: 76, trend: 22 },
      { feature: 'Cost Tracking', usageCount: 1543, uniqueUsers: 89, trend: 18 },
      { feature: 'Playback', usageCount: 1234, uniqueUsers: 156, trend: 10 },
    ]);
  }

  getClients(): Observable<Client[]> {
    return of(this.mockClients);
  }

  getClient(id: number): Observable<Client | undefined> {
    return of(this.mockClients.find(c => c.id === id));
  }

  createClient(client: Partial<Client>): Observable<Client> {
    const newClient: Client = {
      id: Math.max(...this.mockClients.map(c => c.id)) + 1,
      name: client.name || '',
      email: client.email || '',
      phone: client.phone,
      type: client.type || 'transport',
      subscriptionId: client.subscriptionId,
      subscriptionName: client.subscriptionName,
      maxVehicles: client.maxVehicles || 10,
      currentVehicles: 0,
      currentUsers: 0,
      status: 'pending',
      createdAt: new Date()
    };
    this.mockClients.push(newClient);
    return of(newClient);
  }

  updateClient(id: number, updates: Partial<Client>): Observable<Client> {
    const index = this.mockClients.findIndex(c => c.id === id);
    if (index >= 0) {
      this.mockClients[index] = { ...this.mockClients[index], ...updates };
      return of(this.mockClients[index]);
    }
    return throwError(() => new Error('Client not found'));
  }

  suspendClient(id: number): Observable<void> {
    const client = this.mockClients.find(c => c.id === id);
    if (client) {
      client.status = 'suspended';
    }
    return of(void 0);
  }

  activateClient(id: number): Observable<void> {
    const client = this.mockClients.find(c => c.id === id);
    if (client) {
      client.status = 'active';
    }
    return of(void 0);
  }

  getUsers(): Observable<SystemUser[]> {
    return of(this.mockUsers);
  }

  getUsersByCompany(companyId: number): Observable<SystemUser[]> {
    return of(this.mockUsers.filter(u => u.companyId === companyId));
  }

  updateUserPermissions(userId: number, permissions: string[]): Observable<void> {
    const user = this.mockUsers.find(u => u.id === userId);
    if (user) {
      user.permissions = permissions;
    }
    return of(void 0);
  }

  suspendUser(userId: number): Observable<void> {
    const user = this.mockUsers.find(u => u.id === userId);
    if (user) {
      user.status = 'suspended';
    }
    return of(void 0);
  }

  activateUser(userId: number): Observable<void> {
    const user = this.mockUsers.find(u => u.id === userId);
    if (user) {
      user.status = 'active';
    }
    return of(void 0);
  }

  getServiceHealth(): Observable<ServiceHealth[]> {
    return of([
      { name: 'GIS API', status: 'healthy', responseTime: 45, lastCheck: new Date(), uptime: 99.98, details: { version: '1.0.0', requests: 12456 } },
      { name: 'GPS Ingest Service', status: 'healthy', responseTime: 12, lastCheck: new Date(), uptime: 99.99, details: { connections: 847, messagesPerSec: 1234 } },
      { name: 'PostgreSQL', status: 'healthy', responseTime: 8, lastCheck: new Date(), uptime: 100, details: { connections: 45, size: '12.4 GB' } },
      { name: 'RabbitMQ', status: 'healthy', responseTime: 15, lastCheck: new Date(), uptime: 99.95, details: { queues: 12, messages: 234 } },
      { name: 'Frontend', status: 'healthy', responseTime: 120, lastCheck: new Date(), uptime: 99.99, details: { version: '1.0.0' } },
    ]);
  }

  getActivityLogs(limit: number = 50): Observable<ActivityLog[]> {
    const actions = ['login', 'logout', 'view_vehicle', 'create_geofence', 'generate_report', 'update_settings', 'add_maintenance'];
    const logs: ActivityLog[] = [];
    for (let i = 0; i < limit; i++) {
      const user = this.mockUsers[Math.floor(Math.random() * this.mockUsers.length)];
      logs.push({
        id: `log-${i}`,
        userId: user.id,
        userName: user.name,
        companyId: user.companyId,
        companyName: user.companyName,
        action: actions[Math.floor(Math.random() * actions.length)],
        details: 'Action performed successfully',
        ipAddress: `192.168.1.${Math.floor(Math.random() * 255)}`,
        timestamp: new Date(Date.now() - Math.random() * 86400000 * 7)
      });
    }
    return of(logs.sort((a, b) => b.timestamp.getTime() - a.timestamp.getTime()));
  }

  getMaintenanceMode(): Observable<MaintenanceMode> {
    const stored = localStorage.getItem('maintenance_mode');
    if (stored) {
      return of(JSON.parse(stored));
    }
    return of({ enabled: false, pages: [], message: '' });
  }

  setMaintenanceMode(mode: MaintenanceMode): Observable<void> {
    localStorage.setItem('maintenance_mode', JSON.stringify(mode));
    return of(void 0);
  }

  getEstimates(): Observable<Estimate[]> {
    return of([
      {
        id: 'EST-001',
        clientName: 'New Transport Co',
        clientEmail: 'contact@newtransport.tn',
        items: [
          { description: 'GPS Tracking Subscription - 50 vehicles', quantity: 1, unitPrice: 2500, total: 2500 },
          { description: 'GPS Device Installation', quantity: 50, unitPrice: 150, total: 7500 },
        ],
        subtotal: 10000,
        tax: 1900,
        total: 11900,
        status: 'sent',
        validUntil: new Date(Date.now() + 30 * 86400000),
        createdAt: new Date('2024-12-15'),
        createdBy: 'Super Admin'
      },
      {
        id: 'EST-002',
        clientId: 3,
        clientName: 'Livraison Rapide',
        clientEmail: 'admin@livraison-rapide.tn',
        items: [
          { description: 'Upgrade to Parc GPS Install', quantity: 1, unitPrice: 1500, total: 1500 },
          { description: 'Additional 10 GPS Devices', quantity: 10, unitPrice: 200, total: 2000 },
        ],
        subtotal: 3500,
        tax: 665,
        total: 4165,
        status: 'accepted',
        validUntil: new Date(Date.now() + 15 * 86400000),
        createdAt: new Date('2024-12-10'),
        createdBy: 'Super Admin'
      }
    ]);
  }

  createEstimate(estimate: Partial<Estimate>): Observable<Estimate> {
    const newEstimate: Estimate = {
      id: `EST-${String(Math.floor(Math.random() * 1000)).padStart(3, '0')}`,
      clientId: estimate.clientId,
      clientName: estimate.clientName || '',
      clientEmail: estimate.clientEmail || '',
      items: estimate.items || [],
      subtotal: estimate.subtotal || 0,
      tax: estimate.tax || 0,
      total: estimate.total || 0,
      status: 'draft',
      validUntil: estimate.validUntil || new Date(Date.now() + 30 * 86400000),
      createdAt: new Date(),
      createdBy: this.adminUserSubject.value?.name || 'Admin',
      notes: estimate.notes
    };
    return of(newEstimate);
  }

  getAllPages(): string[] {
    return [
      'dashboard',
      'monitoring',
      'vehicles',
      'employees',
      'gps-devices',
      'maintenance',
      'costs',
      'reports',
      'geofences',
      'notifications',
      'settings',
      'users'
    ];
  }

  getSubscriptions(): Observable<any[]> {
    return of([
      { id: 1, name: 'Parc Basic', price: 500, maxVehicles: 20, features: ['Vehicle Management', 'Basic Reports'] },
      { id: 2, name: 'Parc GPS', price: 1500, maxVehicles: 100, features: ['Vehicle Management', 'GPS Tracking', 'Advanced Reports', 'Geofencing'] },
      { id: 3, name: 'Parc GPS Install', price: 3000, maxVehicles: 500, features: ['Vehicle Management', 'GPS Tracking', 'Advanced Reports', 'Geofencing', 'Installation Service', 'Priority Support'] },
    ]);
  }
}
