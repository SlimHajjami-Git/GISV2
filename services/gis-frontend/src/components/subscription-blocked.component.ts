import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { AuthService } from '../services/auth.service';
import { environment } from '../environments/environment';

/**
 * Écran pleine page affiché quand l'abonnement de la société est SUSPENDU par
 * l'administrateur plateforme ou EXPIRÉ au-delà de la période de grâce.
 * On y arrive via l'authInterceptor (403 subscription_*) ou le
 * SubscriptionStatusService. « Réessayer » re-vérifie le statut : si le
 * sys_admin a réactivé, l'utilisateur repart au dashboard sans se reconnecter.
 */
@Component({
  selector: 'app-subscription-blocked',
  standalone: true,
  imports: [CommonModule],
  template: `
<div class="blk">
  <div class="card">
    <div class="ic" [class.warn]="reason==='expired'">
      <svg *ngIf="reason!=='expired'" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><rect x="4" y="10" width="16" height="10" rx="2.5"/><path d="M8 10V7a4 4 0 0 1 8 0v3"/></svg>
      <svg *ngIf="reason==='expired'" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 3"/></svg>
    </div>
    <h1>{{ reason==='expired' ? 'Abonnement expiré' : 'Abonnement suspendu' }}</h1>
    <p *ngIf="reason==='expired'">
      L'abonnement de votre société a expiré et la période de régularisation est écoulée.
      L'accès à Calypso est suspendu jusqu'au renouvellement.
    </p>
    <p *ngIf="reason!=='expired'">
      L'accès de votre société à Calypso a été suspendu.
      Veuillez contacter votre prestataire pour régulariser la situation.
    </p>
    <p class="contact">Contact : <b>{{ contactEmail }}</b></p>
    <div class="actions">
      <button class="btn ghost" (click)="logout()">Se déconnecter</button>
      <button class="btn primary" (click)="retry()" [disabled]="checking">{{ checking ? 'Vérification…' : 'Réessayer' }}</button>
    </div>
  </div>
</div>
  `,
  styles: [`
    :host{display:block}
    .blk{
      min-height:100vh;display:flex;align-items:center;justify-content:center;padding:24px;
      background:
        radial-gradient(900px 400px at 85% -10%, rgba(99,102,241,.25), transparent 60%),
        linear-gradient(135deg,#0d1425,#16213a 60%,#0d1425);
      font-family:var(--font-family,'Inter',system-ui,sans-serif);
    }
    .card{
      max-width:480px;width:100%;text-align:center;
      background:rgba(255,255,255,.045);border:1px solid rgba(148,163,184,.18);
      border-radius:20px;padding:40px 36px;color:#f1f5f9;
      box-shadow:0 30px 60px -30px rgba(2,6,23,.8);backdrop-filter:blur(8px);
    }
    .ic{
      width:64px;height:64px;border-radius:18px;margin:0 auto 20px;
      display:grid;place-items:center;color:#f87171;
      background:rgba(239,68,68,.12);border:1px solid rgba(248,113,113,.3);
    }
    .ic.warn{color:#fbbf24;background:rgba(245,158,11,.12);border-color:rgba(251,191,36,.3)}
    .ic svg{width:30px;height:30px}
    h1{margin:0 0 12px;font-size:24px;font-weight:800;letter-spacing:-.02em}
    p{margin:0 0 10px;font-size:14px;line-height:1.6;color:#cbd5e1}
    .contact{margin-top:14px;font-size:13px;color:#94a3b8}
    .contact b{color:#e2e8f0}
    .actions{display:flex;gap:10px;justify-content:center;margin-top:26px;flex-wrap:wrap}
    .btn{padding:10px 20px;border-radius:10px;font-size:13.5px;font-weight:650;cursor:pointer;border:none;transition:transform .15s,filter .15s}
    .btn:hover{transform:translateY(-1px);filter:brightness(1.08)}
    .btn:disabled{opacity:.6;cursor:default;transform:none}
    .btn.primary{background:linear-gradient(180deg,#6366f1,#4f46e5);color:#fff;box-shadow:0 6px 16px -6px rgba(99,102,241,.6)}
    .btn.ghost{background:rgba(148,163,184,.12);color:#e2e8f0;border:1px solid rgba(148,163,184,.25)}
  `]
})
export class SubscriptionBlockedComponent implements OnInit {
  reason = 'suspended';
  checking = false;
  contactEmail = 'contact@belive.tn';

  constructor(private route: ActivatedRoute, private router: Router,
              private http: HttpClient, private auth: AuthService) {}

  ngOnInit(): void {
    this.reason = this.route.snapshot.queryParamMap.get('reason') || 'suspended';
  }

  retry(): void {
    this.checking = true;
    this.http.get<any>(`${environment.apiUrl}/subscriptions/banner`).subscribe({
      next: (b) => {
        this.checking = false;
        if (b?.level !== 'blocked') this.router.navigate(['/dashboard']);
        else this.reason = b.reason || this.reason;
      },
      error: () => { this.checking = false; }
    });
  }

  logout(): void {
    this.auth.logout();
    this.router.navigate(['/login']);
  }
}
