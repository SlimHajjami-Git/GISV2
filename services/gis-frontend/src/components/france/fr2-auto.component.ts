import { Component, inject, ViewEncapsulation } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { Fr2HeaderComponent, Fr2FooterComponent } from './fr2-chrome.component';

/**
 * CALYPSO AUTO — reproduction de ECRANS SITE WEB CALYPSO/CALYPSO AUTO.
 *
 * Maquette de reference : 1024 x 1536. Reperes mesures : illustration
 * x 50..418, y 90..480 (decoupee de la capture : assets/fr-auto-hero2.png) ;
 * titre y 190..247, sous-titre 265..318, paragraphe 345..416 ; panneaux
 * x 25..999 — description vehicule 479..690 (champs 608..660), question
 * 712..1010 (saisie 820..905, raccourcis 930..980), avantages 1034..1262,
 * appel final 1287..1415 ; pied en ligne a 1447.
 *
 * La logique (contexte vehicule + question -> POST /assistant/ask) est reprise
 * de l'ancienne page : le service public ne conserve rien entre deux appels,
 * le contexte precede donc la question dans le meme message.
 *
 * « Modele » reste un champ libre sous l'apparence de la maquette : une liste
 * fermee de modeles serait forcement fausse, le chevron est un ornement.
 */
