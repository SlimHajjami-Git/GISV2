import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { Router, convertToParamMap } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { of, Subject, throwError } from 'rxjs';
import { UserManagementComponent } from './user-management.component';
import { ApiService } from '../services/api.service';
import { ToastService } from '../services/toast.service';
import { AUTO_DRIVER_LINK_LABEL } from './driver-account-link.helpers';

/**
 * Écran Utilisateurs — sélecteur « Fiche chauffeur à relier » (relecture du 21/09/2026).
 * Sans lui, le serveur ne retrouvait la fiche que par e-mail : une fiche sans e-mail
 * était ratée, une seconde fiche naissait sans véhicule ni tournées, et l'envoi de
 * tournée restait refusé. Tenu ici : seules les fiches libres (et celle déjà reliée à
 * CE compte) sont proposées, le choix part en driverId au POST comme au PUT, et un
 * refus 404/409 du serveur s'affiche sous le sélecteur, formulaire ouvert.
 */
describe('UserManagementComponent — sélecteur « Fiche chauffeur à relier »', () => {
  let component: UserManagementComponent;
  let fixture: any;
  let api: ApiService;
  let toast: ToastService;

  const utilisateurs = [
    {
      id: 10, name: 'Amel Gestion', email: 'amel@transporttest.tn', roleId: 2,
      isCompanyAdmin: false, status: 'active', createdAt: '2026-09-01T00:00:00Z',
      accountType: 'staff', assignedVehicleIds: [], userPermissions: { canMonitoring: true }
    },
    {
      id: 11, name: 'Karim Chauffeur', email: 'karim@transporttest.tn', roleId: 2,
      isCompanyAdmin: false, status: 'active', createdAt: '2026-09-02T00:00:00Z', accountType: 'driver'
    }
  ];

  // GET /api/drivers : 30 et 33 libres, 31 reliée à Karim (11), 32 reliée à un autre compte.
  const fiches = [
    { id: 30, firstName: 'Ali', lastName: 'Ben Salah', email: null, status: 'active', userId: null, accountStatus: null },
    { id: 31, firstName: 'Karim', lastName: 'Chauffeur', email: 'karim@transporttest.tn', status: 'active', userId: 11, accountStatus: 'active' },
    { id: 32, firstName: 'Zied', lastName: 'Autre', email: 'zied@transporttest.tn', status: 'active', userId: 99, accountStatus: 'active' },
    { id: 33, firstName: 'Bilel', lastName: 'Libre', email: 'bilel@transporttest.tn', status: 'active', userId: null, accountStatus: null }
  ];

  beforeEach(async () => {
    jest.restoreAllMocks();
    localStorage.clear();
    localStorage.setItem('auth_token', 'jeton-de-test');
    // Le service d'export PDF précharge le logo par fetch() — absent de jsdom.
    (globalThis as any).fetch = jest.fn(() => Promise.resolve({ ok: false }));

    await TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule, FormsModule, UserManagementComponent],
      providers: [ApiService]
    }).compileComponents();

    fixture = TestBed.createComponent(UserManagementComponent);
    component = fixture.componentInstance;
    api = TestBed.inject(ApiService);
    toast = TestBed.inject(ToastService);

    jest.spyOn(api, 'getCurrentSubscription').mockReturnValue(of({ subscriptionType: null }) as any);
    jest.spyOn(api, 'getRoles').mockReturnValue(of([
      { id: 1, name: 'Administrateur', isSystem: true, isCompanyAdmin: true },
      { id: 2, name: 'Utilisateur', isSystem: true, isCompanyAdmin: false }
    ]) as any);
    jest.spyOn(api, 'getUsers').mockReturnValue(of(utilisateurs) as any);
    jest.spyOn(api, 'getVehicles').mockReturnValue(of([]) as any);
    jest.spyOn(api, 'getDrivers').mockReturnValue(of(fiches) as any);
    jest.spyOn(toast, 'error').mockImplementation(() => {});
    jest.spyOn(toast, 'success').mockImplementation(() => {});

    fixture.detectChanges();
  });

  afterEach(() => {
    localStorage.clear();
  });

  /**
   * Rend le formulaire, puis laisse ngModel écrire la valeur du <select> (il le fait
   * dans une micro-tâche). Pas de whenStable : la mise en page de l'écran garde des
   * minuteries actives, la zone n'est jamais stable.
   */
  const rendre = async () => {
    fixture.detectChanges();
    await new Promise(r => setTimeout(r, 0));
    fixture.detectChanges();
  };
  const selecteur = (): HTMLSelectElement | null => fixture.nativeElement.querySelector('select.driver-link-select');
  const libellesOptions = (): string[] => Array.from(selecteur()!.options).map(o => o.textContent!.trim());
  const cocherChauffeur = () => {
    component.userForm.isDriverAccount = true;
    component.onDriverAccountToggle();
  };
  const remplirIdentite = () => {
    component.userForm.firstName = 'Ali';
    component.userForm.lastName = 'Ben Salah';
    component.userForm.email = 'ali@transporttest.tn';
    component.userForm.password = 'Secret@2026';
  };

  it('absent tant que la case « Chauffeur » n’est pas cochée', async () => {
    component.openUserModal();
    await rendre();
    expect(selecteur()).toBeNull();
  });

  it('création : option « automatique » par défaut, puis les seules fiches libres', async () => {
    component.openUserModal();
    cocherChauffeur();
    await rendre();

    expect(selecteur()).not.toBeNull();
    expect(libellesOptions()).toEqual([
      AUTO_DRIVER_LINK_LABEL,
      'Ali Ben Salah',
      'Bilel Libre — bilel@transporttest.tn'
    ]);
    expect(selecteur()!.selectedIndex).toBe(0);
    expect(component.linkedDriverId).toBeNull();
  });

  it('création : la fiche choisie part en driverId dans le POST', () => {
    const creation = jest.spyOn(api, 'createUser').mockReturnValue(of({ id: 12 }) as any);
    component.openUserModal();
    remplirIdentite();
    cocherChauffeur();
    component.linkedDriverId = 30;

    component.saveUser();

    expect(creation.mock.calls[0][0].isDriverAccount).toBe(true);
    expect(creation.mock.calls[0][0].driverId).toBe(30);
  });

  it('modification d’un chauffeur : sa fiche est proposée, présélectionnée, et repart dans le PUT', async () => {
    const modification = jest.spyOn(api, 'updateUser').mockReturnValue(of(void 0) as any);

    component.openUserModal(utilisateurs[1] as any);
    await rendre();

    expect(component.linkedDriverId).toBe(31);
    expect(libellesOptions()).toContain('Karim Chauffeur — karim@transporttest.tn (reliée à ce compte)');
    expect(libellesOptions().some(l => l.startsWith('Zied'))).toBe(false);
    expect(selecteur()!.selectedOptions[0].textContent).toContain('Karim Chauffeur');

    component.saveUser();
    expect(modification.mock.calls[0][1].driverId).toBe(31);
  });

  it('modification d’un compte ordinaire que l’on passe chauffeur : la fiche choisie part dans le PUT', () => {
    const modification = jest.spyOn(api, 'updateUser').mockReturnValue(of(void 0) as any);

    component.openUserModal(utilisateurs[0] as any);
    cocherChauffeur();
    // Amel n'a pas de fiche reliée : rien n'est présélectionné.
    expect(component.linkedDriverId).toBeNull();
    component.linkedDriverId = 33;
    component.saveUser();

    expect(modification.mock.calls[0][1].isDriverAccount).toBe(true);
    expect(modification.mock.calls[0][1].driverId).toBe(33);
  });

  it('un compte ordinaire n’envoie jamais de driverId, même après avoir choisi puis décoché', () => {
    const modification = jest.spyOn(api, 'updateUser').mockReturnValue(of(void 0) as any);

    component.openUserModal(utilisateurs[0] as any);
    cocherChauffeur();
    component.linkedDriverId = 30;
    component.userForm.isDriverAccount = false;
    component.onDriverAccountToggle();
    component.saveUser();

    expect(modification.mock.calls[0][1].isDriverAccount).toBe(false);
    expect(modification.mock.calls[0][1].driverId).toBeNull();
  });

  it('réponse tardive des fiches d’un formulaire fermé : n’impose pas la fiche d’un autre compte', () => {
    const modification = jest.spyOn(api, 'updateUser').mockReturnValue(of(void 0) as any);
    const enVol = new Subject<any[]>();
    (api.getDrivers as unknown as jest.Mock).mockReturnValueOnce(enVol.asObservable());

    component.openUserModal(utilisateurs[1] as any);   // Karim : chargement en vol
    component.closeUserModal();
    component.openUserModal(utilisateurs[0] as any);   // Amel, compte ordinaire
    enVol.next(fiches);                                 // la réponse de Karim arrive

    expect(component.linkedDriverId).toBeNull();
    cocherChauffeur();
    component.saveUser();
    // Sinon 31 (la fiche de Karim) partait pour Amel : 409 « déjà reliée à un autre compte ».
    expect(modification.mock.calls[0][1].driverId).toBeNull();
  });

  it('409 « fiche déjà reliée » : message du serveur sous le sélecteur, formulaire ouvert, liste rechargée', async () => {
    const message = "Cette fiche chauffeur est déjà reliée à un autre compte : ouvrez ce compte dans Utilisateurs plutôt que d'en créer un second.";
    jest.spyOn(api, 'createUser').mockReturnValue(throwError(() => ({ status: 409, error: { message } })) as any);
    const chargement = api.getDrivers as unknown as jest.Mock;

    component.openUserModal();
    remplirIdentite();
    cocherChauffeur();
    component.linkedDriverId = 30;
    const appelsAvant = chargement.mock.calls.length;

    component.saveUser();
    await rendre();

    expect(component.showUserModal).toBe(true);
    expect(toast.error).toHaveBeenCalledWith('Erreur', message);
    const encart: HTMLElement | null = fixture.nativeElement.querySelector('.driver-link-error');
    expect(encart).not.toBeNull();
    expect(encart!.textContent).toContain('déjà reliée à un autre compte');
    expect(chargement.mock.calls.length).toBe(appelsAvant + 1);
  });

  it('404 « fiche introuvable » en modification : message du serveur sous le sélecteur', async () => {
    jest.spyOn(api, 'updateUser').mockReturnValue(
      throwError(() => ({ status: 404, error: { message: 'Fiche chauffeur introuvable dans votre société.' } })) as any
    );

    component.openUserModal(utilisateurs[0] as any);
    cocherChauffeur();
    component.linkedDriverId = 30;
    component.saveUser();
    await rendre();

    expect(component.showUserModal).toBe(true);
    expect(fixture.nativeElement.querySelector('.driver-link-error')!.textContent)
      .toContain('Fiche chauffeur introuvable dans votre société.');
  });

  it('409 « e-mail déjà utilisé » : toast seulement, rien sous le sélecteur', async () => {
    jest.spyOn(api, 'createUser').mockReturnValue(
      throwError(() => ({ status: 409, error: { message: 'Cet email est déjà utilisé' } })) as any
    );

    component.openUserModal();
    remplirIdentite();
    cocherChauffeur();
    component.saveUser();
    await rendre();

    expect(toast.error).toHaveBeenCalledWith('Erreur', 'Cet email est déjà utilisé');
    expect(fixture.nativeElement.querySelector('.driver-link-error')).toBeNull();
  });
});

