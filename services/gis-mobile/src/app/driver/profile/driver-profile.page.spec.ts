import { NgZone } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { BehaviorSubject, Subject, of } from 'rxjs';
import { DriverProfilePage } from './driver-profile.page';

describe('DriverProfilePage (déconnexion volontaire)', () => {
  let log: string[];
  let pending: number;
  let alerts: any[];

  function page(): DriverProfilePage {
    const tracking = {
      state$: new BehaviorSubject(null), sensorError$: new BehaviorSubject(null), pendingCount: 0,
      stop: async (o: any) => { log.push(`tracking.stop:${JSON.stringify(o)}`); }
    };
    const declarations = {
      pendingCount$: new Subject<number>(),
      get pendingCount() { return pending; },
      replay: async () => { log.push('declarations.replay'); },
      discardForCurrentUser: async () => { log.push('declarations.discard'); },
      disarmAutoReplay: () => { log.push('declarations.disarm'); }
    };
    const push = { unregister: async () => { log.push('push.unregister'); } };
    const auth = { getCurrentUserSync: () => ({ id: '42', name: 'Sami' }), logout: async () => { log.push('auth.logout'); } };
    const api = { getDriverMe: () => of(null) };
    const router = { navigate: (cmd: any[]) => { log.push(`navigate:${cmd[0]}`); return Promise.resolve(true); } };
    const alertCtrl = { create: async (opts: any) => { alerts.push(opts); return { present: async () => {} }; } };
    const p = new DriverProfilePage(api as any, auth as any, push as any, tracking as any, declarations as any,
      router as any, alertCtrl as any, TestBed.inject(NgZone));
    p.ngOnInit();
    return p;
  }

  beforeEach(() => { log = []; pending = 0; alerts = []; });

  it('déclarations non envoyées : on tente d\'abord un envoi, puis on PRÉVIENT qu\'elles seront perdues', async () => {
    pending = 2;
    const p = page();
    await p.confirmLogout();
    expect(log[0]).toBe('declarations.replay');
    expect(alerts[0].message).toContain('2 déclaration(s)');
    expect(alerts[0].message).toContain('PERDUES');
  });

  it('rien en attente : simple confirmation', async () => {
    const p = page();
    await p.confirmLogout();
    expect(alerts[0].message).toBe('Voulez-vous vraiment vous déconnecter ?');
  });

  it('ordre de la déconnexion : envois pendant que le jeton existe, PUIS files du compte vidées, PUIS session effacée', async () => {
    const p = page();
    await p.doLogout();
    expect(log).toEqual([
      'declarations.replay',
      'tracking.stop:{"flush":true}',
      'push.unregister',
      'declarations.discard',
      'declarations.disarm',
      'auth.logout',
      'navigate:/login'
    ]);
  });
});
