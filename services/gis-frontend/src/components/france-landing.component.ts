import { AfterViewInit, Component, ElementRef, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';

/**
 * Vitrine publique destinée au marché européen, France en tête.
 *
 * <p>Servie à la place de l'accueil habituel lorsque le visiteur est
 * détecté en Europe (voir <code>RegionService</code>), et toujours
 * accessible à l'adresse <code>/fr</code> — ce second chemin n'est pas un
 * confort de recette : un robot d'indexation qui explore depuis les
 * États-Unis ne déclenchera JAMAIS la détection, et sans URL stable cette
 * vitrine ne serait jamais référencée.</p>
 *
 * <p>Contenu conforme au cahier des charges France du 20/08/2026 : neuf
 * sections dans l'ordre imposé. Trois éléments des maquettes en sont
 * volontairement absents — « sans carte bancaire » et « sans engagement »
 * tant que la condition commerciale n'est pas validée, les preuves
 * sociales renvoyées en V2, et toute mention légale inventée.</p>
 */
@Component({
  selector: 'app-france-landing',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
      <header class="site">
        <div class="shell bar">
          <a class="brand" href="#top">
            <svg width="24" height="32" viewBox="0 0 48 64" fill="none" xmlns="http://www.w3.org/2000/svg" aria-label="Calypso">
              <defs><linearGradient id="lg1" x1="6" y1="4" x2="40" y2="60" gradientUnits="userSpaceOnUse"><stop offset="0" stop-color="#5fe3bd"/><stop offset=".45" stop-color="#23a6c9"/><stop offset="1" stop-color="#1b3f9e"/></linearGradient></defs>
              <path d="M24 1.5C11.6 1.5 1.7 11.2 1.7 23.4 1.7 39 24 62.5 24 62.5S46.3 39 46.3 23.4C46.3 11.2 36.4 1.5 24 1.5Z" fill="url(#lg1)"/>
              <path d="M11.5 28.5C11 17.8 19.4 9.9 30.2 11.6" stroke="#fff" stroke-width="2.7" stroke-linecap="round" fill="none"/>
              <path d="M17.5 32.2C16.4 24.2 22.6 18.4 30 19.6" stroke="#fff" stroke-width="2.3" stroke-linecap="round" fill="none" opacity=".85"/>
              <circle cx="31.2" cy="14.4" r="3.1" fill="#fff"/>
            </svg>
            <span class="brand-word">Calypso</span>
          </a>
          <nav class="nav">
            <a class="hide-sm" href="#action">Fonctionnalités</a>
            <a class="hide-sm" href="#intelligence">Intelligence Calypso</a>
            <a class="hide-sm" href="#tarifs">Tarifs</a>
            <a class="hide-sm" href="#auto">Calypso Auto</a>
            <a routerLink="/login">Se connecter</a>
            <a class="btn btn-primary btn-sm" routerLink="/inscription">Essayer gratuitement</a>
          </nav>
        </div>
      </header>
      <!-- ===================== 01 · HERO ===================== -->
      <section id="top" class="band-sky hero">
        <div class="shell hero-grid">
          <div>
            <h1>Gérez votre parc.<br><span class="accent">Maîtrisez vos coûts.</span><br>Anticipez vos entretiens.</h1>
            <p class="lede">Calypso centralise entretiens, réparations, carburant, dépenses et échéances pour vous aider à piloter votre parc simplement et à mieux maîtriser vos coûts.</p>
            <p class="incl">Toutes les fonctionnalités incluses.</p>
            <div class="hero-cta">
              <a class="btn btn-primary" routerLink="/inscription">Essayer gratuitement</a>
              <a class="btn btn-ghost" href="#action">Découvrir Calypso</a>
            </div>
            <p class="hero-note"><span>7 jours gratuits</span><span class="dot"></span><span>Toutes les fonctionnalités</span></p>
          </div>
      
          <div class="shot rise">
            <div class="panel">
              <div class="panel-top"><span class="panel-title">Bonjour David</span><span class="panel-date">Août 2026</span></div>
              <div class="kpis">
                <div class="kpi a"><div class="lbl">Entretiens</div><div class="val">8</div></div>
                <div class="kpi b"><div class="lbl">Conso. moy.</div><div class="val">6,8</div></div>
                <div class="kpi c"><div class="lbl">Échéances</div><div class="val">3</div></div>
              </div>
              <div class="chartbox" style="margin-bottom:16px">
                <div class="cap">Dépenses du mois · 2 456 €</div>
                <svg viewBox="0 0 320 88" width="100%" height="88" role="img" aria-label="Évolution des dépenses du mois">
                  <defs><linearGradient id="gf" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#1B4FD8" stop-opacity=".22"/><stop offset="1" stop-color="#1B4FD8" stop-opacity="0"/></linearGradient></defs>
                  <path d="M0 62 L40 48 L80 56 L120 30 L160 42 L200 22 L240 44 L280 26 L320 36 L320 88 L0 88 Z" fill="url(#gf)"/>
                  <path d="M0 62 L40 48 L80 56 L120 30 L160 42 L200 22 L240 44 L280 26 L320 36" fill="none" stroke="#1B4FD8" stroke-width="2.4" stroke-linejoin="round" stroke-linecap="round"/>
                  <circle cx="320" cy="36" r="4" fill="#1B4FD8"/>
                </svg>
              </div>
              <div class="rows">
                <div class="row"><span class="name">Plaquettes de frein</span><span class="bar-track"><span class="bar-fill" style="width:96%;background:var(--blue)"></span></span><span class="pct">96 %</span></div>
                <div class="row"><span class="name">Filtre à air</span><span class="bar-track"><span class="bar-fill" style="width:91%;background:var(--cyan)"></span></span><span class="pct">91 %</span></div>
                <div class="row"><span class="name">Vidange + filtre</span><span class="bar-track"><span class="bar-fill" style="width:49%;background:var(--teal)"></span></span><span class="pct">49 %</span></div>
              </div>
            </div>
            <div class="phone" aria-hidden="true">
              <div class="p-h">Calypso</div>
              <div class="p-k"><div class="l">Véhicules</div><div class="v">46</div></div>
              <div class="p-k"><div class="l">À prévoir</div><div class="v">8</div></div>
              <div class="p-k"><div class="l">Coût mois</div><div class="v">2 456 €</div></div>
            </div>
          </div>
        </div>
      </section>
      
      <!-- ===================== 02 · CALYPSO EN ACTION ===================== -->
      <section id="action" class="band-white">
        <div class="shell">
          <div class="sec-head rise">
            <h2>Tout votre parc. Un seul endroit.</h2>
            <p>Cinq domaines réunis dans une seule plateforme — et un onglet pour voir chacun en détail.</p>
          </div>
      
          <div class="univers rise">
            <div class="uni">
              <div class="ic"><svg width="21" height="21" viewBox="0 0 24 24" fill="none" stroke="#1B4FD8" stroke-width="1.9"><path d="M14.7 6.3a5 5 0 0 0 6 6l-8.4 8.4a2.8 2.8 0 0 1-4-4L16.7 8.3"/><path d="M3 21l4-4"/></svg></div>
              <h3>Entretiens</h3><p>Suivez les entretiens et l'historique</p>
            </div>
            <div class="uni">
              <div class="ic"><svg width="21" height="21" viewBox="0 0 24 24" fill="none" stroke="#23A6C9" stroke-width="1.9"><path d="M5 17h14l-1.5-5.5A2 2 0 0 0 15.6 10H8.4a2 2 0 0 0-1.9 1.5L5 17z"/><circle cx="7.5" cy="17.5" r="1.6"/><circle cx="16.5" cy="17.5" r="1.6"/></svg></div>
              <h3>Réparations</h3><p>Gérez pannes et réparations</p>
            </div>
            <div class="uni">
              <div class="ic"><svg width="21" height="21" viewBox="0 0 24 24" fill="none" stroke="#5FE3BD" stroke-width="1.9"><rect x="4" y="3" width="10" height="18" rx="2"/><path d="M14 8h3a2 2 0 0 1 2 2v6a1.5 1.5 0 0 0 3 0V9l-3-3"/></svg></div>
              <h3>Carburant</h3><p>Analysez consommations et dépenses</p>
            </div>
            <div class="uni">
              <div class="ic"><svg width="21" height="21" viewBox="0 0 24 24" fill="none" stroke="#6B5CE7" stroke-width="1.9"><path d="M12 2v20M17 5.5H9.8a2.8 2.8 0 0 0 0 5.6h4.4a2.8 2.8 0 0 1 0 5.6H6"/></svg></div>
              <h3>Dépenses</h3><p>Centralisez tous vos coûts</p>
            </div>
            <div class="uni">
              <div class="ic"><svg width="21" height="21" viewBox="0 0 24 24" fill="none" stroke="#1B4FD8" stroke-width="1.9"><rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4"/></svg></div>
              <h3>Échéances</h3><p>Ne manquez aucune date</p>
            </div>
          </div>
      
          <div class="tabs" role="tablist" aria-label="Univers fonctionnels">
            <button class="tab" role="tab" [attr.aria-selected]="activeTab === 'p1'" (click)="selectTab('p1')">Entretiens &amp; échéances</button>
            <button class="tab" role="tab" [attr.aria-selected]="activeTab === 'p2'" (click)="selectTab('p2')">Réparations &amp; dépenses</button>
            <button class="tab" role="tab" [attr.aria-selected]="activeTab === 'p3'" (click)="selectTab('p3')">Carburant &amp; consommation</button>
            <button class="tab" role="tab" [attr.aria-selected]="activeTab === 'p4'" (click)="selectTab('p4')">Pilotage du parc</button>
          </div>
      
          <div class="stage">
            <div class="stage-panel" [class.on]="activeTab === 'p1'" role="tabpanel">
              <div class="stage-inner">
                <div class="stage-copy">
                  <h3>Anticipez plutôt que subir</h3>
                  <p>Chaque véhicule vous prévient avant l'échéance, pas après.</p>
                  <ul class="ticks">
                    <li><svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="#1B4FD8" stroke-width="3"><path d="M20 6 9 17l-5-5"/></svg>Entretiens à venir</li>
                    <li><svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="#1B4FD8" stroke-width="3"><path d="M20 6 9 17l-5-5"/></svg>Suivi kilométrique</li>
                    <li><svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="#1B4FD8" stroke-width="3"><path d="M20 6 9 17l-5-5"/></svg>Échéances administratives</li>
                  </ul>
                </div>
                <div class="chartbox">
                  <div class="cap">Progression avant échéance</div>
                  <div class="rows">
                    <div class="row"><span class="name">Plaquettes</span><span class="bar-track"><span class="bar-fill" style="width:96%;background:var(--blue)"></span></span><span class="pct">96 %</span></div>
                    <div class="row"><span class="name">Filtre à air</span><span class="bar-track"><span class="bar-fill" style="width:91%;background:var(--cyan)"></span></span><span class="pct">91 %</span></div>
                    <div class="row"><span class="name">Vidange</span><span class="bar-track"><span class="bar-fill" style="width:49%;background:var(--teal)"></span></span><span class="pct">49 %</span></div>
                    <div class="row"><span class="name">Contrôle tech.</span><span class="bar-track"><span class="bar-fill" style="width:29%;background:var(--violet)"></span></span><span class="pct">29 %</span></div>
                  </div>
                </div>
              </div>
            </div>
            <div class="stage-panel" [class.on]="activeTab === 'p2'" role="tabpanel">
              <div class="stage-inner">
                <div class="stage-copy">
                  <h3>Suivez chaque intervention et son coût</h3>
                  <p>Ce qui a été fait, par qui, pour quel montant — sans chercher.</p>
                  <ul class="ticks">
                    <li><svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="#1B4FD8" stroke-width="3"><path d="M20 6 9 17l-5-5"/></svg>Historique des réparations</li>
                    <li><svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="#1B4FD8" stroke-width="3"><path d="M20 6 9 17l-5-5"/></svg>Coûts pièces et main-d'œuvre</li>
                    <li><svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="#1B4FD8" stroke-width="3"><path d="M20 6 9 17l-5-5"/></svg>Vision financière par véhicule</li>
                  </ul>
                </div>
                <div class="chartbox">
                  <div class="cap">Dépenses par catégorie · 30 derniers jours</div>
                  <div class="rows">
                    <div class="row"><span class="name">Carburant</span><span class="bar-track"><span class="bar-fill" style="width:88%;background:var(--violet)"></span></span><span class="pct">9 820</span></div>
                    <div class="row"><span class="name">Entretien</span><span class="bar-track"><span class="bar-fill" style="width:83%;background:var(--teal)"></span></span><span class="pct">9 294</span></div>
                    <div class="row"><span class="name">Réparation</span><span class="bar-track"><span class="bar-fill" style="width:53%;background:var(--cyan)"></span></span><span class="pct">5 899</span></div>
                    <div class="row"><span class="name">Autres</span><span class="bar-track"><span class="bar-fill" style="width:8%;background:var(--ink-faint)"></span></span><span class="pct">832</span></div>
                  </div>
                </div>
              </div>
            </div>
            <div class="stage-panel" [class.on]="activeTab === 'p3'" role="tabpanel">
              <div class="stage-inner">
                <div class="stage-copy">
                  <h3>Comprenez où part votre carburant</h3>
                  <p>La consommation réelle, véhicule par véhicule, mois après mois.</p>
                  <ul class="ticks">
                    <li><svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="#1B4FD8" stroke-width="3"><path d="M20 6 9 17l-5-5"/></svg>Consommation moyenne</li>
                    <li><svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="#1B4FD8" stroke-width="3"><path d="M20 6 9 17l-5-5"/></svg>Suivi des pleins</li>
                    <li><svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="#1B4FD8" stroke-width="3"><path d="M20 6 9 17l-5-5"/></svg>Anomalies et variations</li>
                  </ul>
                </div>
                <div class="chartbox">
                  <div class="cap">Consommation carburant · L/100 km</div>
                  <svg viewBox="0 0 320 120" width="100%" height="120" role="img" aria-label="Courbe de consommation">
                    <defs><linearGradient id="ff" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#23A6C9" stop-opacity=".26"/><stop offset="1" stop-color="#23A6C9" stop-opacity="0"/></linearGradient></defs>
                    <line x1="0" y1="30" x2="320" y2="30" stroke="#DCE6F2"/><line x1="0" y1="62" x2="320" y2="62" stroke="#DCE6F2"/><line x1="0" y1="94" x2="320" y2="94" stroke="#DCE6F2"/>
                    <path d="M0 82 L40 62 L80 74 L120 42 L160 56 L200 32 L240 60 L280 38 L320 50 L320 120 L0 120 Z" fill="url(#ff)"/>
                    <path d="M0 82 L40 62 L80 74 L120 42 L160 56 L200 32 L240 60 L280 38 L320 50" fill="none" stroke="#23A6C9" stroke-width="2.4" stroke-linejoin="round" stroke-linecap="round"/>
                    <circle cx="320" cy="50" r="4.5" fill="#23A6C9"/>
                  </svg>
                </div>
              </div>
            </div>
            <div class="stage-panel" [class.on]="activeTab === 'p4'" role="tabpanel">
              <div class="stage-inner">
                <div class="stage-copy">
                  <h3>Gardez l'essentiel sous les yeux</h3>
                  <p>L'état du parc, les coûts et les échéances, sur un seul écran.</p>
                  <ul class="ticks">
                    <li><svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="#1B4FD8" stroke-width="3"><path d="M20 6 9 17l-5-5"/></svg>État du parc en temps réel</li>
                    <li><svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="#1B4FD8" stroke-width="3"><path d="M20 6 9 17l-5-5"/></svg>Dépenses consolidées</li>
                    <li><svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="#1B4FD8" stroke-width="3"><path d="M20 6 9 17l-5-5"/></svg>Entretiens et échéances à venir</li>
                  </ul>
                </div>
                <div class="chartbox">
                  <div class="cap">Tableau de bord</div>
                  <div class="kpis" style="margin-bottom:14px">
                    <div class="kpi a"><div class="lbl">Véhicules</div><div class="val">46</div></div>
                    <div class="kpi b"><div class="lbl">En circulation</div><div class="val">31</div></div>
                    <div class="kpi c"><div class="lbl">Coût total</div><div class="val">26 k</div></div>
                  </div>
                  <div class="rows">
                    <div class="row"><span class="name">En circulation</span><span class="bar-track"><span class="bar-fill" style="width:67%;background:var(--teal)"></span></span><span class="pct">67 %</span></div>
                    <div class="row"><span class="name">À l'arrêt</span><span class="bar-track"><span class="bar-fill" style="width:28%;background:var(--cyan)"></span></span><span class="pct">28 %</span></div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>
      
      <!-- ===================== 03 · INTELLIGENCE (bandeau bleu nuit) ===================== -->
      <section id="intelligence" class="band-navy">
        <div class="shell">
          <div class="sec-head rise">
            <p class="eyebrow" style="color:var(--cyan)">Intelligence Calypso</p>
            <h2>Ne vous contentez plus de suivre. Anticipez.</h2>
            <p>Calypso exploite les informations disponibles sur vos véhicules pour vous aider à comprendre leur situation, repérer les points d'attention et anticiper les actions à prévoir.</p>
          </div>
      
          <div class="icards rise">
            <div class="icard">
              <div class="ic"><svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="#5FE3BD" stroke-width="1.9"><path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/><path d="M13.7 21a2 2 0 0 1-3.4 0"/></svg></div>
              <div class="k">ANTICIPER</div>
              <h3>Alertes intelligentes</h3>
              <p>Les entretiens et points d'attention à venir, avant qu'ils ne deviennent urgents.</p>
            </div>
            <div class="icard">
              <div class="ic"><svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="#23A6C9" stroke-width="1.9"><path d="M3 3v18h18"/><path d="M7 15l4-5 3 3 4-6"/></svg></div>
              <div class="k">DÉTECTER</div>
              <h3>Anomalies repérées</h3>
              <p>Les écarts et problèmes potentiels que révèlent les données de vos véhicules.</p>
            </div>
            <div class="icard">
              <div class="ic"><svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="#6B5CE7" stroke-width="1.9"><circle cx="12" cy="12" r="9"/><path d="M12 16v-4M12 8h.01"/></svg></div>
              <div class="k">COMPRENDRE</div>
              <h3>Analyse contextualisée</h3>
              <p>Une lecture remise dans son contexte, véhicule par véhicule.</p>
            </div>
          </div>
      
          <div class="demo rise">
            <div class="demo-head">
              <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="#5FE3BD" stroke-width="2"><path d="M12 3v3M12 18v3M3 12h3M18 12h3M5.6 5.6l2.1 2.1M16.3 16.3l2.1 2.1M18.4 5.6l-2.1 2.1M7.7 16.3l-2.1 2.1"/></svg>
              Assistant Calypso
            </div>
            <div class="demo-body">
              <div class="bubble q">Le voyant moteur est allumé, que peut-il se passer ?</div>
              <div class="bubble a">
                Plusieurs causes peuvent déclencher ce voyant :
                <ul>
                  <li>Problème d'allumage (bougies, bobines)</li>
                  <li>Capteur défectueux (sonde lambda, débitmètre)</li>
                  <li>Système antipollution (EGR, FAP)</li>
                  <li>Carburant de mauvaise qualité</li>
                </ul>
                <p style="margin:10px 0 0">Je recommande un diagnostic électronique pour identifier précisément la cause.</p>
              </div>
            </div>
            <div class="demo-foot">Démonstration — l'assistant professionnel s'utilise dans l'application.</div>
          </div>
        </div>
      </section>
      
      <!-- ===================== 04 · TARIFS ===================== -->
      <section id="tarifs" class="band-pale">
        <div class="shell">
          <div class="sec-head rise">
            <h2>Simple. Transparent. Sans surprise.</h2>
            <p>Les deux formules donnent accès au même produit. Seule la durée change.</p>
          </div>
          <div class="included rise">
            <h3>Toutes les fonctionnalités incluses</h3>
            <div class="feat-grid">
              <span class="feat"><span class="d"></span>Entretiens &amp; échéances</span>
              <span class="feat"><span class="d"></span>Réparations</span>
              <span class="feat"><span class="d"></span>Carburant &amp; consommation</span>
              <span class="feat"><span class="d"></span>Dépenses &amp; budget</span>
              <span class="feat"><span class="d"></span>Tableau de bord &amp; pilotage</span>
              <span class="feat"><span class="d"></span>Intelligence Calypso</span>
            </div>
          </div>
          <div class="plans rise">
            <div class="plan reco">
              <span class="badge">RECOMMANDÉ</span>
              <span class="term">Annuel</span>
              <div class="price"><span class="n">3 €</span><span class="u">/ véhicule / mois</span></div>
              <p class="sub">36 € par véhicule et par an</p>
              <p class="save">Économisez 25 %</p>
              <p class="same">Toutes les fonctionnalités · Mises à jour incluses · 7 jours gratuits</p>
              <a class="btn btn-primary" routerLink="/inscription">Essayer gratuitement</a>
            </div>
            <div class="plan">
              <span class="term">Semestriel</span>
              <div class="price"><span class="n">4 €</span><span class="u">/ véhicule / mois</span></div>
              <p class="sub">24 € par véhicule et par semestre</p>
              <p class="flex">Plus de flexibilité</p>
              <p class="same">Toutes les fonctionnalités · Mises à jour incluses · 7 jours gratuits</p>
              <a class="btn btn-ghost" routerLink="/inscription">Essayer gratuitement</a>
            </div>
          </div>
        </div>
      </section>
      
      <!-- ===================== 05 · CALYPSO AUTO ===================== -->
      <section id="auto" class="band-white">
        <div class="shell">
          <div class="auto-card rise">
            <div>
              <p class="eyebrow">Calypso Auto · Outil gratuit</p>
              <h3>Une question sur votre voiture ? Demandez à Calypso.</h3>
              <p>Entretien, voyant, panne ou consommation : indiquez votre véhicule, posez votre question, obtenez une réponse claire.</p>
              <div class="themes">
                <span class="theme"><span class="d" style="background:var(--blue)"></span>Entretien</span>
                <span class="theme"><span class="d" style="background:var(--cyan)"></span>Voyants</span>
                <span class="theme"><span class="d" style="background:var(--violet)"></span>Pannes</span>
                <span class="theme"><span class="d" style="background:var(--teal)"></span>Consommation</span>
              </div>
              <div style="margin-top:22px"><a class="btn btn-primary" href="#auto">Essayer Calypso Auto</a></div>
            </div>
            <div>
              <p style="font-family:Manrope,sans-serif;font-weight:700;font-size:14px;margin:0 0 12px">Questions fréquentes des automobilistes</p>
              <div class="chips">
                <span class="chip-q">Voyant moteur allumé, que faire ?</span>
                <span class="chip-q">Quand changer la courroie de distribution ?</span>
                <span class="chip-q">Pourquoi ma voiture consomme-t-elle plus ?</span>
                <span class="chip-q">Panne de démarrage à froid</span>
                <span class="chip-q">Pression des pneus recommandée</span>
              </div>
            </div>
          </div>
      
          <div class="steps rise">
            <div class="step"><div class="n">01</div><h4>Posez votre question</h4><p>Décrivez votre problème ou ce que vous voulez savoir.</p></div>
            <div class="step"><div class="n">02</div><h4>Calypso analyse</h4><p>Votre véhicule et votre question sont pris en compte.</p></div>
            <div class="step"><div class="n">03</div><h4>Obtenez votre réponse</h4><p>Une réponse claire, prudente et argumentée.</p></div>
          </div>
      
          <p class="bridge">Vous gérez plusieurs véhicules dans votre entreprise ? <a href="#tarifs">Découvrez Calypso pour les professionnels.</a></p>
        </div>
      </section>
      
      <!-- ===================== 06 · CONFIANCE ===================== -->
      <section class="band-pale tight">
        <div class="shell">
          <div class="sec-head rise">
            <h2>Vos données restent les vôtres</h2>
            <p>Un cadre clair, des accès protégés, une gestion pensée pour les exigences applicables.</p>
          </div>
          <div class="trust rise">
            <div class="trust-item">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="#1B4FD8" stroke-width="1.8"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/></svg>
              <h3>Confidentialité</h3>
              <p>Les informations de votre parc sont traitées dans un cadre défini et pour les finalités prévues.</p>
            </div>
            <div class="trust-item">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="#23A6C9" stroke-width="1.8"><rect x="3" y="11" width="18" height="11" rx="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/></svg>
              <h3>Sécurité</h3>
              <p>Des mesures destinées à protéger l'accès à votre espace et aux données de votre parc.</p>
            </div>
            <div class="trust-item">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="#6B5CE7" stroke-width="1.8"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6"/></svg>
              <h3>Protection des données</h3>
              <p>Une gestion conçue pour répondre aux exigences applicables en matière de protection des données.</p>
            </div>
          </div>
        </div>
      </section>
      
      <!-- ===================== 07 · FAQ ===================== -->
      <section class="band-white tight">
        <div class="shell">
          <div class="sec-head rise"><h2>Questions fréquentes</h2></div>
          <div class="faq rise">
            <details open><summary>Puis-je essayer Calypso gratuitement ?</summary><p class="ans">Oui, pendant 7 jours, avec toutes les fonctionnalités.</p></details>
            <details><summary>Quelles fonctionnalités sont incluses ?</summary><p class="ans">Entretiens, échéances, réparations, carburant, dépenses, pilotage et Intelligence Calypso, entre autres.</p></details>
            <details><summary>Quelle différence entre l'annuel et le semestriel ?</summary><p class="ans">Le périmètre fonctionnel est identique. Seules la durée et le prix changent.</p></details>
            <details><summary>Que se passe-t-il après les 7 jours ?</summary><p class="ans"><span class="todo">Réponse à rédiger une fois le fonctionnement commercial arrêté — à valider par BELIVE</span></p></details>
            <details><summary>Ai-je besoin d'installer un boîtier GPS ?</summary><p class="ans">Non. La gestion des entretiens, réparations, dépenses, carburant et échéances fonctionne sans boîtier.</p></details>
            <details><summary>Comment Calypso protège-t-il mes données ?</summary><p class="ans">Vos informations sont traitées dans un cadre défini et pour les finalités prévues. <a routerLink="/politique-de-confidentialite" style="color:var(--blue);font-weight:600">Consulter la politique de confidentialité</a>.</p></details>
          </div>
        </div>
      </section>
      
      <!-- ===================== 08 · CTA FINAL ===================== -->
      <section class="band-navy tight final">
        <div class="shell">
          <h2>Prêt à simplifier la gestion de votre parc ?</h2>
          <div class="hero-cta" style="justify-content:center">
            <a class="btn btn-light" routerLink="/inscription">Essayer gratuitement</a>
          </div>
          <p class="hero-note" style="justify-content:center;margin-top:18px;color:rgba(255,255,255,.6)">
            <span>7 jours gratuits</span><span class="dot"></span><span>Toutes les fonctionnalités</span>
          </p>
        </div>
      </section>
      
      <footer class="site">
        <div class="shell">
          <div class="foot-grid">
            <div class="foot-brand">
              <a class="brand" href="#top">
                <svg width="20" height="27" viewBox="0 0 48 64" fill="none" xmlns="http://www.w3.org/2000/svg" aria-label="Calypso">
                  <defs><linearGradient id="lg2" x1="6" y1="4" x2="40" y2="60" gradientUnits="userSpaceOnUse"><stop offset="0" stop-color="#5fe3bd"/><stop offset=".45" stop-color="#23a6c9"/><stop offset="1" stop-color="#1b3f9e"/></linearGradient></defs>
                  <path d="M24 1.5C11.6 1.5 1.7 11.2 1.7 23.4 1.7 39 24 62.5 24 62.5S46.3 39 46.3 23.4C46.3 11.2 36.4 1.5 24 1.5Z" fill="url(#lg2)"/>
                  <path d="M11.5 28.5C11 17.8 19.4 9.9 30.2 11.6" stroke="#fff" stroke-width="2.7" stroke-linecap="round" fill="none"/>
                  <circle cx="31.2" cy="14.4" r="3.1" fill="#fff"/>
                </svg>
                <span class="brand-word" style="font-size:17px">Calypso</span>
              </a>
              <p>La gestion de parc automobile, simplement.</p>
            </div>
            <div><h4>Produit</h4><ul>
              <li><a href="#action">Fonctionnalités</a></li>
              <li><a href="#intelligence">Intelligence Calypso</a></li>
              <li><a href="#tarifs">Tarifs</a></li>
              <li><a href="#auto">Calypso Auto</a></li>
            </ul></div>
            <div><h4>Accès</h4><ul>
              <li><a routerLink="/inscription">Essayer gratuitement</a></li>
              <li><a routerLink="/login">Se connecter</a></li>
            </ul></div>
            <div><h4>Entreprise</h4><ul>
              <li><a href="#">BELIVE</a></li>
              <li><a href="#">Contact</a></li>
              <li><a href="#">Support</a></li>
            </ul></div>
            <div><h4>Légal</h4><ul>
              <li><a href="#">Mentions légales</a></li>
              <li><a routerLink="/politique-de-confidentialite">Politique de confidentialité</a></li>
              <li><a href="#">CGU</a></li>
              <li><a href="#">Gestion des cookies</a></li>
            </ul></div>
          </div>
          <div class="foot-bottom">© 2026 BELIVE — Calypso. Tous droits réservés.</div>
        </div>
      </footer>
  `,
  styles: [`
    /* ------------------------------------------------------------------
       Identité Calypso, d'après la planche de maquettes :
       accueil CLAIR (blanc → bleu très pâle), bandeau BLEU NUIT réservé
       aux sections d'accroche, bouton bleu franc pour le CTA unique.
       Les accents cyan et turquoise viennent du dégradé du logo officiel.
       Le site assume une identité de marque unique : toutes les couleurs
       sont peintes explicitement et ne suivent pas le thème du lecteur.
       ------------------------------------------------------------------ */
    :root {
      --white:      #FFFFFF;
      --sky:        #F2F7FD;
      --sky-2:      #E6EFFA;
      --navy:       #0C1A33;
      --navy-2:     #13263F;
      --navy-3:     #1D3556;
      --blue:       #1B4FD8;
      --blue-dark:  #163FAD;
      --blue-soft:  #E8EFFE;
      --cyan:       #23A6C9;
      --teal:       #5FE3BD;
      --violet:     #6B5CE7;
      --ink:        #0E1A2B;
      --ink-soft:   #52637A;
      --ink-faint:  #8A99AC;
      --rule:       #DCE6F2;
      --rule-dark:  #24405F;
      --sh-sm:      0 1px 2px rgba(12,26,51,.05), 0 4px 14px rgba(12,26,51,.05);
      --sh-md:      0 2px 8px rgba(12,26,51,.07), 0 20px 48px rgba(12,26,51,.11);
      --r:          14px;
      --r-sm:       10px;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0; background: var(--white); color: var(--ink);
      font-family: "Source Sans 3", system-ui, -apple-system, "Segoe UI", sans-serif;
      font-size: 17px; line-height: 1.65; -webkit-font-smoothing: antialiased;
    }
    h1, h2, h3, h4, .brand-word, .btn, .eyebrow, .nav a, .tab { font-family: Manrope, system-ui, sans-serif; }
    .shell { max-width: 1140px; margin: 0 auto; padding: 0 24px; }
    .eyebrow { font-size: 12px; font-weight: 700; letter-spacing: .15em; text-transform: uppercase; margin: 0 0 14px; }
  
    /* boutons */
    .btn {
      display: inline-flex; align-items: center; justify-content: center;
      font-size: 15px; font-weight: 700; padding: 14px 26px; border-radius: var(--r-sm);
      text-decoration: none; border: 1.5px solid transparent; cursor: pointer;
      transition: transform .18s ease, background .18s ease, border-color .18s ease, color .18s ease;
    }
    .btn-primary { background: var(--blue); color: var(--white); }
    .btn-primary:hover { background: var(--blue-dark); transform: translateY(-1px); }
    .btn-ghost { background: var(--white); color: var(--ink); border-color: var(--rule); }
    .btn-ghost:hover { border-color: var(--blue); color: var(--blue); transform: translateY(-1px); }
    .btn-light { background: var(--white); color: var(--blue); }
    .btn-light:hover { transform: translateY(-1px); }
    .btn-sm { padding: 10px 18px; font-size: 14px; }
    a:focus-visible, button:focus-visible, summary:focus-visible { outline: 3px solid var(--cyan); outline-offset: 3px; border-radius: 6px; }
  
    /* header clair, comme la maquette */
    header.site {
      position: sticky; top: 0; z-index: 50; background: rgba(255,255,255,.94);
      backdrop-filter: blur(10px); border-bottom: 1px solid var(--rule);
    }
    .bar { display: flex; align-items: center; gap: 30px; height: 70px; }
    .brand { display: flex; align-items: center; gap: 10px; text-decoration: none; }
    .brand-word { color: var(--ink); font-size: 20px; font-weight: 800; letter-spacing: -.02em; }
    .nav { display: flex; gap: 24px; margin-left: auto; align-items: center; }
    .nav a { color: var(--ink-soft); text-decoration: none; font-size: 14.5px; font-weight: 600; }
    .nav a:hover { color: var(--blue); }
    @media (max-width: 980px) { .nav .hide-sm { display: none; } .bar { gap: 14px; } }
  
    section { padding: 112px 0; }
    section.tight { padding: 90px 0; }
    .band-sky { background: linear-gradient(180deg, var(--sky) 0%, var(--white) 100%); }
    .band-white { background: var(--white); }
    .band-pale { background: var(--sky); }
    .band-navy { background: var(--navy); color: #fff; }
  
    .sec-head { max-width: 680px; margin: 0 auto 60px; text-align: center; }
    .sec-head h2 { font-size: clamp(29px, 4.1vw, 42px); font-weight: 800; letter-spacing: -.025em; line-height: 1.14; margin: 0 0 14px; text-wrap: balance; }
    .sec-head p { font-size: 18px; color: var(--ink-soft); margin: 0; }
    .band-navy .sec-head p { color: rgba(255,255,255,.74); }
  
    /* ---------- 01 · HERO (clair) ---------- */
    .hero { padding: 88px 0 96px; }
    .hero-grid { display: grid; grid-template-columns: 1fr 1.06fr; gap: 56px; align-items: center; }
    .hero h1 { font-size: clamp(36px, 4.9vw, 52px); font-weight: 800; letter-spacing: -.03em; line-height: 1.1; margin: 0 0 20px; text-wrap: balance; }
    .hero h1 .accent { color: var(--blue); }
    .hero p.lede { font-size: 18px; color: var(--ink-soft); margin: 0 0 12px; max-width: 50ch; }
    .hero p.incl { font-size: 16px; font-weight: 600; color: var(--ink); margin: 0 0 26px; }
    .hero-cta { display: flex; gap: 13px; flex-wrap: wrap; margin-bottom: 16px; }
    .hero-note { font-size: 14.5px; color: var(--ink-faint); display: flex; align-items: center; gap: 10px; flex-wrap: wrap; margin: 0; }
    .hero-note .dot { width: 4px; height: 4px; border-radius: 50%; background: var(--cyan); }
  
    .shot { position: relative; }
    .panel { background: var(--white); border: 1px solid var(--rule); border-radius: var(--r); box-shadow: var(--sh-md); padding: 22px; }
    .panel-top { display: flex; align-items: center; justify-content: space-between; margin-bottom: 16px; }
    .panel-title { font-family: Manrope, sans-serif; font-weight: 700; font-size: 15px; }
    .panel-date { font-size: 12.5px; color: var(--ink-faint); }
    .kpis { display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px; margin-bottom: 16px; }
    .kpi { background: var(--sky); border-radius: var(--r-sm); padding: 12px 13px; }
    .kpi .lbl { font-size: 10.5px; letter-spacing: .09em; text-transform: uppercase; color: var(--ink-faint); font-weight: 600; }
    .kpi .val { font-family: "IBM Plex Mono", monospace; font-size: 21px; font-weight: 500; margin-top: 2px; font-variant-numeric: tabular-nums; }
    .kpi.a .val { color: var(--blue); } .kpi.b .val { color: var(--cyan); } .kpi.c .val { color: var(--violet); }
    .rows { display: flex; flex-direction: column; gap: 9px; }
    .row { display: flex; align-items: center; gap: 12px; font-size: 13.5px; }
    .row .name { width: 126px; color: var(--ink-soft); flex-shrink: 0; }
    .bar-track { flex: 1; height: 7px; background: var(--sky-2); border-radius: 99px; overflow: hidden; }
    .bar-fill { height: 100%; border-radius: 99px; }
    .row .pct { font-family: "IBM Plex Mono", monospace; font-size: 12px; color: var(--ink-faint); width: 42px; text-align: right; font-variant-numeric: tabular-nums; }
    /* mobile posé sur le tableau de bord, comme la maquette */
    .phone {
      position: absolute; right: -14px; bottom: -30px; width: 132px;
      background: var(--navy); border-radius: 18px; padding: 9px 8px;
      box-shadow: var(--sh-md); border: 3px solid var(--navy-2);
    }
    .phone .p-h { color: #fff; font-family: Manrope, sans-serif; font-size: 10px; font-weight: 700; padding: 3px 5px 7px; }
    .phone .p-k { background: rgba(255,255,255,.09); border-radius: 7px; padding: 7px 8px; margin-bottom: 5px; }
    .phone .p-k .l { font-size: 7.5px; letter-spacing: .06em; text-transform: uppercase; color: rgba(255,255,255,.5); font-weight: 600; }
    .phone .p-k .v { font-family: "IBM Plex Mono", monospace; font-size: 13px; color: #fff; }
    @media (max-width: 940px) { .hero-grid { grid-template-columns: 1fr; gap: 44px; } .phone { display: none; } section { padding: 80px 0; } }
  
    /* ---------- rangée des univers ---------- */
    .univers { display: grid; grid-template-columns: repeat(5, 1fr); gap: 14px; max-width: 980px; margin: 0 auto; }
    .uni { text-align: center; padding: 22px 12px; border-radius: var(--r); transition: background .18s ease; }
    .uni:hover { background: var(--sky); }
    .uni .ic { width: 44px; height: 44px; border-radius: 12px; background: var(--blue-soft); display: flex; align-items: center; justify-content: center; margin: 0 auto 12px; }
    .uni h3 { font-size: 15.5px; font-weight: 700; margin: 0 0 5px; }
    .uni p { font-size: 13.5px; color: var(--ink-soft); margin: 0; line-height: 1.5; }
    @media (max-width: 860px) { .univers { grid-template-columns: repeat(2, 1fr); } }
  
    /* ---------- onglets ---------- */
    .tabs { display: flex; gap: 8px; justify-content: center; flex-wrap: wrap; margin: 48px 0 34px; }
    .tab { background: var(--white); border: 1.5px solid var(--rule); color: var(--ink-soft); font-size: 14.5px; font-weight: 600; padding: 11px 20px; border-radius: 99px; cursor: pointer; transition: all .18s ease; }
    .tab:hover { border-color: var(--blue); color: var(--blue); }
    .tab[aria-selected="true"] { background: var(--navy); border-color: var(--navy); color: #fff; }
    .stage { max-width: 940px; margin: 0 auto; }
    .stage-panel { display: none; }
    .stage-panel.on { display: block; animation: rise .34s ease both; }
    .stage-inner { background: var(--white); border: 1px solid var(--rule); border-radius: var(--r); box-shadow: var(--sh-md); padding: 30px; display: grid; grid-template-columns: 1fr 1.2fr; gap: 32px; align-items: center; }
    .stage-copy h3 { font-size: 22px; font-weight: 800; letter-spacing: -.02em; margin: 0 0 10px; }
    .stage-copy p { color: var(--ink-soft); margin: 0 0 15px; font-size: 16px; }
    .ticks { list-style: none; padding: 0; margin: 0; display: flex; flex-direction: column; gap: 8px; }
    .ticks li { display: flex; gap: 10px; align-items: flex-start; font-size: 15px; color: var(--ink-soft); }
    .ticks svg { flex-shrink: 0; margin-top: 4px; }
    .chartbox { background: var(--sky); border-radius: var(--r-sm); padding: 18px; }
    .chartbox .cap { font-size: 11px; letter-spacing: .09em; text-transform: uppercase; color: var(--ink-faint); font-weight: 600; margin-bottom: 12px; }
    @media (max-width: 820px) { .stage-inner { grid-template-columns: 1fr; } }
  
    /* ---------- 03 · intelligence (bandeau bleu nuit) ---------- */
    .icards { display: grid; grid-template-columns: repeat(3, 1fr); gap: 18px; margin-bottom: 44px; }
    .icard { background: var(--navy-2); border: 1px solid var(--rule-dark); border-radius: var(--r); padding: 26px 24px; }
    .icard .ic { width: 40px; height: 40px; border-radius: 11px; background: rgba(255,255,255,.07); display: flex; align-items: center; justify-content: center; margin-bottom: 14px; }
    .icard h3 { font-size: 16.5px; font-weight: 700; margin: 0 0 8px; color: #fff; }
    .icard p { margin: 0; color: rgba(255,255,255,.72); font-size: 15px; }
    .icard .k { font-family: Manrope, sans-serif; font-size: 11px; font-weight: 700; letter-spacing: .14em; margin-bottom: 8px; }
    .icard:nth-child(1) .k { color: var(--teal); }
    .icard:nth-child(2) .k { color: var(--cyan); }
    .icard:nth-child(3) .k { color: var(--violet); }
    @media (max-width: 800px) { .icards { grid-template-columns: 1fr; } }
  
    .demo { max-width: 620px; margin: 0 auto; background: var(--navy-2); border: 1px solid var(--rule-dark); border-radius: var(--r); overflow: hidden; }
    .demo-head { display: flex; align-items: center; gap: 10px; padding: 14px 20px; border-bottom: 1px solid var(--rule-dark); font-family: Manrope, sans-serif; font-weight: 700; font-size: 14.5px; }
    .demo-body { padding: 20px; display: flex; flex-direction: column; gap: 13px; }
    .bubble { border-radius: 12px; padding: 13px 16px; font-size: 15px; max-width: 84%; }
    .bubble.q { background: var(--navy-3); color: #fff; align-self: flex-end; border-bottom-right-radius: 4px; }
    .bubble.a { background: rgba(255,255,255,.05); border: 1px solid var(--rule-dark); color: rgba(255,255,255,.86); border-bottom-left-radius: 4px; }
    .bubble.a ul { margin: 9px 0 0; padding-left: 18px; }
    .demo-foot { padding: 12px 20px; border-top: 1px solid var(--rule-dark); font-size: 13px; color: rgba(255,255,255,.5); text-align: center; }
  
    /* ---------- 04 · tarifs ---------- */
    .included { background: var(--white); border: 1px solid var(--rule); border-radius: var(--r); padding: 24px 28px; margin: 0 auto 28px; max-width: 940px; box-shadow: var(--sh-sm); }
    .included h3 { margin: 0 0 15px; font-size: 15.5px; font-weight: 800; text-align: center; }
    .feat-grid { display: flex; flex-wrap: wrap; gap: 9px; justify-content: center; }
    .feat { display: inline-flex; align-items: center; gap: 8px; background: var(--sky); border-radius: 99px; padding: 8px 15px; font-size: 14.5px; color: var(--ink-soft); }
    .feat .d { width: 5px; height: 5px; border-radius: 50%; background: var(--cyan); }
    .plans { display: grid; grid-template-columns: repeat(2, 1fr); gap: 22px; max-width: 940px; margin: 0 auto; }
    .plan { background: var(--white); border: 1.5px solid var(--rule); border-radius: var(--r); padding: 32px 30px; position: relative; box-shadow: var(--sh-sm); display: flex; flex-direction: column; }
    .plan.reco { border-color: var(--blue); box-shadow: var(--sh-md); }
    .plan .badge { position: absolute; top: -13px; left: 30px; background: var(--blue); color: #fff; font-family: Manrope, sans-serif; font-size: 11px; font-weight: 700; letter-spacing: .12em; padding: 6px 13px; border-radius: 99px; }
    .plan .term { font-family: Manrope, sans-serif; font-size: 12.5px; font-weight: 700; letter-spacing: .13em; text-transform: uppercase; color: var(--ink-faint); }
    .price { display: flex; align-items: baseline; gap: 7px; margin: 13px 0 4px; }
    .price .n { font-family: Manrope, sans-serif; font-size: 44px; font-weight: 800; letter-spacing: -.03em; }
    .price .u { color: var(--ink-soft); font-size: 15.5px; }
    .plan .sub { color: var(--ink-soft); font-size: 15px; margin: 0 0 6px; }
    .plan .save { font-size: 14px; font-weight: 600; color: var(--cyan); margin: 0 0 20px; }
    .plan .flex { font-size: 14px; font-weight: 600; color: var(--ink-faint); margin: 0 0 20px; }
    .plan .same { font-size: 14.5px; color: var(--ink-soft); margin: 0 0 22px; padding-top: 18px; border-top: 1px solid var(--rule); }
    .plan .btn { margin-top: auto; }
    @media (max-width: 780px) { .plans { grid-template-columns: 1fr; } }
  
    /* ---------- 05 · calypso auto ---------- */
    .auto-card { max-width: 960px; margin: 0 auto; background: var(--white); border: 1px solid var(--rule); border-radius: var(--r); box-shadow: var(--sh-md); padding: 36px; display: grid; grid-template-columns: 1.05fr .95fr; gap: 40px; align-items: center; }
    .auto-card .eyebrow { color: var(--violet); }
    .auto-card h3 { font-size: 25px; font-weight: 800; letter-spacing: -.025em; margin: 0 0 12px; text-wrap: balance; }
    .auto-card > div > p { color: var(--ink-soft); margin: 0 0 20px; }
    .themes { display: grid; grid-template-columns: repeat(2, 1fr); gap: 10px; }
    .theme { background: var(--sky); border-radius: var(--r-sm); padding: 14px 16px; font-weight: 600; font-size: 15px; display: flex; align-items: center; gap: 10px; }
    .theme .d { width: 6px; height: 6px; border-radius: 50%; }
    .chips { display: flex; flex-wrap: wrap; gap: 8px; }
    .chip-q { background: var(--sky); border: 1px solid var(--rule); border-radius: 99px; padding: 8px 14px; font-size: 13.5px; color: var(--ink-soft); }
    .steps { display: grid; grid-template-columns: repeat(3, 1fr); gap: 16px; max-width: 800px; margin: 40px auto 0; }
    .step { text-align: center; }
    .step .n { font-family: Manrope, sans-serif; font-size: 12px; font-weight: 700; color: var(--blue); letter-spacing: .1em; }
    .step h4 { font-size: 15.5px; font-weight: 700; margin: 6px 0 5px; }
    .step p { font-size: 14px; color: var(--ink-soft); margin: 0; }
    @media (max-width: 820px) { .auto-card { grid-template-columns: 1fr; padding: 28px; } .steps { grid-template-columns: 1fr; } }
    .bridge { text-align: center; margin-top: 28px; font-size: 15.5px; color: var(--ink-soft); }
    .bridge a { color: var(--blue); font-weight: 600; }
  
    /* ---------- 06 · confiance ---------- */
    .trust { display: grid; grid-template-columns: repeat(3, 1fr); gap: 20px; max-width: 940px; margin: 0 auto; }
    .trust-item { background: var(--white); border: 1px solid var(--rule); border-radius: var(--r); padding: 26px 24px; }
    .trust-item h3 { font-size: 17px; font-weight: 800; margin: 13px 0 8px; }
    .trust-item p { margin: 0; color: var(--ink-soft); font-size: 15.5px; }
    @media (max-width: 780px) { .trust { grid-template-columns: 1fr; } }
  
    /* ---------- 07 · faq ---------- */
    .faq { max-width: 780px; margin: 0 auto; }
    details { border-bottom: 1px solid var(--rule); }
    details summary { cursor: pointer; list-style: none; padding: 20px 40px 20px 0; position: relative; font-family: Manrope, sans-serif; font-weight: 700; font-size: 16.5px; }
    details summary::-webkit-details-marker { display: none; }
    details summary::after { content: "+"; position: absolute; right: 6px; top: 50%; transform: translateY(-50%); font-size: 22px; color: var(--blue); }
    details[open] summary::after { content: "−"; }
    details .ans { padding: 0 40px 20px 0; color: var(--ink-soft); margin: 0; }
    .todo { display: inline-block; margin-top: 6px; font-size: 13px; font-weight: 600; background: #FFF4E0; color: #92600C; border: 1px solid #F0D9A8; padding: 6px 12px; border-radius: 8px; }
  
    /* ---------- 08 · cta ---------- */
    .final { text-align: center; }
    .final h2 { font-size: clamp(29px, 4.3vw, 44px); font-weight: 800; letter-spacing: -.03em; margin: 0 0 24px; color: #fff; text-wrap: balance; }
  
    /* ---------- 09 · footer ---------- */
    footer.site { background: #08131F; color: rgba(255,255,255,.66); padding: 60px 0 32px; }
    .foot-grid { display: grid; grid-template-columns: 1.4fr repeat(4, 1fr); gap: 32px; margin-bottom: 42px; }
    .foot-brand .brand-word { color: #fff; }
    .foot-brand p { margin: 13px 0 0; font-size: 14.5px; max-width: 30ch; color: rgba(255,255,255,.5); }
    footer h4 { font-family: Manrope, sans-serif; color: #fff; font-size: 12.5px; font-weight: 700; letter-spacing: .13em; text-transform: uppercase; margin: 0 0 14px; }
    footer ul { list-style: none; padding: 0; margin: 0; display: flex; flex-direction: column; gap: 10px; }
    footer a { color: rgba(255,255,255,.66); text-decoration: none; font-size: 14.5px; }
    footer a:hover { color: #fff; }
    .foot-bottom { border-top: 1px solid rgba(255,255,255,.1); padding-top: 22px; font-size: 13.5px; color: rgba(255,255,255,.44); }
    @media (max-width: 900px) { .foot-grid { grid-template-columns: 1fr 1fr; gap: 28px; } }
  
    .rise { opacity: 0; transform: translateY(14px); transition: opacity .55s ease, transform .55s ease; }
    .rise.seen { opacity: 1; transform: none; }
    @keyframes rise { from { opacity: 0; transform: translateY(10px); } to { opacity: 1; transform: none; } }
    @media (prefers-reduced-motion: reduce) { .rise { opacity: 1; transform: none; transition: none; } .stage-panel.on { animation: none; } html { scroll-behavior: auto; } }
    html { scroll-behavior: smooth; }
  
    /* ---------- navigation entre pages ---------- */
    .page[hidden] { display: none; }
    .nav a.on { color: var(--blue); }
  
    /* ---------- page Fonctionnalités ---------- */
    .feat-page { display: grid; grid-template-columns: 1fr 1.05fr; gap: 46px; align-items: center; max-width: 1000px; margin: 0 auto; }
    .feat-list { display: flex; flex-direction: column; gap: 26px; }
    .feat-row { display: flex; gap: 16px; align-items: flex-start; }
    .feat-row .ic { width: 42px; height: 42px; border-radius: 11px; background: var(--blue-soft); display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
    .feat-row h3 { font-size: 16.5px; font-weight: 700; margin: 0 0 4px; }
    .feat-row p { margin: 0; color: var(--ink-soft); font-size: 15px; line-height: 1.55; }
    @media (max-width: 880px) { .feat-page { grid-template-columns: 1fr; gap: 36px; } }
  
    .more { display: grid; grid-template-columns: repeat(5, 1fr); gap: 16px; }
    .more-item { text-align: center; display: flex; flex-direction: column; align-items: center; gap: 11px; font-size: 14.5px; font-weight: 600; color: rgba(255,255,255,.84); }
    @media (max-width: 820px) { .more { grid-template-columns: repeat(2, 1fr); gap: 26px; } }
  
    /* ---------- page Calypso Auto ---------- */
    .auto-hero { display: grid; grid-template-columns: 1.05fr .95fr; gap: 50px; align-items: center; max-width: 980px; margin: 0 auto; }
    .bot-wrap { max-width: 300px; justify-self: center; }
    @media (max-width: 820px) { .auto-hero { grid-template-columns: 1fr; gap: 32px; } .bot-wrap { max-width: 220px; } }
  
    /* ---------- page Contact ---------- */
    .contact-grid { display: grid; grid-template-columns: .85fr 1.15fr; gap: 44px; max-width: 980px; margin: 0 auto; align-items: start; }
    .contact-info { display: flex; flex-direction: column; gap: 24px; }
    .ci { display: flex; gap: 14px; align-items: flex-start; }
    .ci .ic { width: 38px; height: 38px; border-radius: 10px; background: var(--blue-soft); display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
    .ci h4 { font-family: Manrope, sans-serif; font-size: 14px; font-weight: 700; margin: 0 0 4px; letter-spacing: 0; text-transform: none; color: var(--ink); }
    .ci p { margin: 0; font-size: 14.5px; color: var(--ink-soft); }
    .contact-form { background: var(--white); border: 1px solid var(--rule); border-radius: var(--r); box-shadow: var(--sh-sm); padding: 28px; }
    .contact-form label { display: block; font-family: Manrope, sans-serif; font-size: 13.5px; font-weight: 700; margin: 0 0 6px; }
    .contact-form label + input, .contact-form label + textarea { margin-bottom: 18px; }
    .contact-form input, .contact-form textarea {
      width: 100%; border: 1.5px solid var(--rule); border-radius: var(--r-sm);
      padding: 11px 14px; font-family: inherit; font-size: 15px; color: var(--ink);
      background: var(--white); transition: border-color .16s ease;
    }
    .contact-form input:focus, .contact-form textarea:focus { outline: none; border-color: var(--blue); }
    .contact-form textarea { resize: vertical; }
    @media (max-width: 820px) { .contact-grid { grid-template-columns: 1fr; gap: 32px; } }
  
    /* ---------- pages légales ---------- */
    .legal { max-width: 760px; }
    .legal h2 { font-family: Manrope, sans-serif; font-size: clamp(28px, 4vw, 38px); font-weight: 800; letter-spacing: -.025em; margin: 0 0 8px; }
    .legal h3 { font-size: 17.5px; font-weight: 700; margin: 32px 0 10px; }
    .legal p { color: var(--ink-soft); margin: 0 0 12px; }
    .legal-date { font-size: 14.5px; }
    .legal-warn {
      background: #FFF4E0; border: 1px solid #F0D9A8; color: #6E4906;
      border-radius: var(--r-sm); padding: 16px 20px; margin: 22px 0 6px; font-size: 15px;
    }
    .legal .todo { display: block; }
  `]
})
export class FranceLandingComponent implements AfterViewInit, OnDestroy {
  private observer?: IntersectionObserver;

  constructor(private host: ElementRef<HTMLElement>) {}

  /** Onglet visible dans « Calypso en action ». */
  activeTab = 'p1';

  selectTab(id: string) {
    this.activeTab = id;
  }

  /**
   * Apparition discrète au défilement. Sans cet observateur, les blocs portant
   * la classe `rise` resteraient à opacité nulle : le CSS les masque en
   * attendant d'être révélés.
   */
  ngAfterViewInit(): void {
    const blocks = Array.from(
      this.host.nativeElement.querySelectorAll<HTMLElement>('.rise'));

    const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (reduce || !('IntersectionObserver' in window)) {
      blocks.forEach(b => b.classList.add('seen'));
      return;
    }

    this.observer = new IntersectionObserver(entries => {
      entries.forEach(e => {
        if (e.isIntersecting) {
          e.target.classList.add('seen');
          this.observer?.unobserve(e.target);
        }
      });
    }, { threshold: 0.12 });

    blocks.forEach(b => this.observer!.observe(b));
  }

  ngOnDestroy(): void {
    this.observer?.disconnect();
  }
}
