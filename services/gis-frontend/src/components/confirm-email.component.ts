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

      <div class="auth-card">
        <div class="logo">
          <span class="logo-icon">
            <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="white" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
              <path d="M5 13l4 4L19 7"/>
            </svg>
          </span>
          <div class="logo-text">
            <span class="brand">{{ brand }}</span>
            <span class="subtitle">Gestion de flotte</span>
          </div>
        </div>

        @if (state === 'pending') {
          <div class="panel">
            <div class="spinner"></div>
            <h1>Confirmation en cours…</h1>
          </div>
        } @else if (state === 'ok') {
          <div class="panel">
            <div class="icon ok">
              <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M20 6L9 17l-5-5"/></svg>
            </div>
            <h1>Adresse confirmée</h1>
            <p>{{ message }}</p>
            <a routerLink="/login" class="btn-primary">Se connecter</a>
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
