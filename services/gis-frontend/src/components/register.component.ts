import { Component, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { environment } from '../environments/environment';

/**
 * Inscription libre — jumeau visuel de l'écran de connexion.
 *
 * AUCUN CHOIX DE PLAN ICI. Le brouillon précédent proposait au visiteur de
 * sélectionner son abonnement et envoyait l'identifiant choisi au serveur : c'était
 * un libre-service, n'importe qui pouvait s'attribuer l'offre la plus complète. Le
 * plan de départ et la durée d'essai viennent désormais de la configuration du
 * serveur, et la requête ne porte aucun champ d'abonnement. Le visiteur découvre
 * les offres APRÈS son inscription, sur l'écran d'abonnement.
 *
 * Le nom de société est facultatif : à défaut, la société prend le nom de la
 * personne — beaucoup d'indépendants n'ont pas encore d'entreprise déclarée.
 */
@Component({
  selector: 'app-register',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="auth-page">
      <div class="auth-bg">
        <div class="bg-glow"></div>
        <div class="bg-dots"></div>
      </div>

      <div class="auth-card">
        <div class="auth-header">
          <div class="logo">
            <img src="/assets/calypso-logo.svg" alt="Calypso" width="504" height="170">
          </div>
          <h1>{{ submitted ? 'Vérifiez votre boîte mail' : 'Créer un compte' }}</h1>
          <p>{{ submitted ? 'Une dernière étape avant de commencer.' : trialDays + " jours d'essai — sans carte bancaire" }}</p>
        </div>

        <!-- Après envoi : plus de formulaire, l'utilisateur doit aller lire son
             courrier. Aucune session n'est ouverte tant que l'adresse n'est pas
             confirmée. -->
        @if (submitted) {
          <div class="confirm-panel">
            <div class="confirm-icon" [class.warn]="!emailSent">
              @if (emailSent) {
                <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z"/><polyline points="22,6 12,13 2,6"/>
                </svg>
              } @else {
                <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/>
                </svg>
              }
            </div>
            <p class="confirm-text">{{ resultMessage }}</p>
            <p class="confirm-mail">{{ email }}</p>

            <button type="button" class="btn-secondary" [disabled]="isLoading || resendDone" (click)="onResend()">
              {{ resendDone ? 'Nouveau lien demandé' : 'Renvoyer le lien' }}
            </button>
            @if (resendMessage) { <p class="hint center">{{ resendMessage }}</p> }
          </div>
        } @else {

        <form (ngSubmit)="onSubmit()" class="auth-form">
          <!-- La société est créée dans tous les cas — c'est la structure sur
               laquelle repose l'application — mais un particulier n'a pas à le
               savoir ni à inventer un nom d'entreprise. -->
          <div class="type-choice" role="radiogroup" aria-label="Type de compte">
            <button type="button" class="type-btn" [class.active]="accountType === 'particulier'"
                    role="radio" [attr.aria-checked]="accountType === 'particulier'"
                    (click)="accountType = 'particulier'">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>
              Particulier
            </button>
            <button type="button" class="type-btn" [class.active]="accountType === 'societe'"
                    role="radio" [attr.aria-checked]="accountType === 'societe'"
                    (click)="accountType = 'societe'">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 21h18"/><path d="M5 21V7l7-4 7 4v14"/><path d="M9 21v-6h6v6"/></svg>
              Société
            </button>
          </div>

          <div class="form-row">
            <div class="form-group">
              <label for="firstName">Prénom</label>
              <input type="text" id="firstName" name="firstName" [(ngModel)]="firstName" placeholder="Sonia" required />
            </div>
            <div class="form-group">
              <label for="lastName">Nom</label>
              <input type="text" id="lastName" name="lastName" [(ngModel)]="lastName" placeholder="Ben Salah" required />
            </div>
          </div>

          <div class="form-group">
            <label for="email">Email professionnel</label>
            <input type="email" id="email" name="email" [(ngModel)]="email" placeholder="exemple@entreprise.com" required />
          </div>

          <!-- Ce champ n'apparaît QUE pour un professionnel : on ne demande pas à
               un particulier le nom d'une entreprise qu'il n'a pas. -->
          @if (accountType === 'societe') {
            <div class="form-group">
              <label for="companyName">Nom de la société</label>
              <input type="text" id="companyName" name="companyName" [(ngModel)]="companyName"
                     placeholder="Transports Ben Salah" required />
              <small class="hint">Le nom sous lequel vous serez facturé.</small>
            </div>
          }

          <!-- Tranche de véhicules : un ordre de grandeur, pas un décompte.
               Posée à tout le monde — un particulier a lui aussi des véhicules,
               et la question porte sur eux, pas sur une entreprise. -->
          <div class="form-group">
            <label for="fleetSize">Combien de véhicules gérez-vous ?</label>
            <div class="select-wrap">
              <select id="fleetSize" name="fleetSize" [(ngModel)]="fleetSizeRange" required>
              <option value="" disabled>Choisissez une tranche</option>
              @for (r of fleetSizeOptions; track r) {
                <option [value]="r">{{ r === '100+' ? '100 et plus' : r + ' véhicules' }}</option>
              }
            </select>
            </div>
            <small class="hint">Une estimation suffit — vous ajouterez vos véhicules ensuite.</small>
          </div>
          <div class="form-group">
            <label for="country">Pays</label>
            <div class="select-wrap">
              <select id="country" name="country" [(ngModel)]="country" required>
                <option value="" disabled>Choisissez un pays</option>
                @for (c of countries; track c.code) {
                  <option [value]="c.code">{{ c.name }}</option>
                }
              </select>
            </div>
          </div>


          <div class="form-group">
            <label for="phone">Téléphone</label>
            <input type="tel" id="phone" name="phone" [(ngModel)]="phone"
                   [placeholder]="phonePlaceholder" required />
          </div>

          <div class="form-group">
            <label for="password">Mot de passe</label>
            <input type="password" id="password" name="password" [(ngModel)]="password" placeholder="••••••••••" required />
            <small class="hint">Au moins 10 caractères, mêlant minuscules, majuscules, chiffres ou symboles.</small>
          </div>

          @if (errorMessage) {
            <div class="error-message">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/>
              </svg>
              {{ errorMessage }}
            </div>
          }

          <button type="button" class="btn-primary btn-full" [disabled]="isLoading || !isValid()" (click)="onSubmit()">
            {{ isLoading ? 'Création du compte…' : 'Créer mon compte' }}
          </button>
        </form>
        }

        <div class="auth-footer">
          <p>Vous avez déjà un compte ? <a routerLink="/login" class="link">Se connecter</a></p>
        </div>
      </div>

      <div class="auth-side">
        <h2>Votre parc, en ordre, dès aujourd'hui</h2>
        <div class="features-list">
          <div class="feature-item">
            <span class="feature-icon">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="1" y="3" width="15" height="13" rx="2"/><path d="M16 8h4l3 3v5a2 2 0 0 1-2 2h-5"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/></svg>
            </span>
            <div>
              <h3>Vos véhicules et leurs entretiens</h3>
              <p>Échéances, réparations et documents au même endroit</p>
            </div>
          </div>
          <div class="feature-item">
            <span class="feature-icon">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="12" y1="1" x2="12" y2="23"/><path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/></svg>
            </span>
            <div>
              <h3>Dépenses et carburant</h3>
              <p>Saisie manuelle ou scan de vos factures</p>
            </div>
          </div>
          <div class="feature-item">
            <span class="feature-icon">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="20" x2="18" y2="10"/><line x1="12" y1="20" x2="12" y2="4"/><line x1="6" y1="20" x2="6" y2="14"/></svg>
            </span>
            <div>
              <h3>Rapports prêts à exporter</h3>
              <p>Kilométrage, coûts et entretien, en Excel ou PDF</p>
            </div>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    /* Charte « Calypso Command », identique à l'écran de connexion : les deux
       pages se suivent, elles ne doivent pas se ressembler « à peu près ». */
    .auth-page {
      --c-indigo: #4f46e5;
      --c-indigo-ink: #4338ca;
      --c-border: #e6eaf2;
      --c-ink: #0f172a;
      --c-sub: #64748b;

      min-height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 72px;
      padding: 48px 32px;
      box-sizing: border-box;
      position: relative;
      background: linear-gradient(135deg, #0d1425 0%, #16213a 100%);
    }

    .auth-bg { position: absolute; inset: 0; overflow: hidden; pointer-events: none; }
    .bg-glow {
      position: absolute; top: -50%; right: -20%; width: 80%; height: 150%;
      background: radial-gradient(ellipse at center, rgba(99,102,241,.24) 0%, transparent 70%);
      animation: pulse 8s ease-in-out infinite;
    }
    .bg-dots {
      position: absolute; inset: 0;
      background-image: radial-gradient(rgba(255,255,255,.05) 1px, transparent 1px);
      background-size: 40px 40px;
    }
    @keyframes pulse {
      0%, 100% { transform: scale(1); opacity: .5; }
      50% { transform: scale(1.1); opacity: .8; }
    }
    @keyframes rise {
      from { opacity: 0; transform: translateY(8px); }
      to { opacity: 1; transform: none; }
    }

    .auth-card {
      width: 480px; max-width: 100%;
      padding: 44px 48px;
      box-sizing: border-box;
      background: #fff;
      border-radius: 18px;
      position: relative; z-index: 1;
      box-shadow: 0 24px 60px -24px rgba(2,6,23,.65), 0 48px 120px -32px rgba(2,6,23,.55);
      animation: rise .35s ease-out both;
    }

    .auth-header { margin-bottom: 24px; }
    .logo { display: flex; align-items: center; gap: 14px; margin-bottom: 22px; }
    .logo img { height: 42px; width: auto; display: block; }
    .logo-icon {
      width: 52px; height: 52px;
      background: linear-gradient(135deg, #6366f1 0%, #4f46e5 100%);
      border-radius: 14px;
      display: flex; align-items: center; justify-content: center;
      box-shadow: 0 8px 24px rgba(79,70,229,.35);
    }
    .logo-text { display: flex; flex-direction: column; }
    .brand { font-size: 22px; font-weight: 800; color: var(--c-ink); letter-spacing: -.01em; }
    .subtitle { font-size: 13px; color: var(--c-indigo); font-weight: 600; }

    .auth-header h1 { font-size: 26px; font-weight: 800; color: var(--c-ink); margin: 0 0 6px; letter-spacing: -.02em; }
    .auth-header p { color: var(--c-sub); margin: 0; font-size: 14.5px; }

    .form-row { display: grid; grid-template-columns: 1fr 1fr; gap: 14px; }
    .form-group { margin-bottom: 16px; }
    .form-group label { display: block; font-weight: 600; color: #374151; margin-bottom: 8px; font-size: 13.5px; }
    .optional { font-weight: 500; color: #94a3b8; font-size: 12px; margin-left: 4px; }
    .form-group input {
      width: 100%;
      padding: 13px 15px;
      background: #f8fafc;
      border: 1px solid var(--c-border);
      border-radius: 10px;
      font-size: 15px;
      color: var(--c-ink);
      transition: all .2s;
      box-sizing: border-box;
    }
    .form-group input:focus {
      outline: none;
      background: #fff;
      border-color: var(--c-indigo);
      box-shadow: 0 0 0 3px rgba(79,70,229,.12);
    }
    /* ---------- listes déroulantes ----------
       Le <select> natif dessine sa propre flèche, différente sur chaque
       navigateur et impossible à mettre au diapason des champs texte. On la
       supprime (appearance:none) et on la redessine en fond, ce qui donne un
       rendu identique partout — tout en gardant l élément natif, donc le
       clavier, la recherche à la frappe et la liste du système sur mobile. */
    .select-wrap { position: relative; }
    .form-group select {
      width: 100%;
      padding: 13px 40px 13px 15px;
      background: #f8fafc;
      border: 1px solid var(--c-border);
      border-radius: 10px;
      font-size: 15px;
      font-family: inherit;
      color: var(--c-ink);
      transition: all .2s;
      box-sizing: border-box;
      appearance: none;
      -webkit-appearance: none;
      cursor: pointer;
    }
    .form-group select:hover { border-color: #cbd5e1; }
    .form-group select:focus {
      outline: none;
      background: #fff;
      border-color: var(--c-indigo);
      box-shadow: 0 0 0 3px rgba(79,70,229,.12);
    }
    /* Le chevron est posé sur l enveloppe et non sur le select : sur le select
       lui-même, Firefox le recouvre de son propre fond au survol. */
    .select-wrap::after {
      content: "";
      position: absolute; right: 16px; top: 50%;
      width: 9px; height: 9px; margin-top: -6px;
      border-right: 2px solid #64748b;
      border-bottom: 2px solid #64748b;
      transform: rotate(45deg);
      pointer-events: none;
    }
    .select-wrap:focus-within::after { border-color: var(--c-indigo); }
    /* La consigne « Choisissez… » reste grise tant que rien n est retenu. */
    .form-group select:invalid { color: #94a3b8; }

    .hint { display: block; margin-top: 6px; font-size: 12px; color: var(--c-sub); line-height: 1.45; }
    .hint.center { text-align: center; margin-top: 12px; }

    .type-choice { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; margin-bottom: 20px; }
    .type-btn {
      display: flex; align-items: center; justify-content: center; gap: 8px;
      padding: 13px 10px;
      background: #f8fafc;
      border: 1px solid var(--c-border);
      border-radius: 10px;
      font-size: 14px; font-weight: 600; color: var(--c-sub);
      cursor: pointer; transition: all .18s;
    }
    .type-btn:hover { border-color: #c7d2fe; color: var(--c-ink); }
    .type-btn.active {
      background: #fff; color: var(--c-indigo-ink);
      border-color: var(--c-indigo);
      box-shadow: 0 0 0 3px rgba(79,70,229,.12);
    }

    .confirm-panel { text-align: center; padding: 8px 0 4px; }
    .confirm-icon {
      width: 58px; height: 58px; margin: 0 auto 18px;
      display: flex; align-items: center; justify-content: center;
      border-radius: 16px;
      background: rgba(79,70,229,.1); color: var(--c-indigo);
    }
    .confirm-icon.warn { background: rgba(217,119,6,.12); color: #b45309; }
    .confirm-text { margin: 0 0 6px; font-size: 14.5px; line-height: 1.6; color: #334155; }
    .confirm-mail { margin: 0 0 22px; font-size: 14px; font-weight: 700; color: var(--c-ink); }
    .btn-secondary {
      padding: 11px 22px; border-radius: 10px;
      background: #fff; color: var(--c-indigo-ink);
      border: 1px solid var(--c-border);
      font-size: 13.5px; font-weight: 600; cursor: pointer; transition: all .2s;
    }
    .btn-secondary:hover:not(:disabled) { border-color: var(--c-indigo); background: #f8fafc; }
    .btn-secondary:disabled { opacity: .6; cursor: default; }

    .link { color: var(--c-indigo); text-decoration: none; font-weight: 600; }
    .link:hover { color: var(--c-indigo-ink); }

    .btn-primary {
      width: 100%;
      padding: 14px;
      margin-top: 4px;
      background: var(--c-indigo);
      color: #fff;
      border: none;
      border-radius: 10px;
      font-size: 15px;
      font-weight: 600;
      cursor: pointer;
      transition: all .2s;
    }
    .btn-primary:hover:not(:disabled) {
      background: var(--c-indigo-ink);
      transform: translateY(-1px);
      box-shadow: 0 8px 24px rgba(79,70,229,.35);
    }
    .btn-primary:disabled { opacity: .55; cursor: not-allowed; }

    .auth-footer { text-align: center; padding-top: 20px; margin-top: 22px; border-top: 1px solid var(--c-border); }
    .auth-footer p { color: var(--c-sub); font-size: 13.5px; margin: 0; }

    .error-message {
      display: flex; align-items: center; gap: 10px;
      background: rgba(220,38,38,.08);
      border: 1px solid rgba(220,38,38,.25);
      color: #b91c1c;
      padding: 12px 14px;
      border-radius: 10px;
      font-size: 13.5px;
      margin-bottom: 16px;
    }

    .auth-side {
      max-width: 460px;
      position: relative; z-index: 1;
      animation: rise .45s ease-out both;
    }
    .auth-side h2 {
      font-size: 30px; font-weight: 800; color: #fff;
      margin: 0 0 36px; letter-spacing: -.02em; line-height: 1.2;
    }
    .features-list { display: flex; flex-direction: column; gap: 28px; }
    .feature-item { display: flex; align-items: flex-start; gap: 18px; }
    .feature-icon {
      flex-shrink: 0;
      width: 46px; height: 46px;
      display: flex; align-items: center; justify-content: center;
      color: #a5b4fc;
      background: rgba(99,102,241,.16);
      border: 1px solid rgba(99,102,241,.28);
      border-radius: 12px;
    }
    .feature-item h3 { font-size: 17px; font-weight: 600; color: #e2e8f0; margin: 0 0 5px; }
    .feature-item p { font-size: 13.5px; color: #94a3b8; margin: 0; line-height: 1.5; }

    @media (prefers-reduced-motion: reduce) {
      .bg-glow, .auth-card, .auth-side { animation: none; }
    }
    @media (max-width: 1024px) {
      .auth-side { display: none; }
      .auth-card { width: 100%; max-width: 480px; }
    }
    @media (max-width: 560px) {
      .form-row { grid-template-columns: 1fr; gap: 0; }
    }
    @media (max-width: 480px) {
      .auth-page { padding: 24px 16px; }
      .auth-card { padding: 32px 24px; }
    }
  `]
})
export class RegisterComponent {
  accountType: 'particulier' | 'societe' = 'particulier';
  firstName = '';
  lastName = '';
  email = '';
  companyName = '';
  phone = '';

  /**
   * Pays de la societe, en code ISO 3166-1 alpha-2. Prerempli avec celui du
   * deploiement : la quasi-totalite des inscriptions vient du pays servi, et
   * faire choisir chacun dans une liste dont la reponse est presque toujours
   * la meme est une friction gratuite.
   */
  country = (environment as any).defaultCountry || 'TN';

  /** Pays proposes, le pays servi en tete. */
  readonly countries = [
    { code: 'TN', name: 'Tunisie' },
    { code: 'FR', name: 'France' },
    { code: 'DZ', name: 'Algérie' },
    { code: 'MA', name: 'Maroc' },
    { code: 'BE', name: 'Belgique' },
    { code: 'CH', name: 'Suisse' },
    { code: 'LU', name: 'Luxembourg' },
    { code: 'ES', name: 'Espagne' },
    { code: 'IT', name: 'Italie' },
    { code: 'DE', name: 'Allemagne' },
    { code: 'PT', name: 'Portugal' },
    { code: 'NL', name: 'Pays-Bas' },
    { code: 'CA', name: 'Canada' },
    { code: 'SN', name: 'Sénégal' },
    { code: 'CI', name: 'Côte d\x27Ivoire' }
  ];
  password = '';
  isLoading = false;
  errorMessage = '';

  // Après envoi : le formulaire cède la place à l'écran « vérifiez votre boîte ».
  submitted = false;
  emailSent = false;
  resultMessage = '';
  resendDone = false;
  resendMessage = '';

  brand = (environment as any).brandName || 'Calypso';
  // Purement informatif : la durée qui fait foi est celle du serveur.
  trialDays = (environment as any).selfSignupTrialDays || 14;
  // Indicatif PAR DÉPLOIEMENT : le repli garde le format algérien d'origine,
  // pour que Bougeo/DZ reste inchangé sans toucher à sa copie locale.
  phonePlaceholder = (environment as any).phonePlaceholder || '+213 5 00 00 00 00';

  /** Tranches proposées — mêmes valeurs que `FleetSizeRanges` côté serveur. */
  readonly fleetSizeOptions = ['1-5', '6-20', '21-50', '51-100', '100+'];
  fleetSizeRange = '';

  constructor(
    private router: Router,
    private authService: AuthService,
    private cdr: ChangeDetectorRef
  ) {}

  isValid(): boolean {
    const base = this.firstName.trim().length > 0
      && this.lastName.trim().length > 0
      && this.email.trim().length > 0
      && this.password.length >= 10
      // Exigée à l'écran, facultative côté API : le serveur reste compatible
      // avec les clients qui ne l'envoient pas (application mobile notamment).
      && this.fleetSizeRange.length > 0
      // Telephone EXIGE : c est le seul moyen de rappeler un prospect dont
      // l adresse rebondit, et le support s en sert quotidiennement.
      && this.phone.trim().length >= 6
      && this.country.trim().length > 0;
    // Un professionnel doit nommer sa société ; un particulier n'a rien à saisir.
    return this.accountType === 'societe'
      ? base && this.companyName.trim().length > 0
      : base;
  }

  onSubmit() {
    if (!this.isValid()) {
      this.errorMessage = this.accountType === 'societe'
        ? 'Renseignez le nom de votre société, votre identité, votre email, votre téléphone, votre pays et un mot de passe d’au moins 10 caractères.'
        : 'Renseignez votre prénom, votre nom, votre email, votre téléphone, votre pays et un mot de passe d’au moins 10 caractères.';
      return;
    }

    this.isLoading = true;
    this.errorMessage = '';
    this.cdr.detectChanges();

    this.authService.registerCompany({
      firstName: this.firstName.trim(),
      lastName: this.lastName.trim(),
      email: this.email.trim(),
      password: this.password,
      accountType: this.accountType,
      // Le nom d'entreprise n'est transmis que par un professionnel : pour un
      // particulier il n'existe pas, et le serveur l'ignorerait de toute façon.
      companyName: this.accountType === 'societe' ? this.companyName.trim() : undefined,
      fleetSizeRange: this.fleetSizeRange || undefined,
      phone: this.phone.trim(),
      country: this.country || undefined
    }).subscribe({
      next: (res) => {
        this.isLoading = false;
        // AUCUNE session n'est ouverte : le compte attend la confirmation de
        // l'adresse. On bascule sur l'écran qui le dit, plutôt que d'entrer dans
        // l'application.
        this.submitted = true;
        this.emailSent = res?.emailSent ?? false;
        this.resultMessage = res?.message
          || 'Votre compte est créé. Ouvrez le lien de confirmation reçu par email.';
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.isLoading = false;
        // Le serveur renvoie des messages exploitables (email déjà pris, mot de
        // passe trop faible) : les afficher tels quels plutôt qu'un texte générique.
        this.errorMessage = err?.error?.message
          || err?.error?.errors?.[0]?.errorMessage
          || 'La création du compte a échoué. Vérifiez vos informations et réessayez.';
        this.cdr.detectChanges();
      }
    });
  }

  onResend() {
    this.isLoading = true;
    this.resendMessage = '';
    this.cdr.detectChanges();

    this.authService.resendConfirmation(this.email.trim()).subscribe({
      next: (res) => {
        this.isLoading = false;
        this.resendDone = true;
        this.resendMessage = res?.message || 'Si un compte est en attente, un nouvel email vient d’être envoyé.';
        this.cdr.detectChanges();
      },
      error: () => {
        this.isLoading = false;
        this.resendMessage = 'Le renvoi a échoué. Réessayez dans quelques minutes.';
        this.cdr.detectChanges();
      }
    });
  }
}
