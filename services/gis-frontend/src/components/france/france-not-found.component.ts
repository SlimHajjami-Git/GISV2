import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/** Page 8 — Adresse inconnue à l'intérieur du site France. */
@Component({
  selector: 'app-france-not-found',
  standalone: true,
  imports: [RouterLink],
  template: `
    <section class="band-sky">
      <div class="shell" style="text-align:center;max-width:660px">
        <svg viewBox="0 0 260 130" fill="none" aria-hidden="true" style="width:100%;max-width:340px;height:auto;margin:0 auto 8px">
          <path d="M8 108h244" stroke="#DCE6F2" stroke-width="3" stroke-linecap="round"/>
          <path d="M40 108c22-46 66-72 108-58" stroke="#C8D8EC" stroke-width="3"
                stroke-linecap="round" stroke-dasharray="10 12"/>
          <rect x="152" y="70" width="80" height="30" rx="9" fill="#1B4FD8"/>
          <path d="M158 70l10-16h44l12 16" fill="#4C79E4"/>
          <circle cx="172" cy="103" r="9" fill="#0C1A33"/>
          <circle cx="216" cy="103" r="9" fill="#0C1A33"/>
          <path d="M74 34h30l-15 26z" fill="#F0A21C"/>
          <path d="M89 42v8" stroke="#0C1A33" stroke-width="3" stroke-linecap="round"/>
          <circle cx="89" cy="55" r="1.8" fill="#0C1A33"/>
        </svg>

        <p style="font-family:Manrope,sans-serif;font-size:clamp(64px,13vw,104px);font-weight:800;letter-spacing:-.05em;line-height:1;margin:0 0 10px;color:var(--ink)">404</p>
        <h2 style="font-size:clamp(22px,3.4vw,30px);font-weight:800;letter-spacing:-.02em;margin:0 0 12px">
          Cette route n'était pas prévue.
        </h2>
        <p style="color:var(--ink-soft);margin:0 0 28px">
          La page que vous cherchez n'existe pas ou a été déplacée.
        </p>
        <div class="hero-cta" style="justify-content:center">
          <a class="btn btn-primary" routerLink="/fr">Retour à l'accueil</a>
          <a class="btn btn-ghost" routerLink="/fr/calypso-auto">Explorer Calypso Auto</a>
        </div>
      </div>
    </section>
  `
})
export class FranceNotFoundComponent {}
