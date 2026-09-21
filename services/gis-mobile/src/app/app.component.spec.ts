import { CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';

import { AppComponent } from './app.component';
import { ImmobilizationApprovalService } from './core/services/immobilization-approval.service';
import { PushNotificationService } from './core/services/push-notification.service';
import { SignalRService } from './core/services/signalr.service';
import { AuthService, AuthUser } from './core/services/auth.service';
import { TourTrackingService } from './core/services/tour-tracking.service';
import { DriverDeclarationsService } from './core/services/driver-declarations.service';

describe('AppComponent', () => {
  let user$: BehaviorSubject<AuthUser | null>;
  let signalr: jasmine.SpyObj<SignalRService>;
  let push: jasmine.SpyObj<PushNotificationService>;
  let tracking: jasmine.SpyObj<TourTrackingService>;
  let declarations: jasmine.SpyObj<DriverDeclarationsService>;

  const settle = () => new Promise(r => setTimeout(r, 10));
  const user = (accountType: 'staff' | 'driver', id = '1') => ({ id, name: 'X', accountType } as AuthUser);

  beforeEach(async () => {
    user$ = new BehaviorSubject<AuthUser | null>(null);
    signalr = jasmine.createSpyObj('SignalRService', ['startConnection', 'stopConnection']);
    signalr.startConnection.and.resolveTo();
    signalr.stopConnection.and.resolveTo();
    push = jasmine.createSpyObj('PushNotificationService', ['init']);
    push.init.and.resolveTo();
    tracking = jasmine.createSpyObj('TourTrackingService', ['resume', 'stop']);
    tracking.resume.and.resolveTo();
    tracking.stop.and.resolveTo();
    declarations = jasmine.createSpyObj('DriverDeclarationsService', ['armAutoReplay', 'disarmAutoReplay', 'replay']);
    declarations.replay.and.resolveTo();

    await TestBed.configureTestingModule({
      declarations: [AppComponent],
      schemas: [CUSTOM_ELEMENTS_SCHEMA],
      providers: [
        provideRouter([]),
        { provide: ImmobilizationApprovalService, useValue: { init: () => {} } },
        { provide: PushNotificationService, useValue: push },
        { provide: SignalRService, useValue: signalr },
        { provide: AuthService, useValue: { getCurrentUser: () => user$.asObservable() } },
        { provide: TourTrackingService, useValue: tracking },
        { provide: DriverDeclarationsService, useValue: declarations }
      ]
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(AppComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('gestionnaire : démarre SignalR et le push', async () => {
    TestBed.createComponent(AppComponent);
    user$.next(user('staff'));
    await settle();
    expect(signalr.startConnection).toHaveBeenCalled();
    expect(push.init).toHaveBeenCalled();
    expect(tracking.resume).not.toHaveBeenCalled();
  });

  it('chauffeur : JAMAIS SignalR (refusé par le serveur) ; push, rejeu de la file et reprise du suivi', async () => {
    TestBed.createComponent(AppComponent);
    user$.next(user('driver'));
    await settle();
    expect(signalr.startConnection).not.toHaveBeenCalled();
    expect(push.init).toHaveBeenCalled();
    expect(declarations.armAutoReplay).toHaveBeenCalled();
    expect(declarations.replay).toHaveBeenCalled();
    expect(tracking.resume).toHaveBeenCalled();
  });

  it('le null initial (session pas encore relue) n\'efface pas un suivi persisté', async () => {
    TestBed.createComponent(AppComponent);
    await settle();
    expect(tracking.stop).not.toHaveBeenCalled();
  });

  it('session perdue : le suivi s\'arrête en GARDANT état et file du compte, rejeu désarmé, retour à la connexion', async () => {
    const nav = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    TestBed.createComponent(AppComponent);
    user$.next(user('driver'));
    await settle();
    user$.next(null);
    await settle();
    expect(tracking.stop).toHaveBeenCalledOnceWith({ keep: true });
    expect(declarations.disarmAutoReplay).toHaveBeenCalled();
    expect(nav).toHaveBeenCalledWith(['/login'], { replaceUrl: true });
  });

  it('même compte converti chauffeur (appris au rafraîchissement) : renvoyé vers « Mes tournées »', async () => {
    const nav = spyOn(TestBed.inject(Router), 'navigateByUrl').and.resolveTo(true);
    TestBed.createComponent(AppComponent);
    user$.next(user('staff', '5'));
    await settle();
    expect(nav).not.toHaveBeenCalled();
    user$.next(user('driver', '5'));
    await settle();
    expect(nav).toHaveBeenCalledWith('/driver/tours', { replaceUrl: true });
  });

  it('un AUTRE compte qui se connecte n\'est pas un changement de type', async () => {
    const nav = spyOn(TestBed.inject(Router), 'navigateByUrl').and.resolveTo(true);
    TestBed.createComponent(AppComponent);
    user$.next(user('staff', '5'));
    await settle();
    user$.next(null);
    user$.next(user('driver', '6'));
    await settle();
    // (navigate(['/login']) passe lui aussi par navigateByUrl : seule la bascule d'espace compte.)
    expect(nav.calls.allArgs().some(args => String(args[0]) === '/driver/tours')).toBeFalse();
  });
});
