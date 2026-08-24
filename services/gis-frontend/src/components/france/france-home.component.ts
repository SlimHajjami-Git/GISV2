import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * Page 1 — Accueil, d'après la maquette sombre : pilule d'accroche, titre à
 * trois temps dont la ligne centrale en dégradé, mention IA, deux boutons,
 * trois garanties, visuel orbital, puis le bandeau des six univers.
 */
@Component({
  selector: 'app-france-home',
  standalone: true,
  imports: [RouterLink],
  template: `
    <section class="hero band-glow">
      <div class="shell hero-grid">
        <div>
          <span class="pill">Logiciel de gestion de parc automobile</span>
          <h1>
            Gérez votre parc.<br>
            <span class="grad-txt">Maîtrisez vos coûts.</span><br>
            Anticipez vos entretiens.
          </h1>
          <p class="lede">
            Calypso centralise vos véhicules, entretiens, réparations, carburant,
            dépenses et échéances dans une plateforme intelligente conçue pour
            simplifier la gestion de votre parc.
          </p>
          <p class="ai-line">
            <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="M12 3l1.9 5.1L19 10l-5.1 1.9L12 17l-1.9-5.1L5 10l5.1-1.9z"/><path d="M18.5 15.5l.8 2.2 2.2.8-2.2.8-.8 2.2-.8-2.2-2.2-.8 2.2-.8z"/></svg>
            Intelligence artificielle intégrée
          </p>
          <div class="hero-cta">
            <a class="btn btn-grad" routerLink="/inscription">Essayer gratuitement</a>
            <a class="btn btn-line" routerLink="/fr/fonctionnalites">Découvrir Calypso</a>
          </div>
          <ul class="badges">
            <li>
              <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="2" aria-hidden="true"><circle cx="12" cy="12" r="9"/><path d="m8.5 12 2.5 2.5 4.5-5"/></svg>
              7 jours gratuits
            </li>
            <li>
              <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="2" aria-hidden="true"><circle cx="12" cy="12" r="9"/><path d="m8.5 12 2.5 2.5 4.5-5"/></svg>
              Sans carte bancaire
            </li>
            <li>
              <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="2" aria-hidden="true"><circle cx="12" cy="12" r="9"/><path d="m8.5 12 2.5 2.5 4.5-5"/></svg>
              Sans engagement
            </li>
          </ul>
        </div>

        <div class="orbit" aria-label="Aperçu des indicateurs suivis par Calypso">
          <div class="rings" aria-hidden="true">
            <span class="halo"></span>
            <span class="ring r3"></span>
            <span class="ring r2"></span>
            <span class="ring r1"></span>
          </div>

          <div class="orbit-core">
            <svg class="mark" viewBox="0 0 32 32" fill="none" aria-hidden="true">
              <defs>
                <linearGradient id="cg2" x1="2" y1="2" x2="30" y2="30">
                  <stop offset="0" stop-color="#3B82F6"/>
                  <stop offset="1" stop-color="#8B5CF6"/>
                </linearGradient>
              </defs>
              <path d="M25.5 8A11 11 0 1 0 25.5 24" stroke="url(#cg2)" stroke-width="6"
                    stroke-linecap="round" fill="none"/>
            </svg>
            <span>CALYPSO</span>
          </div>

          <div class="o-card c1">
            <div class="ic" style="background:rgba(59,130,246,.14)">
              <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="2" aria-hidden="true"><path d="M14.7 6.3a4 4 0 0 0 4.6 5.7l-8.3 8.3a2 2 0 0 1-2.8-2.8l8.3-8.3a4 4 0 0 0-1.8-2.9z"/></svg>
            </div>
            <div>
              <span class="k">Entretien</span>
              <span class="v">Révision dans</span>
              <span class="m" style="color:#60A5FA">1 250 km</span>
            </div>
          </div>

          <div class="o-card c2">
            <div class="ic" style="background:rgba(52,211,153,.14)">
              <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="#34D399" stroke-width="2" aria-hidden="true"><path d="M4 20V5a2 2 0 0 1 2-2h6a2 2 0 0 1 2 2v15M3 20h13M17 9l2.5 2.5V17a1.5 1.5 0 0 1-3 0V9z"/></svg>
            </div>
            <div>
              <span class="k">Carburant</span>
              <span class="v">Consommation moyenne</span>
              <span class="m" style="color:#34D399">6,8 L/100 km</span>
            </div>
          </div>

          <div class="o-card c3">
            <div class="ic" style="background:rgba(139,92,246,.16)">
              <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="2" aria-hidden="true"><rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4"/></svg>
            </div>
            <div>
              <span class="k">Échéance</span>
              <span class="v">Contrôle technique</span>
              <span class="m" style="color:#A78BFA">dans 18 jours</span>
            </div>
          </div>

          <div class="o-card c4">
            <div class="ic" style="background:rgba(245,158,11,.14)">
              <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="#F59E0B" stroke-width="2" aria-hidden="true"><path d="M3 13h18M5 13l1.5-5A2 2 0 0 1 8.4 6.5h7.2a2 2 0 0 1 1.9 1.5L19 13v5h-2v-2H7v2H5z"/></svg>
            </div>
            <div>
              <span class="k">Réparation</span>
              <span class="v">2 interventions</span>
              <span class="m" style="color:#F59E0B">en cours</span>
            </div>
          </div>

          <div class="o-card c5">
            <div class="ic" style="background:rgba(236,72,153,.14)">
              <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="#EC4899" stroke-width="2" aria-hidden="true"><path d="M9 4a3 3 0 0 0-3 3 3 3 0 0 0-1 5.8A3 3 0 0 0 8 18a3 3 0 0 0 4-1V4.9A3 3 0 0 0 9 4zM15 4a3 3 0 0 1 3 3 3 3 0 0 1 1 5.8A3 3 0 0 1 16 18a3 3 0 0 1-4-1"/></svg>
            </div>
            <div>
              <span class="k">Calypso IA</span>
              <span class="v">Anomalie détectée</span>
              <span class="m" style="color:#EC4899">Analyse disponible</span>
            </div>
          </div>

          <div class="o-card c6">
            <div class="ic" style="background:rgba(99,102,241,.16)">
              <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="#818CF8" stroke-width="2" aria-hidden="true"><path d="M3 13h18M5 13l1.5-5A2 2 0 0 1 8.4 6.5h7.2a2 2 0 0 1 1.9 1.5L19 13v5h-2v-2H7v2H5z"/></svg>
            </div>
            <div>
              <span class="k">Parc</span>
              <span class="v">12 véhicules</span>
              <span class="m" style="color:#818CF8">suivis</span>
            </div>
          </div>
        </div>
      </div>

      <div class="strip-wrap">
        <div class="chevron" aria-hidden="true">
          <span>
            <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="2.4"><path d="m6 9 6 6 6-6"/></svg>
          </span>
        </div>
        <div class="shell">
          <div class="strip">
            <a routerLink="/fr/fonctionnalites">
              <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="1.7" aria-hidden="true"><path d="M14.7 6.3a4 4 0 0 0 4.6 5.7l-8.3 8.3a2 2 0 0 1-2.8-2.8l8.3-8.3a4 4 0 0 0-1.8-2.9z"/></svg>
              <span>Entretiens</span>
            </a>
            <a routerLink="/fr/fonctionnalites">
              <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="1.7" aria-hidden="true"><path d="M3 13h18M5 13l1.5-5A2 2 0 0 1 8.4 6.5h7.2a2 2 0 0 1 1.9 1.5L19 13v5h-2v-2H7v2H5z"/></svg>
              <span>Réparations</span>
            </a>
            <a routerLink="/fr/fonctionnalites">
              <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="1.7" aria-hidden="true"><path d="M4 20V5a2 2 0 0 1 2-2h6a2 2 0 0 1 2 2v15M3 20h13M17 9l2.5 2.5V17a1.5 1.5 0 0 1-3 0V9z"/></svg>
              <span>Carburant</span>
            </a>
            <a routerLink="/fr/fonctionnalites">
              <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="#EC4899" stroke-width="1.7" aria-hidden="true"><path d="M21 12a9 9 0 1 1-9-9v9z"/><path d="M21 12a9 9 0 0 0-9-9"/></svg>
              <span>Dépenses</span>
            </a>
            <a routerLink="/fr/fonctionnalites">
              <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="1.7" aria-hidden="true"><rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4"/></svg>
              <span>Échéances</span>
            </a>
            <a routerLink="/fr/calypso-auto">
              <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.7" aria-hidden="true"><path d="M9 4a3 3 0 0 0-3 3 3 3 0 0 0-1 5.8A3 3 0 0 0 8 18a3 3 0 0 0 4-1V4.9A3 3 0 0 0 9 4zM15 4a3 3 0 0 1 3 3 3 3 0 0 1 1 5.8A3 3 0 0 1 16 18a3 3 0 0 1-4-1"/></svg>
              <span>IA</span>
            </a>
          </div>
        </div>
      </div>
    </section>

    <section class="band-2 tight">
      <div class="shell">
        <div class="sec-head rise">
          <h2>Tout votre parc. <span class="grad-txt">Un seul endroit.</span></h2>
          <p>Des écrans réels de l'application, sans mise en scène.</p>
        </div>
        <div class="shot rise" style="max-width:1120px;margin:0 auto">
          <img src="/assets/france/produit-tableau-de-bord-sombre.webp" width="1708" height="921"
               alt="Tableau de bord Calypso : véhicules en circulation, carte de la flotte,
                    état du parc, consommation de carburant et assistant IA."
               loading="eager">
        </div>
      </div>
    </section>

    <section class="band-glow">
      <div class="shell">
        <div class="sec-head rise">
          <h2>Ne vous contentez plus de suivre. <span class="grad-txt">Anticipez.</span></h2>
          <p>Calypso lit les données de votre flotte et vous prévient avant la panne.</p>
        </div>
        <div class="cards rise">
          <div class="card">
            <div class="ic">
              <svg width="21" height="21" viewBox="0 0 24 24" fill="none" stroke="#34D399" stroke-width="1.8" aria-hidden="true"><path d="M18 8a6 6 0 1 0-12 0c0 7-3 8-3 8h18s-3-1-3-8M13.7 21a2 2 0 0 1-3.4 0"/></svg>
            </div>
            <div class="k" style="color:#34D399">ALERTES</div>
            <h3>Alertes intelligentes</h3>
            <p>Une dérive de consommation, une échéance qui approche, un véhicule
               qui ne démarre plus : vous êtes prévenu, pas informé après coup.</p>
          </div>
          <div class="card">
            <div class="ic">
              <svg width="21" height="21" viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="1.8" aria-hidden="true"><path d="M4 20V9M10 20V4M16 20v-7M22 20H2"/></svg>
            </div>
            <div class="k" style="color:#60A5FA">TEMPS RÉEL</div>
            <h3>Tableaux de bord en temps réel</h3>
            <p>L'état du parc à l'instant présent, pas la photo d'hier soir.</p>
          </div>
          <div class="card">
            <div class="ic">
              <svg width="21" height="21" viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.8" aria-hidden="true"><path d="M12 2v20M17 6H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/></svg>
            </div>
            <div class="k" style="color:#A78BFA">ÉCONOMIES</div>
            <h3>Réduction des coûts</h3>
            <p>Les postes qui dérapent ressortent seuls, chiffres à l'appui.</p>
          </div>
        </div>
        <div class="final" style="margin-top:56px">
          <a class="btn btn-grad" routerLink="/inscription">Essayer gratuitement</a>
        </div>
      </div>
    </section>
  `
})
export class FranceHomeComponent {}
