import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/** Page 8 — Adresse inconnue à l'intérieur du site France. */
@Component({
  selector: 'app-france-not-found',
  standalone: true,
  imports: [RouterLink],
  template: `
    <section class="band-glow">
      <div class="shell" style="text-align:center;max-width:660px">
        <svg viewBox="0 0 260 130" fill="none" aria-hidden="true" style="width:100%;max-width:340px;height:auto;margin:0 auto 8px">
          <path d="M8 108h244" stroke="rgba(255,255,255,.12)" stroke-width="3" stroke-linecap="round"/>
          <path d="M40 108c22-46 66-72 108-58" stroke="rgba(96,165,250,.28)" stroke-width="3"
                stroke-linecap="round" stroke-dasharray="10 12"/>
          <rect x="152" y="70" width="80" height="30" rx="9" fill="#60A5FA"/>
          <path d="M158 70l10-16h44l12 16" fill="#818CF8"/>
          <circle cx="172" cy="103" r="9" fill="#0B1020"/>
          <circle cx="216" cy="103" r="9" fill="#0B1020"/>
          <path d="M74 34h30l-15 26z" fill="#F59E0B"/>
          <path d="M89 42v8" stroke="#0B1020" stroke-width="3" stroke-linecap="round"/>
          <circle cx="89" cy="55" r="1.8" fill="#0B1020"/>
        </svg>

        <p class="grad-txt" style="font-size:clamp(72px,14vw,120px);font-weight:800;letter-spacing:-.05em;line-height:1;margin:0 0 10px">404</p>
        <h2 style="font-size:clamp(22px,3.4vw,30px);font-weight:800;letter-spacing:-.02em;margin:0 0 12px">
          Cette route n'était pas prévue.
        </h2>
        <p style="color:var(--txt-soft);margin:0 0 28px">
          La page que vous cherchez n'existe pas ou a été déplacée.
        </p>
        <div class="hero-cta" style="justify-content:center">
          <a class="btn btn-grad" routerLink="/fr">Retour à l'accueil</a>
          <a class="btn btn-line" routerLink="/fr/calypso-auto">Explorer Calypso Auto</a>
        </div>
      </div>
    </section>
  `
})
export class FranceNotFoundComponent {}
