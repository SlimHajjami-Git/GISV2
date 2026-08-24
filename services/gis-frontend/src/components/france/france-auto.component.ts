import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * Page 4 — Calypso Auto, l'assistant automobile public.
 *
 * <p>Le bouton renvoie vers <code>/assistant</code>, la page qui existe déjà
 * et qui interroge réellement le service. Aucune démonstration simulée : une
 * réponse d'assistant mise en scène dans une page vitrine laisse croire à une
 * qualité que seul le vrai service peut tenir.</p>
 */
@Component({
  selector: 'app-france-auto',
  standalone: true,
  imports: [RouterLink],
  template: `
    <section class="band-glow tight">
      <div class="shell">
        <div class="auto-hero rise">
          <div>
            <span class="pill">Assistant automobile</span>
            <h2 style="font-size:clamp(32px,4.6vw,48px);font-weight:800;letter-spacing:-.035em;line-height:1.1;margin:22px 0 14px">
              <span class="grad-txt">Calypso Auto</span>
            </h2>
            <p style="font-size:19px;font-weight:700;margin:0 0 12px">Votre assistant automobile intelligent</p>
            <p style="color:var(--txt-soft);margin:0 0 26px">
              Posez vos questions et obtenez des réponses claires sur l'entretien,
              les pannes, les voyants au tableau de bord ou la consommation de
              votre véhicule.
            </p>
            <div class="hero-cta">
              <a class="btn btn-grad" routerLink="/assistant">Poser une question</a>
            </div>
            <ul class="badges">
              <li>
                <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="2" aria-hidden="true"><circle cx="12" cy="12" r="9"/><path d="m8.5 12 2.5 2.5 4.5-5"/></svg>
                Gratuit et accessible à tous
              </li>
            </ul>
          </div>
          <div class="bot-wrap">
            <svg viewBox="0 0 200 200" fill="none" aria-hidden="true" style="width:100%;height:auto">
              <defs>
                <linearGradient id="bg1" x1="0" y1="0" x2="200" y2="200">
                  <stop offset="0" stop-color="#60A5FA"/>
                  <stop offset="1" stop-color="#A78BFA"/>
                </linearGradient>
              </defs>
              <circle cx="100" cy="100" r="92" fill="rgba(59,130,246,.14)"/>
              <circle cx="100" cy="100" r="70" fill="url(#bg1)"/>
              <rect x="66" y="78" width="68" height="52" rx="16" fill="#fff"/>
              <circle cx="86" cy="102" r="7" fill="#60A5FA"/>
              <circle cx="114" cy="102" r="7" fill="#60A5FA"/>
              <path d="M88 118h24" stroke="#60A5FA" stroke-width="4" stroke-linecap="round"/>
              <path d="M100 78V64" stroke="#fff" stroke-width="5" stroke-linecap="round"/>
              <circle cx="100" cy="59" r="7" fill="#34D399"/>
              <path d="M52 96a50 50 0 0 1 12-30M148 96a50 50 0 0 0-12-30" stroke="#60A5FA"
                    stroke-width="4" stroke-linecap="round"/>
            </svg>
          </div>
        </div>
      </div>
    </section>

    <section class="band-2 tight">
      <div class="shell">
        <div class="sec-head rise">
          <h2>Exemples de questions</h2>
          <p>Les sujets que l'assistant traite le plus souvent.</p>
        </div>
        <div class="chips rise" style="justify-content:center;max-width:820px;margin:0 auto">
          <span class="chip-q">Voyant moteur allumé, que faire ?</span>
          <span class="chip-q">Quand changer la courroie de distribution ?</span>
          <span class="chip-q">Pourquoi ma voiture consomme-t-elle plus ?</span>
          <span class="chip-q">Panne de démarrage à froid</span>
          <span class="chip-q">Pression des pneus recommandée</span>
          <span class="chip-q">Quel entretien à 100 000 km ?</span>
        </div>

        <div class="sec-head rise" style="margin-top:72px">
          <h2>Comment ça fonctionne ?</h2>
        </div>
        <div class="steps rise">
          <div class="step">
            <div class="n">01</div>
            <h4>Posez votre question</h4>
            <p>Décrivez le symptôme ou ce que vous cherchez à savoir, en français courant.</p>
          </div>
          <div class="step">
            <div class="n">02</div>
            <h4>Calypso analyse</h4>
            <p>L'assistant rapproche votre description des causes connues et des entretiens attendus.</p>
          </div>
          <div class="step">
            <div class="n">03</div>
            <h4>Obtenez une réponse</h4>
            <p>Une explication argumentée, avec les vérifications à effectuer en priorité.</p>
          </div>
        </div>
      </div>
    </section>

    <section class="band-glow">
      <div class="shell final">
        <h2>Prêt à essayer Calypso Auto ?</h2>
        <a class="btn btn-grad" routerLink="/assistant">Poser une question</a>
      </div>
    </section>
  `
})
export class FranceAutoComponent {}
