import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';

/**
 * Page 8 — Calypso Auto, l'expérience IA publique.
 *
 * <p><b>L'ordre est imposé et il a un sens.</b> Le véhicule se décrit AVANT que
 * la question ne soit posée : ces informations servent à contextualiser la
 * réponse, et les demander après coup obligerait à reformuler. Le document
 * maître fixe cet enchaînement.</p>
 *
 * <p><b>Aucun bouton « Enregistrer mon véhicule ».</b> C'est une interdiction
 * explicite, et elle est juste : rien n'est conservé ici. Un bouton
 * d'enregistrement promettrait une persistance qui n'existe pas sur une page
 * publique, et laisserait croire à la création d'un compte.</p>
 *
 * <p>Les champs sont tous FACULTATIFS. Exiger la marque avant d'autoriser la
 * moindre question transformerait une aide en formulaire, alors que beaucoup
 * de questions — un bruit de frein, un voyant — se répondent sans connaître le
 * modèle.</p>
 */
@Component({
  selector: 'app-france-auto',
  standalone: true,
  imports: [FormsModule, RouterLink],
  template: `
    <section class="band-glow tight">
      <div class="shell">
        <div class="auto-hero rise">
          <div>
            <h2 class="auto-title">Calypso <span class="grad-txt">Auto</span></h2>
            <p class="auto-sub">Votre assistant IA pour l'entretien et la réparation de votre véhicule</p>
            <p style="color:var(--txt-soft);margin:0">
              Posez vos questions, obtenez des conseils personnalisés et suivez les
              recommandations d'entretien adaptées à votre véhicule.
            </p>
          </div>
          <div class="bot-wrap">
            <svg viewBox="0 0 200 200" fill="none" aria-hidden="true" style="width:100%;height:auto">
              <defs>
                <linearGradient id="bg1" x1="0" y1="0" x2="200" y2="200">
                  <stop offset="0" stop-color="#3B82F6"/><stop offset="1" stop-color="#8B5CF6"/>
                </linearGradient>
              </defs>
              <circle cx="100" cy="100" r="88" fill="none" stroke="rgba(139,92,246,.35)" stroke-width="1"/>
              <circle cx="100" cy="100" r="70" fill="rgba(79,70,229,.14)"/>
              <circle cx="100" cy="100" r="58" fill="none" stroke="url(#bg1)" stroke-width="2"/>
              <path d="M62 108h76M70 108l4-14a6 6 0 0 1 5.7-4.2h40.6A6 6 0 0 1 126 94l4 14v14h-8v-6H78v6h-8z"
                    stroke="url(#bg1)" stroke-width="3" stroke-linecap="round" stroke-linejoin="round" fill="none"/>
              <circle cx="82" cy="116" r="3.5" fill="#A78BFA"/>
              <circle cx="118" cy="116" r="3.5" fill="#A78BFA"/>
            </svg>
          </div>
        </div>
      </div>
    </section>

    <section class="band-2 tight" style="padding-top:0">
      <div class="shell">
        <!-- ÉTAPE 1 — le véhicule, avant la question. -->
        <div class="panel-block rise">
          <h3>Décrivez votre véhicule pour des réponses plus précises</h3>
          <div class="veh-grid">
            <div class="veh-field">
              <label for="v-marque">Marque</label>
              <input id="v-marque" name="marque" type="text" [(ngModel)]="marque"
                     placeholder="Ex. : Renault" autocomplete="off">
            </div>
            <div class="veh-field">
              <label for="v-modele">Modèle</label>
              <input id="v-modele" name="modele" type="text" [(ngModel)]="modele"
                     placeholder="Ex. : Clio" autocomplete="off">
            </div>
            <div class="veh-field">
              <label for="v-annee">Année</label>
              <div class="select-wrap">
                <select id="v-annee" name="annee" [(ngModel)]="annee">
                  <option value="">Sélectionnez l'année</option>
                  @for (a of years; track a) { <option [value]="a">{{ a }}</option> }
                </select>
              </div>
            </div>
            <div class="veh-field">
              <label for="v-carb">Carburant</label>
              <div class="select-wrap">
                <select id="v-carb" name="carburant" [(ngModel)]="carburant">
                  <option value="">Sélectionnez le carburant</option>
                  @for (c of fuels; track c) { <option [value]="c">{{ c }}</option> }
                </select>
              </div>
            </div>
            <div class="veh-field">
              <label for="v-km">Kilométrage</label>
              <input id="v-km" name="km" type="text" [(ngModel)]="kilometrage"
                     placeholder="Ex. : 85 000 km" inputmode="numeric" autocomplete="off">
            </div>
          </div>
        </div>

        <!-- ÉTAPE 2 — la question, une fois le contexte donné. -->
        <div class="panel-block rise">
          <h3>Demandez à Calypso Auto</h3>
          <p class="panel-lede">Notre IA est là pour vous aider.</p>

          <form class="ask" (ngSubmit)="ask($event)">
            <input type="text" [(ngModel)]="question" name="question"
                   placeholder="Exemple : Ma voiture consomme plus que d'habitude, que vérifier ?"
                   [disabled]="loading" aria-label="Votre question">
            <button type="submit" [disabled]="loading || !question.trim()" aria-label="Envoyer">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
                   stroke-linecap="round" stroke-linejoin="round"><path d="m22 2-7 20-4-9-9-4z"/></svg>
            </button>
          </form>

          <div class="chips" style="justify-content:flex-start;margin-top:16px">
            @for (c of shortcuts; track c) {
              <button type="button" class="chip-q" (click)="pick(c)" [disabled]="loading">{{ c }}</button>
            }
          </div>

          @if (loading) {
            <p class="answer-wait" role="status">Calypso Auto analyse votre question…</p>
          }
          @if (answer) {
            <div class="answer" role="region" aria-label="Réponse de Calypso Auto">{{ answer }}</div>
          }
          @if (error) {
            <p class="legal-warn" style="margin-top:18px" role="status">{{ error }}</p>
          }
        </div>

        <div class="cards rise" style="margin-top:44px">
          <div class="card">
            <div class="ic"><svg viewBox="0 0 24 24" width="21" height="21" fill="none" stroke="#A78BFA" stroke-width="1.8"><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/></svg></div>
            <h3>Réponses instantanées</h3>
            <p>Obtenez des conseils clairs en quelques secondes.</p>
          </div>
          <div class="card">
            <div class="ic"><svg viewBox="0 0 24 24" width="21" height="21" fill="none" stroke="#60A5FA" stroke-width="1.8"><path d="M3 13h18M5 13l1.6-5.2A2 2 0 0 1 8.5 6.4h7a2 2 0 0 1 1.9 1.4L19 13v5h-2.2v-2H7.2v2H5z"/></svg></div>
            <h3>Conseils personnalisés</h3>
            <p>Des recommandations adaptées à votre véhicule et à son usage.</p>
          </div>
          <div class="card">
            <div class="ic"><svg viewBox="0 0 24 24" width="21" height="21" fill="none" stroke="#34D399" stroke-width="1.8"><path d="M6 3.5h7.5L18 8v12.5H6z"/><path d="M13.5 3.5V8H18M9 12.5h6M9 16h4"/></svg></div>
            <h3>Entretien simplifié</h3>
            <p>Suivez les bonnes pratiques pour garder votre véhicule en parfait état.</p>
          </div>
        </div>
      </div>
    </section>

    <section class="band-glow">
      <div class="shell final">
        <h2>Essayez Calypso gratuitement pendant 7 jours</h2>
        <a class="btn btn-grad" routerLink="/inscription">Essayer gratuitement</a>
      </div>
    </section>
  `
})
export class FranceAutoComponent {
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

  readonly fuels = ['Essence', 'Diesel', 'Hybride', 'Électrique', 'GPL'];

  /** Vingt-cinq millésimes, du plus récent au plus ancien. */
  readonly years = Array.from({ length: 26 }, (_, i) => 2026 - i);

  readonly shortcuts = [
    'Voyant moteur allumé',
    'Bruit de frein',
    'Révision à faire ?',
    'Pneu qui s\'use vite',
    'Consommation élevée'
  ];

  pick(c: string): void {
    this.question = c;
  }

  ask(event: Event): void {
    event.preventDefault();
    const q = this.question.trim();
    if (!q || this.loading) return;

    this.loading = true;
    this.answer = '';
    this.error = '';

    // Le contexte véhicule précède la question dans le même message : le service
    // public ne conserve rien entre deux appels, la transmettre séparément la
    // perdrait.
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
      ? `Mon véhicule : ${bits.join(', ')}.\n\n${q}`
      : q;
  }
}
