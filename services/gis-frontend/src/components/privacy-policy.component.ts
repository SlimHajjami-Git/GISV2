import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-privacy-policy',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <div class="privacy-page">
      <header class="privacy-header">
        <div class="container">
          <a [routerLink]="['/']" class="back-link" aria-label="Retour à l'accueil">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
              <path d="M19 12H5"/>
              <polyline points="12 19 5 12 12 5"/>
            </svg>
            <span>Retour</span>
          </a>

          <div class="brand">
            <svg width="32" height="32" viewBox="0 0 40 40" fill="none" aria-hidden="true">
              <rect width="40" height="40" rx="8" fill="#2563eb"/>
              <path d="M12 20L18 26L28 14" stroke="white" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
            </svg>
            <span class="brand-name">Calypso</span>
          </div>
        </div>
      </header>

      <main class="privacy-main">
        <article class="container document">
          <div class="doc-header">
            <h1>Politique de confidentialité – Calypso Mobile</h1>
            <p class="last-updated"><strong>Dernière mise à jour :</strong> 20/04/2026</p>
          </div>

          <section>
            <h2>1. Présentation</h2>
            <p>
              Calypso Mobile est une application mobile développée par <strong>Belive</strong>,
              permettant la gestion de flotte et la géolocalisation des véhicules en temps réel.
            </p>
            <p>
              La présente politique de confidentialité explique quelles données nous collectons,
              comment nous les utilisons et comment nous les protégeons.
            </p>
            <p>En utilisant l'application Calypso Mobile, vous acceptez cette politique.</p>
          </section>

          <section>
            <h2>2. Données collectées</h2>
            <p>Nous pouvons collecter les types de données suivants :</p>

            <h3>a. Données personnelles</h3>
            <ul>
              <li>Nom et prénom</li>
              <li>Adresse e-mail</li>
              <li>Numéro de téléphone</li>
              <li>Informations de connexion (compte utilisateur)</li>
            </ul>

            <h3>b. Données de localisation</h3>
            <ul>
              <li>Position GPS en temps réel des véhicules</li>
              <li>Historique des trajets</li>
            </ul>
            <p>
              Les données de localisation peuvent être collectées même lorsque l'application
              fonctionne en arrière-plan, selon les autorisations accordées.
            </p>

            <h3>c. Données techniques</h3>
            <ul>
              <li>Type d'appareil et système d'exploitation</li>
              <li>Adresse IP</li>
              <li>Journaux d'utilisation</li>
              <li>Identifiants uniques de l'appareil</li>
            </ul>

            <h3>d. Données liées aux véhicules</h3>
            <ul>
              <li>Niveau et tension de la batterie</li>
              <li>Consommation de carburant</li>
              <li>Données CAN bus (si disponibles)</li>
              <li>Alertes et événements techniques</li>
            </ul>
          </section>

          <section>
            <h2>3. Utilisation des données</h2>
            <p>Les données collectées sont utilisées pour :</p>
            <ul>
              <li>Assurer le suivi en temps réel des véhicules</li>
              <li>Générer des rapports et statistiques</li>
              <li>Envoyer des alertes et notifications</li>
              <li>Améliorer les performances de l'application</li>
              <li>Garantir la sécurité et la fiabilité du service</li>
            </ul>
          </section>

          <section>
            <h2>4. Partage des données</h2>
            <p>Nous ne vendons ni ne louons vos données personnelles.</p>
            <p>Les données peuvent être partagées uniquement dans les cas suivants :</p>
            <ul>
              <li>Avec votre organisation (entreprise ou gestionnaire de flotte)</li>
              <li>Avec des prestataires techniques (hébergement, maintenance)</li>
              <li>En cas d'obligation légale</li>
            </ul>
          </section>

          <section>
            <h2>5. Sécurité des données</h2>
            <p>Nous mettons en œuvre des mesures de sécurité appropriées :</p>
            <ul>
              <li>Chiffrement des échanges (HTTPS)</li>
              <li>Systèmes d'authentification sécurisés</li>
              <li>Contrôle des accès</li>
              <li>Sauvegardes régulières</li>
            </ul>
          </section>

          <section>
            <h2>6. Conservation des données</h2>
            <p>
              Les données sont conservées uniquement pendant la durée nécessaire à la fourniture
              du service ou pour répondre aux obligations légales.
            </p>
          </section>

          <section>
            <h2>7. Vos droits</h2>
            <p>Selon la réglementation applicable, vous disposez des droits suivants :</p>
            <ul>
              <li>Accéder à vos données</li>
              <li>Corriger vos données</li>
              <li>Demander leur suppression</li>
              <li>Vous opposer à leur traitement</li>
            </ul>
            <p class="contact-line">
              Pour exercer vos droits :
              <a href="mailto:contact@belive.tn" class="email-link">contact&#64;belive.tn</a>
            </p>
          </section>

          <section>
            <h2>8. Autorisations de l'application</h2>
            <p>Calypso Mobile peut demander les autorisations suivantes :</p>
            <ul>
              <li><strong>Localisation</strong> : pour le suivi des véhicules</li>
              <li><strong>Accès Internet</strong> : pour la synchronisation des données</li>
              <li><strong>Notifications</strong> : pour les alertes et mises à jour</li>
            </ul>
            <p>Vous pouvez modifier ces autorisations dans les paramètres de votre appareil.</p>
          </section>

          <section>
            <h2>9. Données des enfants</h2>
            <p>L'application n'est pas destinée aux enfants de moins de 13 ans.</p>
            <p>Nous ne collectons pas volontairement leurs données.</p>
          </section>

          <section>
            <h2>10. Modifications</h2>
            <p>Cette politique peut être mise à jour à tout moment.</p>
            <p>Toute modification sera publiée dans l'application.</p>
          </section>

          <section>
            <h2>11. Contact</h2>
            <p class="contact-line">
              Pour toute question :
              <a href="mailto:contact@belive.tn" class="email-link">contact&#64;belive.tn</a>
            </p>
          </section>
        </article>
      </main>

      <footer class="privacy-footer">
        <div class="container">
          <p>&copy; 2026 Belive · Calypso. Tous droits réservés.</p>
        </div>
      </footer>
    </div>
  `,
  styles: [`
    :host {
      display: block;
    }

    .privacy-page {
      min-height: 100vh;
      background: #f8fafc;
      display: flex;
      flex-direction: column;
      color: #1e293b;
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
    }

    .container {
      max-width: 880px;
      margin: 0 auto;
      padding: 0 24px;
      width: 100%;
      box-sizing: border-box;
    }

    .privacy-header {
      background: #ffffff;
      border-bottom: 1px solid #e2e8f0;
      padding: 16px 0;
      position: sticky;
      top: 0;
      z-index: 10;
    }

    .privacy-header .container {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 16px;
    }

    .back-link {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      color: #475569;
      text-decoration: none;
      font-weight: 500;
      font-size: 14px;
      padding: 8px 12px;
      border-radius: 8px;
      transition: background 0.15s ease, color 0.15s ease;
    }

    .back-link:hover {
      background: #f1f5f9;
      color: #2563eb;
    }

    .brand {
      display: inline-flex;
      align-items: center;
      gap: 10px;
    }

    .brand-name {
      font-weight: 700;
      font-size: 18px;
      color: #1e293b;
    }

    .privacy-main {
      flex: 1;
      padding: 48px 0;
    }

    .document {
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 16px;
      padding: 48px;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.04);
    }

    .doc-header {
      border-bottom: 1px solid #e2e8f0;
      padding-bottom: 24px;
      margin-bottom: 32px;
    }

    .doc-header h1 {
      font-size: 30px;
      line-height: 1.25;
      font-weight: 700;
      color: #0f172a;
      margin: 0 0 12px;
    }

    .last-updated {
      color: #64748b;
      font-size: 14px;
      margin: 0;
    }

    section {
      margin-bottom: 32px;
    }

    section:last-child {
      margin-bottom: 0;
    }

    h2 {
      font-size: 20px;
      font-weight: 700;
      color: #0f172a;
      margin: 0 0 12px;
    }

    h3 {
      font-size: 16px;
      font-weight: 600;
      color: #1e293b;
      margin: 20px 0 10px;
    }

    p {
      font-size: 15px;
      line-height: 1.7;
      color: #334155;
      margin: 0 0 10px;
    }

    ul {
      margin: 8px 0 12px;
      padding-left: 22px;
    }

    ul li {
      font-size: 15px;
      line-height: 1.7;
      color: #334155;
      margin-bottom: 4px;
    }

    .contact-line {
      margin-top: 12px;
    }

    .email-link {
      color: #2563eb;
      text-decoration: none;
      font-weight: 500;
    }

    .email-link:hover {
      text-decoration: underline;
    }

    .privacy-footer {
      background: #ffffff;
      border-top: 1px solid #e2e8f0;
      padding: 24px 0;
      text-align: center;
    }

    .privacy-footer p {
      color: #64748b;
      font-size: 13px;
      margin: 0;
    }

    @media (max-width: 640px) {
      .privacy-main {
        padding: 24px 0;
      }

      .document {
        padding: 28px 20px;
        border-radius: 12px;
      }

      .doc-header h1 {
        font-size: 24px;
      }

      h2 {
        font-size: 18px;
      }

      .brand-name {
        display: none;
      }
    }
  `]
})
export class PrivacyPolicyComponent {}
