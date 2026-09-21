import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Platform, ToastController } from '@ionic/angular';
import { of } from 'rxjs';
import { PushNotificationService } from './push-notification.service';
import { ApiService } from './api.service';
import { AuthService } from './auth.service';
import { ImmobilizationApprovalService } from './immobilization-approval.service';

describe('PushNotificationService.unregister (déconnexion)', () => {
  let api: { unregisterDeviceToken: jasmine.Spy; registerDeviceToken: jasmine.Spy };
  let service: PushNotificationService;

  beforeEach(() => {
    api = {
      unregisterDeviceToken: jasmine.createSpy('unregisterDeviceToken').and.returnValue(of({})),
      registerDeviceToken: jasmine.createSpy('registerDeviceToken').and.returnValue(of({}))
    };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        PushNotificationService,
        { provide: Platform, useValue: { is: () => false } },
        { provide: ApiService, useValue: api },
        { provide: AuthService, useValue: { getCurrentUserSync: () => ({ id: '42' }), isDriver: () => true } },
        { provide: ImmobilizationApprovalService, useValue: {} },
        { provide: ToastController, useValue: {} }
      ]
    });
    service = TestBed.inject(PushNotificationService);
  });

  it('désinscrit le jeton FCM connu même si son inscription a échoué pendant cette session', async () => {
    // Démarrage hors ligne : le POST /api/devicetokens a échoué, mais le serveur garde la
    // ligne active des sessions précédentes pour ce même compte.
    (service as any).currentToken = 'fcm-abc';
    (service as any).registeredUserId = null;

    await service.unregister();

    expect(api.unregisterDeviceToken).toHaveBeenCalledOnceWith('fcm-abc');
    expect((service as any).registeredUserId).toBeNull();
  });

  it('aucun jeton FCM connu (push refusé) : aucun appel', async () => {
    await service.unregister();
    expect(api.unregisterDeviceToken).not.toHaveBeenCalled();
  });
});