@Component({
  selector: 'app-fr2-auto',
  standalone: true,
  imports: [RouterLink, FormsModule, Fr2HeaderComponent, Fr2FooterComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="fr2-page">
      <app-fr2-header [accueil]="true" />
      <div class="fa2">

        <!-- ══ HERO : illustration decoupee + titre ══ -->
        <section class="fa2-hero">
          <img class="art" src="/assets/fr-auto-hero2.png" alt="Calypso Auto, assistant automobile">
          <div class="tx">
            <h1>Calypso <span class="g">Auto</span></h1>
            <p class="sub">Votre assistant IA pour l'entretien et la<br>réparation de votre véhicule</p>
            <p class="lede">
              Posez vos questions, obtenez des conseils personnalisés<br>
              et suivez les recommandations d'entretien adaptées<br>
              à votre véhicule.
            </p>
          </div>
        </section>

        <!-- ══ DESCRIPTION DU VÉHICULE ══ -->
        <section class="fa2-panel fa2-desc">
          <h2>Décrivez votre véhicule pour des réponses plus précises</h2>
          <div class="fields">
            <div class="f">
              <label for="fa2-marque"><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.8"><path d="M4 13l1.5-4.5A2 2 0 0 1 7.4 7h9.2a2 2 0 0 1 1.9 1.5L20 13v5h-2v-1.5H6V18H4z"/><circle cx="7.5" cy="14.5" r="1"/><circle cx="16.5" cy="14.5" r="1"/></svg>Marque</label>
              <div class="in">
                <select id="fa2-marque" name="marque" [(ngModel)]="marque">
                  <option value="">Sélectionnez la marque</option>
                  @for (b of brands; track b) { <option [value]="b">{{ b }}</option> }
                </select>
                <span class="chev"></span>
              </div>
            </div>
            <div class="f">
              <label for="fa2-modele"><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.8"><path d="M4 13l1.5-4.5A2 2 0 0 1 7.4 7h9.2a2 2 0 0 1 1.9 1.5L20 13v5h-2v-1.5H6V18H4z"/><circle cx="7.5" cy="14.5" r="1"/><circle cx="16.5" cy="14.5" r="1"/></svg>Modèle</label>
              <div class="in">
                <input id="fa2-modele" name="modele" [(ngModel)]="modele" placeholder="Sélectionnez le modèle">
                <span class="chev"></span>
              </div>
            </div>
            <div class="f">
              <label for="fa2-annee"><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.8"><rect x="4" y="5" width="16" height="16" rx="2"/><path d="M4 10h16M9 3v4M15 3v4"/></svg>Année</label>
              <div class="in">
                <select id="fa2-annee" name="annee" [(ngModel)]="annee">
                  <option value="">Sélectionnez l'année</option>
                  @for (y of years; track y) { <option [value]="y">{{ y }}</option> }
                </select>
                <span class="chev"></span>
              </div>
            </div>
            <div class="f">
              <label for="fa2-carb"><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.8"><path d="M5 20V6a2 2 0 0 1 2-2h5a2 2 0 0 1 2 2v14M4 20h11M14 11h2.5a2 2 0 0 1 2 2v3a1.5 1.5 0 0 0 3 0V9l-2.5-2.5"/></svg>Carburant</label>
              <div class="in">
                <select id="fa2-carb" name="carburant" [(ngModel)]="carburant">
                  <option value="">Sélectionnez le carburant</option>
                  @for (f of fuels; track f) { <option [value]="f">{{ f }}</option> }
                </select>
                <span class="chev"></span>
              </div>
            </div>
            <div class="f">
              <label for="fa2-km"><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.8"><circle cx="12" cy="13" r="8"/><path d="M12 13l3.5-3.5M9 3.5h6"/></svg>Kilométrage</label>
              <div class="in">
                <input id="fa2-km" name="kilometrage" [(ngModel)]="kilometrage" placeholder="Ex. : 85 000 km" inputmode="numeric">
              </div>
            </div>
          </div>
        </section>

        <!-- ══ QUESTION ══ -->
        <section class="fa2-panel fa2-ask">
          <h2>Demandez à Calypso Auto</h2>
          <p class="hint">Notre IA est là pour vous aider.</p>
          <form (ngSubmit)="ask($event)">
            <div class="box">
              <textarea name="question" [(ngModel)]="question" rows="2"
                        placeholder="Exemple : Ma voiture consomme plus que d'habitude, que vérifier ?"></textarea>
              <button type="submit" [disabled]="loading" aria-label="Envoyer la question">
                <svg viewBox="0 0 24 24" fill="none" stroke="#7C3AED" stroke-width="2"><path d="M21 3 10.5 13.5M21 3l-6.8 18-3.7-7.5L3 9.8z"/></svg>
              </button>
            </div>
          </form>
          <div class="chips">
            @for (c of shortcuts; track c.t) {
              <button type="button" (click)="pick(c.t)">
                @switch (c.k) {
                  @case ('star') { <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.7"><path d="m12 3 2.7 5.6 6.3.8-4.6 4.2 1.2 6.1L12 16.8 6.4 19.7l1.2-6.1L3 9.4l6.3-.8z"/></svg> }
                  @case ('brake') { <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.7"><circle cx="12" cy="12" r="4"/><path d="M12 2a10 10 0 0 1 7.7 3.6M12 2a10 10 0 0 0-7.7 3.6M4.3 18.4A10 10 0 0 0 12 22a10 10 0 0 0 7.7-3.6"/></svg> }
                  @case ('bubble') { <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.7"><path d="M20 12a8 8 0 1 0-3.1 6.3L21 20l-1.3-3.6A8 8 0 0 0 20 12z"/><path d="M9 11h6M9 14h3.5"/></svg> }
                  @case ('tire') { <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.7"><circle cx="12" cy="12" r="9"/><circle cx="12" cy="12" r="3.4"/><path d="M12 3v5.6M12 15.4V21M3 12h5.6M15.4 12H21"/></svg> }
                  @case ('gauge') { <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.7"><circle cx="12" cy="12" r="9"/><path d="M12 12l4-4M12 7v.01M7 12h.01M17 12h.01"/></svg> }
                }
                {{ c.t }}
              </button>
            }
          </div>
          @if (loading) { <div class="ans wait">Calypso Auto réfléchit…</div> }
          @if (answer) { <div class="ans">{{ answer }}</div> }
          @if (error) { <div class="ans err">{{ error }}</div> }
        </section>

        <!-- ══ AVANTAGES : 4 colonnes separees ══ -->
        <section class="fa2-panel fa2-why">
          @for (b of benefits; track b.t) {
            <div class="col">
              <span class="ring">
                @switch (b.k) {
                  @case ('chat') { <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.6"><path d="M20 11.5a8 8 0 0 1-11.9 7L4 20l1.5-4.1A8 8 0 1 1 20 11.5z"/></svg> }
                  @case ('car') { <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.6"><path d="M4 13l1.5-4.5A2 2 0 0 1 7.4 7h9.2a2 2 0 0 1 1.9 1.5L20 13v5h-2v-1.5H6V18H4z"/><circle cx="7.5" cy="14.5" r="1"/><circle cx="16.5" cy="14.5" r="1"/></svg> }
                  @case ('list') { <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.6"><rect x="6" y="4" width="12" height="17" rx="2"/><path d="M9.5 4.5h5M9 10.5l1.5 1.5 3-3M9 15.5l1.5 1.5 3-3" /></svg> }
                  @case ('shield') { <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.6"><path d="M12 3l7.5 3v6c0 4.4-3.1 7.6-7.5 9-4.4-1.4-7.5-4.6-7.5-9V6z"/><path d="m9 12 2 2 4-4.5"/></svg> }
                }
              </span>
              <h3>{{ b.t }}</h3>
              <p>{{ b.d }}</p>
            </div>
          }
        </section>

        <!-- ══ APPEL FINAL ══ -->
        <section class="fa2-panel fa2-cta">
          <span class="cal"><svg viewBox="0 0 24 24" fill="none" stroke="#C4B5FD" stroke-width="1.6"><rect x="4" y="5" width="16" height="16" rx="2"/><path d="M4 10h16M9 3v4M15 3v4"/></svg></span>
          <div class="tx">
            <h2>Essayez Calypso Auto gratuitement pendant 7 jours</h2>
            <p>Accédez à toutes les fonctionnalités IA. Sans carte bancaire. Sans engagement.</p>
          </div>
          <div class="bt">
            <a routerLink="/inscription" class="b1">Essayer gratuitement</a>
            <a routerLink="/fr/fonctionnalites" class="b2">En savoir plus</a>
          </div>
        </section>

      </div>
      <app-fr2-footer variant="inline" />
    </div>
  `,
  styles: [`
    .fa2 {
      /* Maquette a 1024 px, meme plafond que l'accueil. */
      --w: min(0.09766cqw, 1.367px);
      display: block; position: relative; z-index: 1;
      font-family: Manrope, system-ui, -apple-system, "Segoe UI", sans-serif;
      padding-bottom: calc(32 * var(--w));
    }

    /* ── HERO : illustration x 50..460 (y 90..480), texte des x 425 ── */
    .fa2-hero { position: relative; min-height: calc(422 * var(--w)); }
    .fa2-hero .art {
      position: absolute; left: 4.883cqw; top: calc(32 * var(--w));
      width: calc(368 * var(--w)); height: auto;
    }
    .fa2-hero .tx { padding: calc(100 * var(--w)) 4.883cqw 0 46.4cqw; }
    .fa2 h1 { margin: 0; font-size: calc(60 * var(--w)); line-height: 1.08; font-weight: 800; letter-spacing: -.03em; }
    .fa2 h1 .g { background: linear-gradient(90deg,#8B5CF6,#A78BFA);
      -webkit-background-clip: text; background-clip: text; color: transparent; }
    .fa2-hero .sub { margin: calc(20 * var(--w)) 0 0; font-size: calc(21 * var(--w)); line-height: calc(32 * var(--w)); font-weight: 700; }
    .fa2-hero .lede { margin: calc(20 * var(--w)) 0 0; font-size: calc(16 * var(--w)); line-height: calc(29 * var(--w)); color: #9AA7BD; }

    /* ── Panneaux : x 25..999 ── */
    .fa2-panel {
      width: 95.1cqw; margin: 0 auto calc(22 * var(--w));
      background: rgba(255,255,255,.028); border: 1px solid rgba(255,255,255,.075);
      border-radius: calc(14 * var(--w));
    }
    .fa2 h2 { margin: 0; font-size: calc(20 * var(--w)); font-weight: 800; letter-spacing: -.01em; }

    /* ── Description : titre y 512, champs 608..660 ── */
    .fa2-desc { padding: calc(32 * var(--w)) calc(24 * var(--w)) calc(30 * var(--w)); }
    .fields { display: grid; grid-template-columns: repeat(5, 1fr); gap: calc(14 * var(--w)); margin-top: calc(30 * var(--w)); }
    .f label {
      display: flex; align-items: center; gap: calc(8 * var(--w));
      font-size: calc(14 * var(--w)); font-weight: 700; margin-bottom: calc(12 * var(--w));
    }
    .f label svg { width: calc(19 * var(--w)); height: calc(19 * var(--w)); flex: none; }
    .f .in { position: relative; }
    .f select, .f input {
      width: 100%; height: calc(52 * var(--w));
      background: rgba(255,255,255,.03); border: 1px solid rgba(255,255,255,.14);
      border-radius: calc(10 * var(--w)); color: #C7D2E4;
      font: inherit; font-size: calc(13 * var(--w));
      padding: 0 calc(30 * var(--w)) 0 calc(12 * var(--w));
      appearance: none; outline: none;
    }
    .f input::placeholder { color: #6B7A94; }
    .f select:focus, .f input:focus { border-color: rgba(167,139,250,.6); }
    .f select option { background: #0B1020; color: #E7ECF5; }
    .f .chev {
      position: absolute; right: calc(12 * var(--w)); top: 50%;
      width: calc(8 * var(--w)); height: calc(8 * var(--w));
      border-right: 1.5px solid #9AA7BD; border-bottom: 1.5px solid #9AA7BD;
      transform: translateY(-70%) rotate(45deg); pointer-events: none;
    }

    /* ── Question : saisie blanche 820..905, raccourcis 930..980 ── */
    .fa2-ask { padding: calc(30 * var(--w)) calc(24 * var(--w)) calc(28 * var(--w)); }
    .fa2-ask .hint { margin: calc(12 * var(--w)) 0 0; font-size: calc(14.5 * var(--w)); color: #9AA7BD; }
    .fa2-ask .box {
      position: relative; margin-top: calc(18 * var(--w));
      background: #F2F3F7; border-radius: calc(12 * var(--w));
      min-height: calc(85 * var(--w));
    }
    .fa2-ask textarea {
      width: 100%; min-height: calc(85 * var(--w)); resize: vertical;
      background: transparent; border: 0; outline: none;
      font: inherit; font-size: calc(14.5 * var(--w)); color: #1E2433;
      padding: calc(26 * var(--w)) calc(64 * var(--w)) calc(14 * var(--w)) calc(20 * var(--w));
    }
    .fa2-ask textarea::placeholder { color: #7A8497; }
    .fa2-ask .box button {
      position: absolute; right: calc(18 * var(--w)); top: 50%; transform: translateY(-50%);
      background: none; border: 0; cursor: pointer; padding: calc(6 * var(--w));
    }
    .fa2-ask .box svg { width: calc(26 * var(--w)); height: calc(26 * var(--w)); display: block; }
    .chips { display: flex; gap: calc(14 * var(--w)); margin-top: calc(20 * var(--w)); }
    .chips button {
      flex: 1; height: calc(50 * var(--w));
      display: inline-flex; align-items: center; justify-content: center; gap: calc(9 * var(--w));
      background: rgba(255,255,255,.03); border: 1px solid rgba(255,255,255,.12);
      border-radius: calc(10 * var(--w)); cursor: pointer;
      font: inherit; font-size: calc(13 * var(--w)); font-weight: 600; color: #C7D2E4;
      white-space: nowrap; transition: border-color .15s;
    }
    .chips button:hover { border-color: rgba(167,139,250,.5); }
    .chips svg { width: calc(17 * var(--w)); height: calc(17 * var(--w)); flex: none; }
    .ans {
      margin-top: calc(22 * var(--w)); padding: calc(18 * var(--w)) calc(20 * var(--w));
      background: rgba(139,92,246,.07); border: 1px solid rgba(167,139,250,.3);
      border-radius: calc(10 * var(--w));
      font-size: calc(14.5 * var(--w)); line-height: 1.65; color: #E7ECF5; white-space: pre-wrap;
    }
    .ans.wait { color: #9AA7BD; font-style: italic; }
    .ans.err { border-color: rgba(244,63,94,.4); background: rgba(244,63,94,.07); color: #FDA4AF; }

    /* ── Avantages : y 1034..1262, 4 colonnes separees d'un trait ── */
    .fa2-why { display: grid; grid-template-columns: repeat(4, 1fr); padding: calc(20 * var(--w)) 0 calc(22 * var(--w)); }
    .fa2-why .col { padding: 0 calc(26 * var(--w)); text-align: center; }
    .fa2-why .col + .col { border-left: 1px solid rgba(255,255,255,.08); }
    .fa2-why .ring {
      width: calc(76 * var(--w)); height: calc(76 * var(--w)); border-radius: 50%;
      margin: 0 auto; display: grid; place-items: center;
      background: rgba(139,92,246,.1); border: 1px solid rgba(167,139,250,.35);
    }
    .fa2-why .ring svg { width: calc(34 * var(--w)); height: calc(34 * var(--w)); }
    .fa2-why h3 { margin: calc(17 * var(--w)) 0 0; font-size: calc(16 * var(--w)); font-weight: 800; }
    .fa2-why p { margin: calc(9 * var(--w)) 0 0; font-size: calc(13.5 * var(--w)); line-height: calc(21 * var(--w)); color: #9AA7BD; }

    /* ── Appel final : y 1287..1415 ── */
    .fa2-cta {
      min-height: calc(128 * var(--w));
      display: flex; align-items: center; gap: calc(22 * var(--w));
      padding: 0 calc(30 * var(--w)); margin-bottom: 0;
    }
    .fa2-cta .cal {
      width: calc(64 * var(--w)); height: calc(64 * var(--w)); border-radius: 50%; flex: none;
      background: rgba(139,92,246,.12); border: 1px solid rgba(167,139,250,.35);
      display: grid; place-items: center;
    }
    .fa2-cta .cal svg { width: calc(30 * var(--w)); height: calc(30 * var(--w)); }
    .fa2-cta h2 { font-size: calc(21 * var(--w)); }
    .fa2-cta .tx p { margin: calc(8 * var(--w)) 0 0; font-size: calc(13.5 * var(--w)); color: #9AA7BD; }
    .fa2-cta .bt { margin-left: auto; display: flex; gap: calc(14 * var(--w)); }
    .fa2-cta .b1, .fa2-cta .b2 {
      display: inline-flex; align-items: center; justify-content: center;
      height: calc(46 * var(--w)); padding: 0 calc(24 * var(--w)); border-radius: calc(9 * var(--w));
      font-size: calc(13.5 * var(--w)); font-weight: 700; white-space: nowrap;
    }
    .fa2-cta .b1 { background: linear-gradient(135deg,#4F46E5,#7C3AED); color: #fff; }
    .fa2-cta .b2 { border: 1px solid rgba(255,255,255,.18); color: #fff; }

    @media (max-width: 820px) {
      .fa2-hero { min-height: 0; }
      .fa2-hero .art { position: static; display: block; margin: calc(24 * var(--w)) auto 0; }
      .fa2-hero .tx { padding: calc(24 * var(--w)) 4.883cqw 0; text-align: center; }
      .fields { grid-template-columns: 1fr 1fr; }
      .chips { flex-wrap: wrap; }
      .chips button { flex: 1 1 40%; }
      .fa2-why { grid-template-columns: 1fr 1fr; gap: calc(26 * var(--w)) 0; }
      .fa2-why .col:nth-child(3) { border-left: 0; }
      .fa2-cta { flex-wrap: wrap; padding: calc(20 * var(--w)); }
      .fa2-cta .bt { margin-left: 0; width: 100%; }
      .fa2-cta .b1, .fa2-cta .b2 { flex: 1; }
    }
  `]
})
export class Fr2AutoComponent {
  private readonly http = inject(HttpClient);

  marque = '';
  modele = '';
  annee = '';
  carburant = '';
  kilometrage = '';

  question = '';
  answer = '';
  error = '';
  loading = false;

  readonly brands = [
    'Audi', 'BMW', 'Citroën', 'Dacia', 'Fiat', 'Ford', 'Hyundai', 'Kia',
    'Mercedes-Benz', 'Nissan', 'Opel', 'Peugeot', 'Renault', 'Seat', 'Škoda',
    'Suzuki', 'Toyota', 'Volkswagen', 'Volvo', 'Autre'
  ];
  readonly fuels = ['Essence', 'Diesel', 'Hybride', 'Électrique', 'GPL'];
  /** Vingt-cinq millésimes, du plus récent au plus ancien. */
  readonly years = Array.from({ length: 26 }, (_, i) => 2026 - i);

  readonly shortcuts = [
    { k: 'star',   t: 'Voyant moteur allumé' },
    { k: 'brake',  t: 'Bruit de frein' },
    { k: 'bubble', t: 'Révision à faire ?' },
    { k: 'tire',   t: "Pneu qui s'use vite" },
    { k: 'gauge',  t: 'Consommation élevée' }
  ];

  readonly benefits = [
    { k: 'chat',   t: 'Réponses instantanées',  d: 'Obtenez des conseils clairs en quelques secondes.' },
    { k: 'car',    t: 'Conseils personnalisés', d: 'Des recommandations adaptées à votre véhicule et son usage.' },
    { k: 'list',   t: 'Entretien simplifié',    d: 'Suivez les bonnes pratiques pour garder votre véhicule en parfait état.' },
    { k: 'shield', t: 'Fiable et sécurisé',     d: 'Des informations issues de bases techniques reconnues.' }
  ];

  pick(c: string): void { this.question = c; }

  ask(event: Event): void {
    event.preventDefault();
    const q = this.question.trim();
    if (!q || this.loading) return;

    this.loading = true;
    this.answer = '';
    this.error = '';

    // Le contexte véhicule précède la question dans le même message : le
    // service public ne conserve rien entre deux appels.
    this.http.post<{ answer?: string; message?: string }>(
      `${environment.apiUrl}/assistant/ask`, { message: this.withContext(q) }
    ).subscribe({
      next: (r) => {
        this.loading = false;
        this.answer = (r?.answer || r?.message || '').trim()
          || "Aucune réponse n'a pu être produite. Reformulez votre question.";
      },
      error: () => {
        this.loading = false;
        this.error = "L'assistant est momentanément indisponible. Réessayez dans un instant.";
      }
    });
  }

  /** Préfixe la question des informations saisies, en ignorant les champs vides. */
  private withContext(q: string): string {
    const bits: string[] = [];
    if (this.marque.trim()) bits.push(`marque ${this.marque.trim()}`);
    if (this.modele.trim()) bits.push(`modèle ${this.modele.trim()}`);
    if (this.annee) bits.push(`année ${this.annee}`);
    if (this.carburant) bits.push(this.carburant.toLowerCase());
    if (this.kilometrage.trim()) bits.push(`${this.kilometrage.trim()} au compteur`);

    return bits.length
      ? `Mon véhicule : ${bits.join(', ')}. ${q}`
      : q;
  }
}
