import { Component, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { environment } from '../environments/environment';
import { RegionService } from '../services/region.service';
import { AiLandingComponent } from './ai-landing.component';
import { LandingComponent } from './landing.component';

/**
 * Page d'aiguillage de la racine : vitrine européenne ou accueil habituel.
 *
 * <p>Les signaux immédiats (domaine, forçage, session) sont tranchés avant le
 * premier rendu. Sinon, l'API est interrogée sur le pays de l'adresse — 600 ms
 * maximum : au-delà, l'accueil habituel s'affiche sans attendre la réponse.
 * Le visiteur européen dont la réponse arriverait après ce délai verra
 * l'accueil habituel cette fois-ci ; le résultat étant gardé en session, le
 * moindre rechargement le mènera au bon endroit. Ce compromis protège la
 * majorité des visiteurs (locaux) d'un écran vide sur liaison lente.</p>
 *
 * <p><code>decided</code> est un signal, pas un simple champ : pendant la
 * navigation initiale du routeur, une affectation faite après un
 * <code>await</code> ne déclenchait AUCUN rafraîchissement — la page restait
 * noire, sans erreur. Constaté en production le 26/08/2026 ; le signal notifie
 * la vue par construction, quel que soit le contexte d'exécution.</p>
 */
@Component({
  selector: 'app-region-gate',
  standalone: true,
  imports: [AiLandingComponent, LandingComponent],
  template: `
    @if (decided()) {
      @if (aiLanding) {
        <app-ai-landing />
      } @else {
        <app-landing />
      }
    }
  `,
  // Pendant la décision (< 600 ms), un fond neutre plutôt qu'un flash blanc.
  styles: [`:host { display: block; min-height: 100vh; background: #05070F; }`]
})
export class RegionGateComponent implements OnInit {
  private readonly region = inject(RegionService);
  private readonly router = inject(Router);

  readonly aiLanding = (environment as any).aiAssistantLanding === true;
  readonly decided = signal(false);

  async ngOnInit(): Promise<void> {
    if (this.region.isEurope) {
      this.router.navigateByUrl('/fr', { replaceUrl: true });
      return;
    }
    const resolved = await this.region.resolveByCountry();
    if (resolved === 'europe') {
      this.router.navigateByUrl('/fr', { replaceUrl: true });
      return;
    }
    this.decided.set(true);
  }

}
