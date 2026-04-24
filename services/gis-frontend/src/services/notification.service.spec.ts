import { Subject, BehaviorSubject } from 'rxjs';
import { HttpClient } from '@angular/common/http';

import { NotificationService } from './notification.service';
import { SignalRService, SignalRNotification } from './signalr.service';

/**
 * Unit tests for Calypso bell notification dedup (item I2).
 * Verifies that a SignalR notification with an id already present in the list
 * is silently ignored, so the bell doesn't show duplicates after reconnections.
 *
 * Bypass TestBed: the service subscribes to SignalR in its constructor, so we
 * construct it manually with stubs to avoid Angular 21 module-overrides quirks.
 */
describe('NotificationService — dedup (Calypso I2)', () => {
  let service: NotificationService;
  let signalrStub: { notification$: Subject<SignalRNotification>; unreadCount$: BehaviorSubject<number> };

  const makeNotif = (id: number, overrides: Partial<SignalRNotification> = {}): SignalRNotification => ({
    id,
    type: 'accident_detected',
    title: 'Accident détecté',
    message: 'Véhicule Peugeot 208',
    priority: 'high',
    createdAt: '2026-04-24T10:00:00Z',
    ...overrides
  });

  beforeEach(() => {
    signalrStub = {
      notification$: new Subject<SignalRNotification>(),
      unreadCount$: new BehaviorSubject<number>(0)
    };
    const httpStub = {} as HttpClient;
    service = new NotificationService(httpStub, signalrStub as unknown as SignalRService);
  });

  afterEach(() => {
    service.ngOnDestroy();
  });

  it('prepends a first SignalR notification to the list', () => {
    signalrStub.notification$.next(makeNotif(1));

    const list = service.notifications$.value;
    expect(list.length).toBe(1);
    expect(list[0].id).toBe(1);
    expect(service.unreadCount$.value).toBe(1);
  });

  it('ignores a duplicate notification (same id) — no prepend, no counter bump', () => {
    signalrStub.notification$.next(makeNotif(42));
    signalrStub.notification$.next(makeNotif(42, { title: 'Duplicate retransmit' }));

    const list = service.notifications$.value;
    expect(list.length).toBe(1);
    expect(list[0].title).toBe('Accident détecté');
    expect(service.unreadCount$.value).toBe(1);
  });

  it('keeps distinct ids in most-recent-first order', () => {
    signalrStub.notification$.next(makeNotif(1, { title: 'One' }));
    signalrStub.notification$.next(makeNotif(2, { title: 'Two' }));
    signalrStub.notification$.next(makeNotif(3, { title: 'Three' }));

    const ids = service.notifications$.value.map(n => n.id);
    expect(ids).toEqual([3, 2, 1]);
    expect(service.unreadCount$.value).toBe(3);
  });

  it('does not emit newNotification$ for duplicates', () => {
    const emitted: number[] = [];
    service.newNotification$.subscribe(n => emitted.push(n.id));

    signalrStub.notification$.next(makeNotif(7));
    signalrStub.notification$.next(makeNotif(7));
    signalrStub.notification$.next(makeNotif(8));

    expect(emitted).toEqual([7, 8]);
  });

  it('propagates SignalR unreadCount$ to local unreadCount$', () => {
    signalrStub.unreadCount$.next(12);
    expect(service.unreadCount$.value).toBe(12);
  });
});