/**
 * « Créer son compte » depuis l'écran Chauffeurs : la fiche du lien (?driverId=…) est
 * celle qui apparaît choisie dans le sélecteur — l'admin voit ce qui partira.
 */
describe('UserManagementComponent — « Créer son compte » présélectionne la fiche dans le sélecteur', () => {
  let component: UserManagementComponent;
  let fixture: any;
  let api: ApiService;

  beforeEach(async () => {
    jest.restoreAllMocks();
    localStorage.clear();
    localStorage.setItem('auth_token', 'jeton-de-test');
    (globalThis as any).fetch = jest.fn(() => Promise.resolve({ ok: false }));

    await TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule, FormsModule, UserManagementComponent],
      providers: [ApiService]
    }).compileComponents();

    fixture = TestBed.createComponent(UserManagementComponent);
    component = fixture.componentInstance;
    api = TestBed.inject(ApiService);

    jest.spyOn(api, 'getCurrentSubscription').mockReturnValue(of({ subscriptionType: null }) as any);
    jest.spyOn(api, 'getRoles').mockReturnValue(of([{ id: 2, name: 'Utilisateur', isSystem: true, isCompanyAdmin: false }]) as any);
    jest.spyOn(api, 'getUsers').mockReturnValue(of([]) as any);
    jest.spyOn(api, 'getVehicles').mockReturnValue(of([]) as any);
    jest.spyOn(api, 'getDrivers').mockReturnValue(of([
      { id: 30, firstName: 'Ali', lastName: 'Ben Salah', email: null, status: 'active', userId: null },
      { id: 33, firstName: 'Bilel', lastName: 'Libre', email: null, status: 'active', userId: null }
    ]) as any);
    jest.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    (component as any).route = {
      snapshot: { queryParamMap: convertToParamMap({ nouveau: 'chauffeur', driverId: '30', prenom: 'Ali', nom: 'Ben Salah' }) }
    };

    fixture.detectChanges();
  });

  afterEach(() => {
    localStorage.clear();
  });

  it('la fiche d’où l’on vient est affichée comme choisie', async () => {
    fixture.detectChanges();
    await new Promise(r => setTimeout(r, 0));
    fixture.detectChanges();

    expect(component.linkedDriverId).toBe(30);
    const select: HTMLSelectElement = fixture.nativeElement.querySelector('select.driver-link-select');
    expect(select).not.toBeNull();
    expect(select.selectedOptions[0].textContent!.trim()).toBe('Ali Ben Salah');
  });
});
