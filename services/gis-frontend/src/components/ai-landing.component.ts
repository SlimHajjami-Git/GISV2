import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';

interface ChatMessage {
  role: 'user' | 'ai';
  text: string;
}

/**
 * Calypso — AI automobile assistant landing page (public, pre-login).
 *
 * A ChatGPT-style conversation, dressed as a premium automotive cockpit:
 * midnight background, headlight glow, receding "road" grid, custom vehicle
 * SVGs, and staggered page-load motion. The chat is fully interactive with
 * topic-matched canned answers (interface-first) — wiring the real backend is
 * a one-line swap in `respond()`. "Accéder à Calypso" routes to /login.
 */
@Component({
  selector: 'app-ai-landing',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
  <div class="cockpit">
    <!-- ambient layers -->
    <div class="bg-grid" aria-hidden="true"></div>
    <div class="bg-glow" aria-hidden="true"></div>
    <div class="bg-car" aria-hidden="true">
      <svg viewBox="0 0 640 220" fill="none" xmlns="http://www.w3.org/2000/svg">
        <path d="M40 160 L110 160 C130 120 175 96 240 92 L400 88 C452 86 496 104 540 148 L600 158 C616 160 622 168 618 178 L560 180"
              stroke="currentColor" stroke-width="2" stroke-linecap="round"/>
        <path d="M110 160 L560 180" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>
        <path d="M255 92 L268 132 L470 128 L440 100" stroke="currentColor" stroke-width="1.6"/>
        <circle cx="205" cy="180" r="34" stroke="currentColor" stroke-width="3"/>
        <circle cx="205" cy="180" r="13" stroke="currentColor" stroke-width="2"/>
        <circle cx="470" cy="180" r="34" stroke="currentColor" stroke-width="3"/>
        <circle cx="470" cy="180" r="13" stroke="currentColor" stroke-width="2"/>
      </svg>
    </div>

    <!-- top bar -->
    <header class="topbar">
      <div class="brand">
        <span class="logo" aria-hidden="true">
          <svg width="34" height="34" viewBox="0 0 40 40" fill="none">
            <rect width="40" height="40" rx="10" fill="url(#lg)"/>
            <path d="M11 22c0-5 4-9 9-9s9 4 9 9" stroke="#fff" stroke-width="2.6" stroke-linecap="round"/>
            <circle cx="20" cy="27" r="2.6" fill="#fff"/>
            <defs><linearGradient id="lg" x1="0" y1="0" x2="40" y2="40">
              <stop stop-color="#2f6bff"/><stop offset="1" stop-color="#12d0ff"/></linearGradient></defs>
          </svg>
        </span>
        <div class="brand-txt">
          <span class="brand-name">Calypso</span>
          <span class="brand-badge">Assistant Auto · IA</span>
        </div>
      </div>
      <button class="btn-calypso" (click)="goToCalypso()">
        Accéder à Calypso
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none"><path d="M5 12h14M13 6l6 6-6 6" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/></svg>
      </button>
    </header>

    <!-- conversation -->
    <main class="stage" [class.chatting]="messages.length > 0">

      @if (messages.length === 0) {
        <section class="hero">
          <span class="eyebrow up">Diagnostic · Entretien · Pièces · Conduite</span>
          <h1 class="up d1">Votre copilote <span class="grad">automobile</span>,<br>propulsé par l'IA.</h1>
          <p class="lede up d2">Posez n'importe quelle question sur votre véhicule — pannes, voyants, entretien, consommation — et obtenez une réponse claire en quelques secondes.</p>

          <div class="tiles up d3">
            @for (t of suggestions; track t.q) {
              <button class="tile" (click)="ask(t.q)">
                <span class="tile-ico" [innerHTML]="t.icon"></span>
                <span class="tile-q">{{ t.q }}</span>
                <span class="tile-arrow">→</span>
              </button>
            }
          </div>
        </section>
      } @else {
        <section class="thread">
          @for (m of messages; track $index) {
            <div class="msg" [class.me]="m.role === 'user'" [class.ai]="m.role === 'ai'">
              @if (m.role === 'ai') {
                <span class="avatar" aria-hidden="true">
                  <svg width="20" height="20" viewBox="0 0 24 24" fill="none">
                    <circle cx="12" cy="12" r="9" stroke="currentColor" stroke-width="1.8"/>
                    <circle cx="12" cy="12" r="3" stroke="currentColor" stroke-width="1.8"/>
                    <path d="M12 3v4M12 17v4M3 12h4M17 12h4" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/>
                  </svg>
                </span>
              }
              <div class="bubble">{{ m.text }}</div>
            </div>
          }
          @if (thinking) {
            <div class="msg ai">
              <span class="avatar" aria-hidden="true">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none"><circle cx="12" cy="12" r="9" stroke="currentColor" stroke-width="1.8"/><circle cx="12" cy="12" r="3" stroke="currentColor" stroke-width="1.8"/></svg>
              </span>
              <div class="bubble typing"><i></i><i></i><i></i></div>
            </div>
          }
        </section>
      }

      <!-- console / input -->
      <div class="console up d4">
        <div class="console-inner" [class.focus]="focused">
          <span class="console-ico" aria-hidden="true">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none"><circle cx="12" cy="12" r="8.2" stroke="currentColor" stroke-width="1.6"/><path d="M12 5.5v6.5l4 2.4" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/></svg>
          </span>
          <input
            class="console-input"
            [(ngModel)]="draft"
            (keydown.enter)="send()"
            (focus)="focused = true" (blur)="focused = false"
            placeholder="Posez votre question automobile…"
            aria-label="Votre question" />
          <button class="send" (click)="send()" [disabled]="!draft.trim() || thinking" aria-label="Envoyer">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none"><path d="M5 12h13M12 5l7 7-7 7" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/></svg>
          </button>
        </div>
        <p class="disclaimer">Aperçu de l'interface — l'assistant IA complet sera bientôt connecté. Vérifiez toujours les informations critiques.</p>
      </div>
    </main>
  </div>
  `,
  styles: [`
    @import url('https://fonts.googleapis.com/css2?family=Sora:wght@600;700;800&family=Manrope:wght@400;500;600&family=Rajdhani:wght@600;700&display=swap');

    :host { display:block; }
    *, *::before, *::after { box-sizing:border-box; }

    .cockpit{
      --bg:#060912; --bg2:#0a0f1f; --text:#e9eefc; --muted:#93a3c7;
      --blue:#2f6bff; --cyan:#19d3ff; --line:rgba(120,160,255,.14);
      position:relative; min-height:100vh; overflow:hidden;
      background:
        radial-gradient(1200px 600px at 50% -10%, #12224a 0%, transparent 60%),
        linear-gradient(180deg, var(--bg2), var(--bg));
      color:var(--text);
      font-family:'Manrope', system-ui, sans-serif;
      display:flex; flex-direction:column;
    }

    /* ambient */
    .bg-grid{ position:absolute; inset:0; pointer-events:none; opacity:.5;
      background-image:linear-gradient(var(--line) 1px, transparent 1px), linear-gradient(90deg, var(--line) 1px, transparent 1px);
      background-size:44px 44px;
      transform:perspective(520px) rotateX(62deg) translateY(28%) scale(1.9);
      transform-origin:top center;
      mask-image:linear-gradient(180deg, transparent, #000 32%, transparent 78%);
    }
    .bg-glow{ position:absolute; top:-24%; left:50%; width:900px; height:520px; transform:translateX(-50%);
      pointer-events:none; filter:blur(40px);
      background:radial-gradient(closest-side, rgba(47,107,255,.42), rgba(25,211,255,.10) 55%, transparent 72%);
      animation:sweep 9s ease-in-out infinite alternate; }
    @keyframes sweep{ from{ transform:translateX(-58%) } to{ transform:translateX(-42%) } }
    .bg-car{ position:absolute; right:-40px; bottom:-10px; width:min(680px,62vw); color:rgba(150,180,255,.10);
      pointer-events:none; filter:drop-shadow(0 0 30px rgba(47,107,255,.18)); }
    .bg-car svg{ width:100%; height:auto; }

    /* top bar */
    .topbar{ position:relative; z-index:3; display:flex; align-items:center; justify-content:space-between;
      padding:20px clamp(18px,4vw,54px); }
    .brand{ display:flex; align-items:center; gap:12px; }
    .logo{ display:grid; place-items:center; filter:drop-shadow(0 6px 18px rgba(47,107,255,.45)); }
    .brand-txt{ display:flex; flex-direction:column; line-height:1; }
    .brand-name{ font-family:'Sora',sans-serif; font-weight:800; font-size:20px; letter-spacing:-.01em; }
    .brand-badge{ margin-top:4px; font-family:'Rajdhani',sans-serif; font-weight:700; font-size:11px;
      letter-spacing:.22em; text-transform:uppercase; color:var(--cyan); }
    .btn-calypso{ display:inline-flex; align-items:center; gap:9px; cursor:pointer;
      font-family:'Rajdhani',sans-serif; font-weight:700; font-size:15px; letter-spacing:.06em; text-transform:uppercase;
      color:#fff; padding:11px 20px; border-radius:12px; border:1px solid rgba(120,170,255,.5);
      background:linear-gradient(180deg, rgba(47,107,255,.95), rgba(23,90,230,.95));
      box-shadow:0 10px 26px rgba(47,107,255,.42), inset 0 1px 0 rgba(255,255,255,.25);
      transition:transform .18s ease, box-shadow .18s ease, filter .18s ease; }
    .btn-calypso:hover{ transform:translateY(-2px); filter:brightness(1.08);
      box-shadow:0 16px 34px rgba(47,107,255,.55), inset 0 1px 0 rgba(255,255,255,.3); }
    .btn-calypso svg{ transition:transform .18s ease; } .btn-calypso:hover svg{ transform:translateX(3px); }

    /* stage */
    .stage{ position:relative; z-index:2; flex:1; display:flex; flex-direction:column;
      width:100%; max-width:900px; margin:0 auto; padding:8px clamp(16px,4vw,32px) 26px;
      justify-content:center; }
    .stage.chatting{ justify-content:flex-start; }

    /* hero */
    .hero{ text-align:center; margin:auto 0; }
    .eyebrow{ display:inline-block; font-family:'Rajdhani',sans-serif; font-weight:700; font-size:12px;
      letter-spacing:.26em; text-transform:uppercase; color:var(--muted);
      padding:7px 16px; border:1px solid var(--line); border-radius:999px;
      background:rgba(255,255,255,.02); }
    h1{ font-family:'Sora',sans-serif; font-weight:800; letter-spacing:-.02em; line-height:1.05;
      font-size:clamp(34px, 6vw, 60px); margin:22px 0 0; }
    .grad{ background:linear-gradient(100deg, var(--blue), var(--cyan)); -webkit-background-clip:text; background-clip:text; color:transparent; }
    .lede{ color:var(--muted); font-size:clamp(15px,1.6vw,18px); line-height:1.6; max-width:620px; margin:18px auto 0; }

    /* suggestion tiles */
    .tiles{ margin-top:36px; display:grid; grid-template-columns:repeat(2,1fr); gap:14px; max-width:680px; margin-inline:auto; }
    .tile{ display:flex; align-items:center; gap:14px; text-align:left; cursor:pointer;
      padding:16px 18px; border-radius:16px; color:var(--text);
      border:1px solid var(--line); background:linear-gradient(180deg, rgba(255,255,255,.045), rgba(255,255,255,.015));
      backdrop-filter:blur(6px); transition:transform .18s ease, border-color .18s ease, box-shadow .18s ease; }
    .tile:hover{ transform:translateY(-3px); border-color:rgba(47,107,255,.55);
      box-shadow:0 14px 30px rgba(0,0,0,.35), 0 0 0 1px rgba(47,107,255,.18) inset; }
    .tile-ico{ flex:none; width:38px; height:38px; display:grid; place-items:center; border-radius:11px;
      color:var(--cyan); background:rgba(25,211,255,.10); border:1px solid rgba(25,211,255,.22); }
    .tile-ico svg{ width:20px; height:20px; }
    .tile-q{ flex:1; font-weight:600; font-size:14.5px; line-height:1.35; }
    .tile-arrow{ color:var(--muted); font-size:18px; transition:transform .18s ease, color .18s ease; }
    .tile:hover .tile-arrow{ transform:translateX(4px); color:var(--cyan); }

    /* thread */
    .thread{ display:flex; flex-direction:column; gap:16px; padding:22px 2px 8px; overflow-y:auto; }
    .msg{ display:flex; gap:12px; max-width:100%; animation:rise .34s cubic-bezier(.2,.7,.2,1) both; }
    .msg.me{ justify-content:flex-end; }
    .avatar{ flex:none; width:34px; height:34px; border-radius:11px; display:grid; place-items:center;
      color:var(--cyan); background:rgba(25,211,255,.10); border:1px solid rgba(25,211,255,.25); }
    .bubble{ padding:13px 16px; border-radius:16px; font-size:15px; line-height:1.55; max-width:min(74%, 620px); }
    .msg.ai .bubble{ background:rgba(255,255,255,.045); border:1px solid var(--line); border-top-left-radius:6px; }
    .msg.me .bubble{ background:linear-gradient(180deg, rgba(47,107,255,.95), rgba(23,90,230,.95)); color:#fff;
      border-top-right-radius:6px; box-shadow:0 8px 22px rgba(47,107,255,.32); }
    .typing{ display:inline-flex; gap:6px; align-items:center; }
    .typing i{ width:7px; height:7px; border-radius:50%; background:var(--cyan); opacity:.5; animation:blink 1.2s infinite; }
    .typing i:nth-child(2){ animation-delay:.2s } .typing i:nth-child(3){ animation-delay:.4s }
    @keyframes blink{ 0%,60%,100%{ transform:translateY(0); opacity:.4 } 30%{ transform:translateY(-4px); opacity:1 } }

    /* console */
    .console{ margin-top:24px; }
    .console-inner{ display:flex; align-items:center; gap:10px; padding:8px 8px 8px 16px;
      border-radius:18px; border:1px solid rgba(120,160,255,.28);
      background:linear-gradient(180deg, rgba(16,24,48,.9), rgba(9,14,30,.9)); backdrop-filter:blur(10px);
      box-shadow:0 16px 40px rgba(0,0,0,.45), inset 0 1px 0 rgba(255,255,255,.06);
      transition:border-color .2s ease, box-shadow .2s ease; }
    .console-inner.focus{ border-color:rgba(47,107,255,.7);
      box-shadow:0 16px 44px rgba(0,0,0,.5), 0 0 0 4px rgba(47,107,255,.14); }
    .console-ico{ color:var(--muted); display:grid; place-items:center; }
    .console-input{ flex:1; background:transparent; border:0; outline:0; color:var(--text);
      font-family:'Manrope',sans-serif; font-size:15.5px; padding:12px 4px; }
    .console-input::placeholder{ color:#61708f; }
    .send{ flex:none; width:44px; height:44px; border-radius:13px; cursor:pointer; color:#fff; border:0;
      display:grid; place-items:center; background:linear-gradient(180deg, var(--blue), #175ae6);
      box-shadow:0 8px 20px rgba(47,107,255,.45); transition:transform .16s ease, filter .16s ease; }
    .send:hover:not(:disabled){ transform:translateY(-2px); filter:brightness(1.1); }
    .send:disabled{ opacity:.45; cursor:not-allowed; box-shadow:none; }
    .disclaimer{ text-align:center; color:#5c6b8a; font-size:12px; margin:12px 0 0; }

    /* load motion */
    .up{ opacity:0; transform:translateY(14px); animation:rise .6s cubic-bezier(.2,.7,.2,1) forwards; }
    .up.d1{ animation-delay:.06s } .up.d2{ animation-delay:.14s } .up.d3{ animation-delay:.22s } .up.d4{ animation-delay:.3s }
    @keyframes rise{ to{ opacity:1; transform:none } }

    @media (max-width:640px){
      .tiles{ grid-template-columns:1fr; }
      .brand-badge{ display:none; }
      .btn-calypso{ padding:10px 14px; font-size:13px; }
      .bg-car{ opacity:.6; }
    }
    @media (prefers-reduced-motion:reduce){ .up,.msg,.bg-glow{ animation:none; opacity:1; transform:none; } }
  `]
})
export class AiLandingComponent {
  draft = '';
  focused = false;
  thinking = false;
  messages: ChatMessage[] = [];

  readonly suggestions = [
    { q: 'Quand dois-je faire ma prochaine vidange ?', icon: this.ico('drop') },
    { q: 'Mon voyant moteur est allumé, que faire ?', icon: this.ico('warn') },
    { q: 'Quelle est la bonne pression pour mes pneus ?', icon: this.ico('tire') },
    { q: 'Comment réduire ma consommation de carburant ?', icon: this.ico('fuel') },
  ];

  constructor(private router: Router) {}

  goToCalypso(): void { this.router.navigate(['/login']); }

  ask(q: string): void { this.draft = q; this.send(); }

  send(): void {
    const q = this.draft.trim();
    if (!q || this.thinking) return;
    this.messages.push({ role: 'user', text: q });
    this.draft = '';
    this.thinking = true;
    // Interface-first stub. To wire the real AI, replace this timeout with an
    // HTTP call and push the response into `messages`.
    setTimeout(() => {
      this.thinking = false;
      this.messages.push({ role: 'ai', text: this.respond(q) });
    }, 850 + Math.min(q.length * 12, 700));
  }

  private respond(q: string): string {
    const s = q.toLowerCase();
    if (s.includes('vidange') || s.includes('huile'))
      return "En général : tous les 10 000–15 000 km (ou une fois par an) en essence, et 7 000–10 000 km en diesel — plus tôt en usage intensif. Le carnet d'entretien de votre modèle fait foi. 🔧";
    if (s.includes('voyant') || s.includes('warning') || (s.includes('moteur') && s.includes('allum')))
      return "Voyant moteur orange fixe = anomalie à diagnostiquer sans urgence ; clignotant = arrêtez-vous (risque moteur). Une lecture OBD des codes défaut donnera la cause exacte. ⚠️";
    if (s.includes('pneu') || s.includes('pression'))
      return "La pression recommandée est sur l'étiquette dans la portière conducteur ou la trappe à carburant (souvent 2,2–2,5 bar). Contrôlez à froid, une fois par mois. 🛞";
    if (s.includes('carburant') || s.includes('consomm') || s.includes('essence') || s.includes('gasoil') || s.includes('gazoil'))
      return "Pour consommer moins : anticipez le freinage, roulez à vitesse stable, gonflez bien les pneus, allégez le coffre et gardez le filtre à air propre. Calypso peut suivre votre consommation réelle, km par km. ⛽";
    if (s.includes('batterie'))
      return "Une batterie qui se vide vite = âge (>4 ans), trajets trop courts, accessoire resté allumé, ou alternateur faible. Un test de charge en atelier tranchera. 🔋";
    if (s.includes('p0') || s.includes('code défaut') || s.includes('obd') || s.includes('erreur'))
      return "Les codes P0xxx sont des défauts standardisés (ex. P0300 = ratés d'allumage multiples). Donnez-moi le code exact et je vous explique la cause probable et les vérifications. 🛠️";
    if (s.includes('frein'))
      return "Plaquettes : ~30 000 km selon la conduite ; disques ~2 jeux de plaquettes. Bruit métallique, vibration au freinage ou pédale molle = contrôle immédiat. 🛑";
    return "Bonne question ! 🚗 Je vous prépare une réponse claire et adaptée à votre véhicule. L'assistant IA complet (diagnostic, entretien, pièces, conduite) est en cours de connexion — ceci est un aperçu de l'interface.";
  }

  private ico(kind: string): string {
    const p: Record<string, string> = {
      drop: '<path d="M12 3s6 6.5 6 11a6 6 0 1 1-12 0C6 9.5 12 3 12 3Z" stroke="currentColor" stroke-width="1.7" stroke-linejoin="round"/>',
      warn: '<path d="M12 4l9 15H3l9-15Z" stroke="currentColor" stroke-width="1.7" stroke-linejoin="round"/><path d="M12 10v4M12 17h.01" stroke="currentColor" stroke-width="1.7" stroke-linecap="round"/>',
      tire: '<circle cx="12" cy="12" r="8.2" stroke="currentColor" stroke-width="1.7"/><circle cx="12" cy="12" r="3.2" stroke="currentColor" stroke-width="1.7"/><path d="M12 3.8v3M12 17.2v3M3.8 12h3M17.2 12h3" stroke="currentColor" stroke-width="1.7" stroke-linecap="round"/>',
      fuel: '<rect x="5" y="4" width="9" height="16" rx="1.6" stroke="currentColor" stroke-width="1.7"/><path d="M8 8h3M14 9l3 2v6a2 2 0 0 1-2 2" stroke="currentColor" stroke-width="1.7" stroke-linecap="round"/>',
    };
    return '<svg viewBox="0 0 24 24" fill="none">' + (p[kind] || '') + '</svg>';
  }
}
