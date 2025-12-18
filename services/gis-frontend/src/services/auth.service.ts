import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, BehaviorSubject, of, tap, map, catchError } from 'rxjs';

export interface AuthUser {
  id: string;
  name: string;
  email: string;
  phone?: string;
  roles: string[];
  permissions: string[];
  companyId: string;
  companyName: string;
}

export interface AuthResponse {
  token: string;
  refreshToken: string;
  user: {
    id: number;
    name: string;
    email: string;
    phone?: string;
    roles: string[];
    permissions: string[];
    companyId: number;
    companyName: string;
  };
}

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly API_URL = this.getApiUrl();
  private currentUser$ = new BehaviorSubject<AuthUser | null>(null);

  constructor(private http: HttpClient) {
    this.loadStoredAuth();
  }

  private getApiUrl(): string {
    // Always use relative path - nginx proxies to the backend
    return '/api';
  }

  private loadStoredAuth() {
    const token = localStorage.getItem('auth_token');
    const userData = localStorage.getItem('auth_user');
    if (token && userData) {
      try {
        const parsed = JSON.parse(userData);
        this.currentUser$.next({
          id: parsed.id?.toString() || '',
          name: parsed.name || '',
          email: parsed.email || '',
          phone: parsed.phone,
          roles: parsed.roles || [],
          permissions: parsed.permissions || [],
          companyId: parsed.companyId?.toString() || '',
          companyName: parsed.companyName || ''
        });
      } catch (e) {
        console.error('Error loading stored auth:', e);
      }
    }
  }

  login(email: string, password: string): Observable<AuthUser | null> {
    // Mock user check - admin@test.com with password "admin" (case insensitive)
    if (email === 'admin@test.com' && password.toLowerCase() === 'admin') {
      console.log('AuthService.login - Using mock user');
      const mockUser: AuthUser = {
        id: '1',
        name: 'Admin Test',
        email: 'admin@test.com',
        roles: ['admin'],
        permissions: ['all'],
        companyId: '1',
        companyName: 'Demo Company'
      };
      const mockResponse = {
        token: 'mock-jwt-token-for-testing',
        refreshToken: 'mock-refresh-token',
        user: mockUser
      };
      localStorage.setItem('auth_token', mockResponse.token);
      localStorage.setItem('auth_user', JSON.stringify(mockUser));
      this.currentUser$.next(mockUser);
      return of(mockUser);
    }

    const url = `${this.API_URL}/auth/login`;
    console.log('AuthService.login - URL:', url);
    console.log('AuthService.login - Payload:', { email, password: '***' });
    
    return this.http.post<AuthResponse>(url, { email, password }).pipe(
      tap(response => {
        console.log('AuthService.login - Response received:', response);
        localStorage.setItem('auth_token', response.token);
        localStorage.setItem('auth_user', JSON.stringify(response.user));
      }),
      map(response => {
        const user: AuthUser = {
          id: response.user.id.toString(),
          name: response.user.name,
          email: response.user.email,
          phone: response.user.phone,
          roles: response.user.roles || [],
          permissions: response.user.permissions || [],
          companyId: response.user.companyId.toString(),
          companyName: response.user.companyName
        };
        this.currentUser$.next(user);
        console.log('AuthService.login - User mapped:', user);
        return user;
      }),
      catchError(err => {
        console.error('AuthService.login - Error:', err);
        return of(null);
      })
    );
  }

  register(name: string, email: string, password: string, companyName: string, phone?: string): Observable<AuthUser | null> {
    return this.http.post<AuthResponse>(`${this.API_URL}/auth/register`, { 
      name, email, password, companyName, phone 
    }).pipe(
      tap(response => {
        localStorage.setItem('auth_token', response.token);
        localStorage.setItem('auth_user', JSON.stringify(response.user));
      }),
      map(response => {
        const user: AuthUser = {
          id: response.user.id.toString(),
          name: response.user.name,
          email: response.user.email,
          phone: response.user.phone,
          roles: response.user.roles || [],
          permissions: response.user.permissions || [],
          companyId: response.user.companyId.toString(),
          companyName: response.user.companyName
        };
        this.currentUser$.next(user);
        return user;
      }),
      catchError(err => {
        console.error('Register failed:', err);
        return of(null);
      })
    );
  }

  logout() {
    localStorage.removeItem('auth_token');
    localStorage.removeItem('auth_user');
    this.currentUser$.next(null);
  }

  isAuthenticated(): boolean {
    return !!localStorage.getItem('auth_token');
  }

  getCurrentUser(): Observable<AuthUser | null> {
    return this.currentUser$.asObservable();
  }

  getCurrentUserSync(): AuthUser | null {
    return this.currentUser$.value;
  }

  getToken(): string | null {
    return localStorage.getItem('auth_token');
  }
}
