import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { environment } from '../environments/environment';

/**
 * Cible du lien de confirmation envoyé par email : /confirmation-email?token=…
 *
 * L'écran consomme le jeton puis renvoie vers la connexion. Il n'ouvre PAS de
 * session : quiconque intercepte le lien pourrait sinon entrer dans le compte sans
 * connaître le mot de passe.
 */
@Component({
  selector: 'app-confirm-email',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <div class="auth-page">
      <div class="auth-bg"><div class="bg-glow"></div><div class="bg-dots"></div></div>

      <div class="auth-card" [class.wide]="state === 'ok'">
        <div class="logo">
          <img src="/assets/calypso-logo.svg" alt="Calypso">
        </div>

        @if (state === 'pending') {
          <div class="panel">
            <div class="spinner"></div>
            <h1>Confirmation en cours…</h1>
          </div>
        } @else if (state === 'ok') {
          <!-- C'EST ICI que l'écran « Votre compte Calypso est prêt ! » a sa
               place, et pas après l'inscription : à cet instant précis le compte
               vient de passer en actif. Annoncer « espace activé » juste après
               le formulaire aurait envoyé l'utilisateur vers une connexion qui
               l'aurait refusé. -->
          <div class="panel ready">
            <div class="icon ok">
              <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M20 6L9 17l-5-5"/></svg>
            </div>
            <h1>Votre compte Calypso est prêt !</h1>
            <p>Merci de nous avoir rejoints. Votre espace est maintenant activé et prêt à l'emploi.</p>

            <span class="ok-badge">
              <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4"><circle cx="12" cy="12" r="9"/><path d="m8.5 12 2.5 2.5 4.5-5"/></svg>
              Activation réussie
            </span>

            <h2 class="trial-title">Profitez de {{ trialDays }} jours d'essai gratuit</h2>

            <div class="trial-grid">
              <div class="trial-item">
                <span class="ti-ic"><svg viewBox="0 0 24 24" fill="none" stroke="#818cf8" stroke-width="1.8"><rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4"/></svg></span>
                <h3>{{ trialDays }} jours complets</h3>
                <p>Testez toutes les fonctionnalités sans aucune limitation.</p>
              </div>
              <div class="trial-item">
                <span class="ti-ic"><svg viewBox="0 0 24 24" fill="none" stroke="#818cf8" stroke-width="1.8"><rect x="2.5" y="5" width="19" height="14" rx="2.5"/><path d="M2.5 10h19"/></svg></span>
                <h3>Aucune carte bancaire</h3>
                <p>Aucun paiement requis pendant la période d'essai.</p>
              </div>
              <div class="trial-item">
                <span class="ti-ic"><svg viewBox="0 0 24 24" fill="none" stroke="#818cf8" stroke-width="1.8"><path d="M5 15.5 8.5 19M4.5 19.5 9 15M14 4.5c3.5-1.5 6 1 4.5 4.5-1.2 2.8-4.6 6-8 8.5L7 14C9.5 10.6 11.2 5.7 14 4.5z"/></svg></span>
                <h3>Démarrage immédiat</h3>
                <p>Accédez à votre espace et commencez dès maintenant.</p>
              </div>
            </div>

            <a routerLink="/login" class="btn-primary">Accéder à Calypso</a>
          </div>
        } @else {
          <div class="panel">
            <div class="icon ko">
              <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
            </div>
            <h1>Confirmation impossible</h1>
            <p>{{ message }}</p>
            <a routerLink="/inscription" class="btn-primary">Retour à l'inscription</a>
          </div>
        }
      </div>
    </div>
  `,
  styles: [`
    /* ---------- écran « Votre compte Calypso est prêt ! » ---------- */
    .panel.ready { max-width: 720px; }
    .ok-badge {
      display: inline-flex; align-items: center; gap: 8px; margin: 4px 0 26px;
      padding: 8px 16px; border-radius: 999px;
      background: rgba(16,185,129,.12); border: 1px solid rgba(52,211,153,.35);
      color: #34d399; font-size: 13px; font-weight: 600;
    }
    .trial-title { font-size: 21px; font-weight: 700; margin: 0 0 24px; color: #0f172a; }
    .trial-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 4px; margin-bottom: 28px; }
    .trial-item { padding: 0 18px; }
    .trial-item + .trial-item { border-left: 1px solid var(--c-border); }
    .ti-ic {
      width: 52px; height: 52px; border-radius: 50%; margin: 0 auto 14px;
      display: grid; place-items: center; background: rgba(99,102,241,.10);
    }
    .ti-ic svg { width: 23px; height: 23px; }
    .trial-item h3 { font-size: 14.5px; font-weight: 700; margin: 0 0 6px; color: #0f172a; }
    .trial-item p { font-size: 13px; line-height: 1.5; color: var(--c-sub); margin: 0; }
    @media (max-width: 640px) {
      .trial-grid { grid-template-columns: 1fr; gap: 22px; }
      .trial-item + .trial-item { border-left: 0; border-top: 1px solid var(--c-border); padding-top: 22px; }
    }
    .auth-page {
      --c-indigo: #4f46e5; --c-ink: #0f172a; --c-sub: #64748b; --c-border: #e6eaf2;
      min-height: 100vh; display: flex; align-items: center; justify-content: center;
      padding: 48px 24px; box-sizing: border-box; position: relative;
      background: linear-gradient(135deg, #0d1425 0%, #16213a 100%);
    }
    .auth-bg { position: absolute; inset: 0; overflow: hidden; pointer-events: none; }
    .bg-glow {
      position: absolute; top: -50%; right: -20%; width: 80%; height: 150%;
      background: radial-gradient(ellipse at center, rgba(99,102,241,.24) 0%, transparent 70%);
    }
    .bg-dots {
      position: absolute; inset: 0;
      background-image: radial-gradient(rgba(255,255,255,.05) 1px, transparent 1px);
      background-size: 40px 40px;
    }
    .auth-card.wide { width: 720px; }
    .logo img { height: 42px; width: auto; display: block; margin: 0 auto; }
    .auth-card {
      width: 440px; max-width: 100%; padding: 44px 40px; box-sizing: border-box;
      background: #fff; border-radius: 18px; position: relative; z-index: 1;
      box-shadow: 0 24px 60px -24px rgba(2,6,23,.65);
      text-align: center;
    }
    .logo { display: flex; align-items: center; justify-content: center; gap: 14px; margin-bottom: 26px; }
    .logo-icon {
      width: 52px; height: 52px; border-radius: 14px;
      background: linear-gradient(135deg, #6366f1 0%, #4f46e5 100%);
      display: flex; align-items: center; justify-content: center;
      box-shadow: 0 8px 24px rgba(79,70,229,.35);
    }
    .logo-text { display: flex; flex-direction: column; text-align: left; }
    .brand { font-size: 22px; font-weight: 800; color: var(--c-ink); }
    .subtitle { font-size: 13px; color: var(--c-indigo); font-weight: 600; }

    .panel h1 { font-size: 21px; font-weight: 800; color: var(--c-ink); margin: 0 0 8px; }
    .panel p { font-size: 14.5px; line-height: 1.6; color: var(--c-sub); margin: 0 0 24px; }

    .icon {
      width: 60px; height: 60px; margin: 0 auto 18px; border-radius: 17px;
      display: flex; align-items: center; justify-content: center;
    }
    .icon.ok { background: rgba(5,150,105,.12); color: #059669; }
    .icon.ko { background: rgba(220,38,38,.1); color: #b91c1c; }

    .spinner {
      width: 34px; height: 34px; margin: 6px auto 20px; border-radius: 50%;
      border: 3px solid #e6eaf2; border-top-color: var(--c-indigo);
      animation: spin .8s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }

    .btn-primary {
      display: inline-block; padding: 13px 30px; border-radius: 10px;
      background: var(--c-indigo); color: #fff; text-decoration: none;
      font-size: 14.5px; font-weight: 600; transition: all .2s;
    }
    .btn-primary:hover { background: #4338ca; transform: translateY(-1px); }

    @media (prefers-reduced-motion: reduce) { .spinner { animation: none; } }
  `]
})
export class ConfirmEmailComponent implements OnInit {
  state: 'pending' | 'ok' | 'ko' = 'pending';
  message = '';
  brand = (environment as any).brandName || 'Calypso';

  /** Durée annoncée de l essai. Le document maitre impose 7 jours. */
  trialDays = (environment as any).selfSignupTrialDays || 7;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private authService: AuthService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    const token = this.route.snapshot.queryParamMap.get('token') || '';
    if (!token) {
      this.state = 'ko';
      this.message = "Ce lien est incomplet. Ouvrez celui reçu par email, sans le modifier.";
      return;
    }

    this.authService.confirmEmail(token).subscribe({
      next: (res) => {
        this.state = 'ok';
        this.message = res?.message || 'Votre adresse est confirmée. Vous pouvez vous connecter.';
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.state = 'ko';
        this.message = err?.error?.message
          || "Ce lien n'est plus valable. Demandez-en un nouveau depuis la page d'inscription.";
        this.cdr.detectChanges();
      }
    });
  }
}
