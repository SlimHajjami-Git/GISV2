import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/** Page 2 — Fonctionnalités. Liste illustrée, captures réelles, bandeau nuit. */
@Component({
  selector: 'app-france-features',
  standalone: true,
  imports: [RouterLink],
  template: `
    <section class="band-sky tight">
      <div class="shell">
        <div class="sec-head rise">
          <h2>Toutes les fonctionnalités incluses</h2>
          <p>Une solution complète pour une gestion de parc automobile efficace.</p>
        </div>
        <div class="feat-page rise">
          <div class="feat-list">
            <div class="feat-row">
              <div class="ic"><svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="#1B4FD8" stroke-width="2" aria-hidden="true"><path d="M14.7 6.3a4 4 0 0 0 4.6 5.7l-8.3 8.3a2 2 0 0 1-2.8-2.8l8.3-8.3a4 4 0 0 0-1.8-2.9z"/></svg></div>
              <div>
                <h3>Entretiens</h3>
                <p>Planifiez, suivez et anticipez tous vos entretiens. L'historique
                   complet reste attaché au véhicule.</p>
              </div>
            </div>
            <div class="feat-row">
              <div class="ic"><svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="#1B4FD8" stroke-width="2" aria-hidden="true"><path d="M3 13h18M5 13l1.5-5A2 2 0 0 1 8.4 6.5h7.2a2 2 0 0 1 1.9 1.5L19 13v5h-2v-2H7v2H5z"/></svg></div>
              <div>
                <h3>Réparations</h3>
                <p>Pièces, main-d'œuvre et garanties consignées intervention par
                   intervention, avec le coût réel de chacune.</p>
              </div>
            </div>
            <div class="feat-row">
              <div class="ic"><svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="#1B4FD8" stroke-width="2" aria-hidden="true"><path d="M4 20V5a2 2 0 0 1 2-2h6a2 2 0 0 1 2 2v15M3 20h13M17 9l2.5 2.5V17a1.5 1.5 0 0 1-3 0V9z"/></svg></div>
              <div>
                <h3>Carburant</h3>
                <p>Saisie manuelle ou import Excel, prix au litre, volume et
                   consommation moyenne — les écarts anormaux remontent seuls.</p>
              </div>
            </div>
            <div class="feat-row">
              <div class="ic"><svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="#1B4FD8" stroke-width="2" aria-hidden="true"><path d="M12 2v20M17 6H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/></svg></div>
              <div>
                <h3>Dépenses</h3>
                <p>Carburant, péages, assurance, fournitures : toutes les charges
                   du parc au même endroit, ventilées par catégorie.</p>
              </div>
            </div>
            <div class="feat-row">
              <div class="ic"><svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="#1B4FD8" stroke-width="2" aria-hidden="true"><rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4"/></svg></div>
              <div>
                <h3>Échéances</h3>
                <p>Visite technique, assurance, vignette : chaque date est suivie
                   et signalée avant qu'elle ne tombe.</p>
              </div>
            </div>
            <div class="feat-row">
              <div class="ic"><svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="#1B4FD8" stroke-width="2" aria-hidden="true"><path d="M4 20V9M10 20V4M16 20v-7M22 20H2"/></svg></div>
              <div>
                <h3>Rapports et tableaux de bord</h3>
                <p>L'état du parc en temps réel, exportable en PDF ou en Excel
                   pour vos comptes rendus.</p>
              </div>
            </div>
          </div>
          <div class="shot">
            <img src="/assets/france/produit-tableau-de-bord-clair.webp" width="1536" height="1024"
                 alt="Tableau de bord Calypso : répartition de la flotte, consommation de carburant et dépenses par catégorie."
                 loading="lazy">
          </div>
        </div>
      </div>
    </section>

    <section class="band-white tight">
      <div class="shell">
        <div class="sec-head rise">
          <h2>Le produit, tel qu'il est</h2>
          <p>Des écrans réels de l'application, sans mise en scène.</p>
        </div>
        <div class="gallery rise">
          <figure>
            <img class="shot-img" src="/assets/france/produit-carburant.webp" width="502" height="416"
                 alt="Écran de gestion du carburant : saisie d'une entrée et dernières opérations enregistrées."
                 loading="lazy">
            <figcaption>Carburant et consommation</figcaption>
          </figure>
          <figure>
            <img class="shot-img" src="/assets/france/produit-entretiens.webp" width="500" height="416"
                 alt="Écran des entretiens : progression de chaque échéance et kilomètres restants."
                 loading="lazy">
            <figcaption>Entretiens et échéances</figcaption>
          </figure>
          <figure>
            <img class="shot-img" src="/assets/france/produit-reparations.webp" width="503" height="416"
                 alt="Écran des réparations : références, pièces, main-d'œuvre et coût total par intervention."
                 loading="lazy">
            <figcaption>Réparations et dépenses</figcaption>
          </figure>
          <figure>
            <img class="shot-img" src="/assets/france/produit-tableau-de-bord.webp" width="466" height="446"
                 alt="Vue globale du parc : santé des véhicules, dépenses par catégorie et alertes récentes."
                 loading="lazy">
            <figcaption>Vue globale du parc</figcaption>
          </figure>
          <figure>
            <img class="shot-img" src="/assets/france/produit-depenses.webp" width="492" height="446"
                 alt="Journal des dépenses : date, véhicule, catégorie et montant."
                 loading="lazy">
            <figcaption>Journal des dépenses</figcaption>
          </figure>
          <figure>
            <img class="shot-img" src="/assets/france/produit-echeances.webp" width="501" height="446"
                 alt="Suivi des échéances : visites techniques, assurances et vignettes avec leur date d'expiration."
                 loading="lazy">
            <figcaption>Suivi des échéances</figcaption>
          </figure>
        </div>
      </div>
    </section>

    <section class="band-navy">
      <div class="shell">
        <div class="sec-head rise">
          <h2>Et bien plus encore…</h2>
        </div>
        <div class="more rise">
          <div class="more-item">
            <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="#5FE3BD" stroke-width="1.8" aria-hidden="true"><circle cx="9" cy="8" r="3"/><path d="M3 20a6 6 0 0 1 12 0M17 11a3 3 0 1 0-2-5.2M21 20a5 5 0 0 0-3-4.6"/></svg>
            <span>Gestion multi-utilisateurs</span>
          </div>
          <div class="more-item">
            <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="#23A6C9" stroke-width="1.8" aria-hidden="true"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/></svg>
            <span>Historique complet</span>
          </div>
          <div class="more-item">
            <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="#6B5CE7" stroke-width="1.8" aria-hidden="true"><path d="M12 3v12M8 11l4 4 4-4M4 20h16"/></svg>
            <span>Export de données</span>
          </div>
          <div class="more-item">
            <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="#5FE3BD" stroke-width="1.8" aria-hidden="true"><path d="M18 8a6 6 0 1 0-12 0c0 7-3 8-3 8h18s-3-1-3-8M13.7 21a2 2 0 0 1-3.4 0"/></svg>
            <span>Alertes intelligentes</span>
          </div>
          <div class="more-item">
            <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="#23A6C9" stroke-width="1.8" aria-hidden="true"><rect x="7" y="2" width="10" height="20" rx="2"/><path d="M11 18h2"/></svg>
            <span>Application mobile</span>
          </div>
        </div>
        <div class="final" style="margin-top:52px">
          <h2>Prêt à simplifier la gestion de votre parc ?</h2>
          <a class="btn btn-light" routerLink="/inscription">Essayer gratuitement</a>
        </div>
      </div>
    </section>
  `
})
export class FranceFeaturesComponent {}
