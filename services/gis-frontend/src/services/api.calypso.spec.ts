import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { ApiService } from './api.service';

/**
 * Calypso 6 patch — dedicated spec (item I, self-service password change).
 * Uses the Angular 21 provider-based test setup (provideHttpClient + provideHttpClientTesting)
 * rather than the deprecated HttpClientTestingModule.
 */
describe('ApiService - changeMyPassword (Calypso I)', () => {
  let service: ApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    localStorage.setItem('auth_token', 'mock-token');
    TestBed.configureTestingModule({
      providers: [
        ApiService,
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });
    service = TestBed.inject(ApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.removeItem('auth_token');
  });

  it('calls PUT /api/users/me/password with the correct payload + auth header', () => {
    let completed = false;
    service
      .changeMyPassword({ currentPassword: 'oldPass1', newPassword: 'newPass2' })
      .subscribe(() => (completed = true));

    const req = httpMock.expectOne('/api/users/me/password');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ currentPassword: 'oldPass1', newPassword: 'newPass2' });
    expect(req.request.headers.get('Authorization')).toBe('Bearer mock-token');

    req.flush(null);
    expect(completed).toBe(true);
  });

  it('propagates a 400 error (wrong current password) to the subscriber', () => {
    let caughtStatus = 0;
    service
      .changeMyPassword({ currentPassword: 'wrong', newPassword: 'newPass2' })
      .subscribe({
        next: () => {},
        error: err => (caughtStatus = err.status)
      });

    const req = httpMock.expectOne('/api/users/me/password');
    req.flush({ message: 'Mot de passe actuel incorrect' }, { status: 400, statusText: 'Bad Request' });

    expect(caughtStatus).toBe(400);
  });
});
