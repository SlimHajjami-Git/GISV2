import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { FormsModule } from '@angular/forms';
import { of } from 'rxjs';
import { UserManagementComponent } from './user-management.component';
import { ApiService } from '../services/api.service';

/**
 * Écran Utilisateurs — compte chauffeur (décision du 21/09/2026 : « le chauffeur est
 * un utilisateur »). Un administrateur coche « Chauffeur (application mobile) » ; le
 * compte n'a aucun droit de gestion, aucune affectation de véhicule, et le serveur fait
 * tout le cloisonnement à partir du booléen isDriverAccount. Ces tests verrouillent le
 * contrat d'API côté écran : le booléen part TOUJOURS (jamais undefined), les
 * affectations sont vides pour un chauffeur, et la liste montre le badge « Chauffeur ».
 */
describe('UserManagementComponent — compte chauffeur (application mobile)', () => {
  let component: UserManagementComponent;
  let fixture: any;
  let api: ApiService;

  const roles = [
    { id: 1, name: 'Administrateur', isSystem: true, isCompanyAdmin: true },
    { id: 2, name: 'Utilisateur', isSystem: true, isCompanyAdmin: false }
  ];

  const utilisateurs = [
    {
      id: 10, name: 'Amel Gestion', email: 'amel@transporttest.tn', roleId: 2,
      isCompanyAdmin: false, status: 'active', createdAt: '2026-09-01T00:00:00Z',
      accountType: 'staff', assignedVehicleIds: [5],
      userPermissions: { canMonitoring: true, canVehicles: true }
    },
    {
      id: 11, name: 'Karim Chauffeur', email: 'karim@transporttest.tn', roleId: 2,
      isCompanyAdmin: false, status: 'active', createdAt: '2026-09-02T00:00:00Z',
      accountType: 'driver'
    }
  ];

  const vehicules = [
    { id: 5, name: 'Camion 12', plate: 'AB-123-CD' },
    { id: 6, name: 'Utilitaire 3', plate: 'EF-456-GH' }
  ];

  beforeEach(async () => {
    jest.restoreAllMocks();
    localStorage.clear();
    // ngOnInit renvoie vers /login sans jeton ; le jeton n'est jamais lu au-delà.
    localStorage.setItem('auth_token', 'jeton-de-test');
    // La barre de l'écran injecte le service d'export PDF, qui précharge le logo
    // par fetch() — absent de jsdom. L'échec de préchargement est déjà prévu.
    (globalThis as any).fetch = jest.fn(() => Promise.resolve({ ok: false }));

    await TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule, FormsModule, UserManagementComponent],
      providers: [ApiService]
    }).compileComponents();

    fixture = TestBed.createComponent(UserManagementComponent);
    component = fixture.componentInstance;
    api = TestBed.inject(ApiService);

    jest.spyOn(api, 'getCurrentSubscription').mockReturnValue(of({ subscriptionType: null }) as any);
    jest.spyOn(api, 'getRoles').mockReturnValue(of(roles) as any);
    jest.spyOn(api, 'getUsers').mockReturnValue(of(utilisateurs) as any);
    jest.spyOn(api, 'getVehicles').mockReturnValue(of(vehicules) as any);

    fixture.detectChanges();
  });

  afterEach(() => {
    localStorage.clear();
  });

  /** Remplit l'identité minimale exigée par saveUser(). */
  const remplirIdentite = (mdp = 'Secret@2026') => {
    component.userForm.firstName = 'Karim';
    component.userForm.lastName = 'Chauffeur';
    component.userForm.email = 'karim@transporttest.tn';
    component.userForm.password = mdp;
  };

  it('créer un chauffeur envoie isDriverAccount: true et aucune affectation de véhicule', () => {
    const creation = jest.spyOn(api, 'createUser').mockReturnValue(of({ id: 12 }) as any);

    component.openUserModal();
    remplirIdentite();
    // Un véhicule coché et « Administrateur » AVANT de cocher la case : tout doit tomber.
    component.toggleVehicle(5);
    component.userForm.isCompanyAdmin = true;
    component.userForm.isDriverAccount = true;
    component.onDriverAccountToggle();

    component.saveUser();

    expect(creation).toHaveBeenCalledTimes(1);
    const charge = creation.mock.calls[0][0];
    expect(charge.isDriverAccount).toBe(true);
    expect(charge.assignedVehicleIds).toEqual([]);
    expect(charge.isCompanyAdmin).toBe(false);
    expect(charge.email).toBe('karim@transporttest.tn');
    expect(charge.roleId).toBe(2);
  });

  it('un compte ordinaire part avec isDriverAccount: false et ses affectations', () => {
    const creation = jest.spyOn(api, 'createUser').mockReturnValue(of({ id: 13 }) as any);

    component.openUserModal();
    remplirIdentite();
    component.toggleVehicle(6);

    component.saveUser();

    const charge = creation.mock.calls[0][0];
    expect(charge.isDriverAccount).toBe(false);
    expect(charge.assignedVehicleIds).toEqual([6]);
  });

  it('modifier un utilisateur envoie isDriverAccount en booléen, jamais undefined', () => {
    const modification = jest.spyOn(api, 'updateUser').mockReturnValue(of(void 0) as any);

    // Compte ordinaire : false, pour que le serveur puisse repasser un chauffeur en
    // compte ordinaire (null = inchangé côté serveur, donc undefined interdit).
    component.openUserModal(utilisateurs[0] as any);
    expect(component.userForm.isDriverAccount).toBe(false);
    component.saveUser();
    expect(typeof modification.mock.calls[0][1].isDriverAccount).toBe('boolean');
    expect(modification.mock.calls[0][1].isDriverAccount).toBe(false);
    expect(modification.mock.calls[0][1].assignedVehicleIds).toEqual([5]);

    // Compte chauffeur existant : la case est cochée à l'ouverture et repart à true.
    component.openUserModal(utilisateurs[1] as any);
    expect(component.userForm.isDriverAccount).toBe(true);
    component.saveUser();
    expect(modification.mock.calls[1][1].isDriverAccount).toBe(true);
    expect(modification.mock.calls[1][1].assignedVehicleIds).toEqual([]);
  });

  it('un chauffeur n’a ni étape Permissions ni étape Véhicules : le bouton mène à l’enregistrement', () => {
    component.openUserModal();
    expect(component.hasNextUserStep()).toBe(true);

    component.userForm.isDriverAccount = true;
    component.onDriverAccountToggle();
    fixture.detectChanges();

    expect(component.hasNextUserStep()).toBe(false);
    expect(component.userModalStep).toBe(1);
    const onglets: NodeListOf<HTMLElement> = fixture.nativeElement.querySelectorAll('.step-tab');
    expect(onglets.length).toBe(1);
    expect(fixture.nativeElement.querySelector('.driver-note')!.textContent).toContain('aucun droit de gestion');
    expect(fixture.nativeElement.querySelector('.modal-footer .btn-primary')!.textContent).toContain('Créer');
  });

  it('la liste affiche le badge « Chauffeur » pour accountType: driver, le libellé de permissions sinon', () => {
    const badges: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.users-table tbody .role-badge'));
    expect(badges.length).toBe(2);

    expect(badges[0].classList.contains('driver')).toBe(false);
    expect(badges[0].textContent).toContain('module');

    expect(badges[1].classList.contains('driver')).toBe(true);
    expect(badges[1].textContent).toContain('Chauffeur');
  });

  it('la tuile « Chauffeurs » compte les comptes de l’application mobile', () => {
    expect(component.getDriverAccountsCount()).toBe(1);
    const tuile: HTMLElement | null = fixture.nativeElement.querySelector('.stat-driver-accounts .stat-value');
    expect(tuile).not.toBeNull();
    expect(tuile!.textContent!.trim()).toBe('1');
  });
});
