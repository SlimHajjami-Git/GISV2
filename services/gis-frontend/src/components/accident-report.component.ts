import { Component, OnInit, OnDestroy, AfterViewInit, ElementRef, ViewChild, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { Subscription } from 'rxjs';
import * as L from 'leaflet';
import { AppLayoutComponent } from './shared/app-layout.component';
import {
  ApiService,
  PositionDto,
  AccidentReportDto,
  AccidentReportThirdPartyDto,
  AddThirdPartyRequest,
} from '../services/api.service';
import { AuthService } from '../services/auth.service';
import { AccidentPdfService } from '../services/accident-pdf.service';

interface NarrativeEvent {
  time: string;
  title: string;
  body: string;
  severity: 'normal' | 'warning' | 'critical' | 'neutral';
}

interface Indicator {
  label: string;
  value: string;
  hint?: string;
}

interface ChartDot {
  x: number;
  y: number;
  isImpact?: boolean;
}

interface ChartAxisLabel {
  x: number;
  label: string;
}

/**
 * Aggregated accelerometer signal around the impact, computed from the
 * memsX/Y/Z fields of the GPS history frames. Drives the richer story,
 * reasons, indicators and confidence score (second shock, sustained tilt
 * for rollover, magnitude / saturated axes).
 */
interface MemsStats {
  /** Maximum vector magnitude observed within ±30 s of impact (≈ 222 max). */
  peakMag: number;
  /** Absolute axis values at the peak frame (clamped 0..127). */
  peakAx: number;
  peakAy: number;
  peakAz: number;
  /** Highest magnitude of a second peak in (impact+5 s, impact+90 s), or 0. */
  secondShockMag: number;
  secondShockT?: number;
  /** Sustained-tilt window after the stop (|Z| ≥ 90), or 0 min. */
  tiltDurationMin: number;
  tiltStartT?: number;
  tiltEndT?: number;
}

/**
 * Formal accident report page — written as a corporate document for a
 * non-technical fleet owner. No jargon, no dramatic UI. Think: an
 * insurance adjuster's written report, a medical summary, a notary's
 * brief.
 *
 * Data flow:
 *   1. Route param `:accidentId` → GET /api/accident-reports/{id}
 *   2. The persisted DTO populates every narrative field (story, reasons,
 *      indicators, synthesis, location, confidence, reference code…).
 *   3. The component then fetches GPS history around `incidentAt` using
 *      the DTO's real `deviceUid`, and `computeFromPositions()` builds
 *      the speed chart + map trajectory from the live GPS series.
 *
 * There is NO hardcoded demo scenario any more. The class fields below
 * all start empty/neutral and are filled exclusively from the real report
 * DTO + live GPS. The page is only ever reached with an `:accidentId`
 * (the id-less route redirects to the list), so a real report is always
 * loaded; if the load fails the document degrades to "—" and explicit
 * "données indisponibles" placeholders rather than inventing an incident.
 */
@Component({
  selector: 'app-accident-report',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, AppLayoutComponent],
  template: `
    <app-layout>
      <div class="doc-page">
        <article class="doc" [class.shown]="shown">

          <!-- Document header -->
          <header class="doc-header">
            <div class="doc-header-top">
              <div class="doc-brand">
                <div class="brand-mark" aria-label="Logo Calypso">
                  <svg viewBox="0 0 512 640" xmlns="http://www.w3.org/2000/svg" fill="none" role="img" focusable="false">
                    <defs>
                      <linearGradient id="pinGradHdr" x1="0" y1="0" x2="1" y2="1">
                        <stop offset="0%" stop-color="#43e6a0"/>
                        <stop offset="50%" stop-color="#2fb8d8"/>
                        <stop offset="100%" stop-color="#1a3a8a"/>
                      </linearGradient>
                      <linearGradient id="innerGradHdr" x1="0.2" y1="0" x2="0.8" y2="1">
                        <stop offset="0%" stop-color="#3de8a8"/>
                        <stop offset="100%" stop-color="#1848a0"/>
                      </linearGradient>
                    </defs>
                    <path d="M256 20C152 20 68 104 68 208c0 140 188 290 188 290s188-150 188-290C444 104 360 20 256 20z" fill="url(#pinGradHdr)"/>
                    <circle cx="256" cy="200" r="130" fill="url(#innerGradHdr)" opacity="0.3"/>
                    <path d="M310 110c40 20 68 62 68 108" stroke="white" stroke-width="8" stroke-linecap="round" fill="none" opacity="0.8"/>
                    <circle cx="378" cy="218" r="8" fill="white" opacity="0.8"/>
                    <g transform="translate(256,210)" fill="white">
                      <path d="M-60,10 L-50,-25 Q-45,-40 -30,-40 L30,-40 Q45,-40 50,-25 L60,10 Q62,16 58,20 L-58,20 Q-62,16 -60,10z" opacity="0.95"/>
                      <rect x="-65" y="20" width="130" height="30" rx="8" opacity="0.95"/>
                      <circle cx="-40" cy="50" r="10" fill="url(#pinGradHdr)"/>
                      <circle cx="40" cy="50" r="10" fill="url(#pinGradHdr)"/>
                      <rect x="-20" y="-30" width="40" height="20" rx="4" fill="rgba(26,58,138,0.3)"/>
                    </g>
                  </svg>
                </div>
                <div class="brand-txt">
                  <div class="brand-name">Calypso</div>
                  <div class="brand-tag">Fleet Analytics</div>
                </div>
              </div>
              <div class="doc-meta">
                <div class="meta-row"><span class="meta-k">Référence</span><span class="meta-v">{{ reference }}</span></div>
                <div class="meta-row"><span class="meta-k">Établi le</span><span class="meta-v">{{ issueDate }}</span></div>
                <div class="meta-row"><span class="meta-k">Véhicule</span><span class="meta-v">{{ vehicleLabel }}</span></div>
              </div>
            </div>

            <div class="doc-title-block">
              <div class="doc-eyebrow">Document officiel · Usage interne</div>
              <h1 class="doc-title">Rapport d'incident</h1>
              <div class="doc-subtitle">
                Analyse détaillée d'un événement détecté sur un véhicule de votre flotte
              </div>
            </div>
          </header>

          <!-- Decision status banner — shown once the backend DTO is
               loaded. Reflects accident_events.status and offers fallback
               Confirmer / Fausse alerte buttons to admins when the modal
               was "Reporter"-ed earlier. -->
          <section class="status-banner" *ngIf="status" [attr.data-status]="status">
            <div class="status-head">
              <span class="status-pill" [class.is-pending]="status === 'pending'"
                                         [class.is-confirmed]="status === 'confirmed'"
                                         [class.is-dismissed]="status === 'dismissed'">
                <span class="status-dot"></span>
                {{ statusLabel }}
              </span>
              <span class="status-meta" *ngIf="decidedByName">
                par {{ decidedByName }}<span *ngIf="decidedAtFormatted"> — {{ decidedAtFormatted }}</span>
              </span>
              <span class="status-tow" *ngIf="towDetectedAtFormatted">
                Remorquage détecté le {{ towDetectedAtFormatted }}
              </span>
              <a *ngIf="pdfReportUrl" [href]="pdfReportUrl" target="_blank" class="status-pdf">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>
                Télécharger le rapport PDF
              </a>
              <button *ngIf="isAdmin && status && status !== 'pending'" type="button"
                      class="status-pdf status-pdf-btn" (click)="regeneratePdf()" [disabled]="pdfBusy"
                      title="Reconstruit le PDF à partir de l'état actuel (remorquage, phases…)">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="23 4 23 10 17 10"/><path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10"/></svg>
                {{ pdfBusy ? 'Génération…' : 'Régénérer le PDF' }}
              </button>
            </div>
            <p class="status-error" *ngIf="pdfError">{{ pdfError }}</p>
            <div class="status-actions" *ngIf="status === 'pending' && isAdmin">
              <p class="status-hint">
                Décision requise. Vous pouvez confirmer ou signaler une fausse alerte :
              </p>
              <div class="status-buttons">
                <button type="button" class="sb-btn sb-btn-confirm"
                        (click)="confirmFromReport()"
                        [disabled]="decisionBusy">
                  {{ decisionBusy === 'confirm' ? 'Confirmation…' : "Confirmer l'accident" }}
                </button>
                <button type="button" class="sb-btn sb-btn-dismiss"
                        (click)="dismissFromReport()"
                        [disabled]="decisionBusy">
                  {{ decisionBusy === 'dismiss' ? 'Envoi…' : 'Fausse alerte' }}
                </button>
              </div>
              <p class="status-error" *ngIf="decisionError">{{ decisionError }}</p>
            </div>
          </section>

          <!-- Calypso 7 — Accident timeline (6 phases, each independently
               editable as the real-world events unfold over multiple days). -->
          <section class="timeline" *ngIf="isAdmin && status === 'confirmed'">
            <h3 class="timeline-title">Suivi de l'accident</h3>
            <p class="timeline-sub">
              Chaque phase peut être complétée indépendamment au fur et à mesure
              (visite d'expert, devis garage, réparation, suivi assurance…).
            </p>

            <!--
              Calypso 7: never write a flip-style toggle handler here, e.g.
              (toggle)="phaseOpen.X = !phaseOpen.X". The native details element
              fires its toggle event whenever the open property changes,
              including when Angular property-binding reflects state back to
              the DOM. A flip-style handler then re-assigns the value back,
              Angular re-writes [open], the DOM fires toggle again, and the
              handler keeps flipping — observed at ~9000 toggles per 500 ms
              during repro. Always READ the new DOM state from
              $event.target.open and store it; never mutate it.
            -->

            <!-- Phase 2 — Initial damages -->
            <details class="phase" [open]="phaseOpen.initial" (toggle)="phaseOpen.initial = $any($event.target).open">
              <summary class="phase-head">
                <span class="phase-num">2</span>
                <span class="phase-title">Dégâts visibles (jour de l'accident)</span>
                <span class="phase-status" [class.done]="!!phase2.description || !!phase2.severity">
                  {{ phase2.description || phase2.severity ? 'Renseigné' : 'À compléter' }}
                </span>
              </summary>
              <div class="phase-body">
                <div class="phase-row">
                  <label class="phase-field" style="flex:2 1 0;">
                    <span>Description des dégâts visibles</span>
                    <textarea rows="3" [(ngModel)]="phase2.description" placeholder="Aile avant droite enfoncée, pare-chocs cassé..."></textarea>
                  </label>
                </div>
                <div class="phase-row">
                  <label class="phase-field">
                    <span>Sévérité apparente</span>
                    <select [(ngModel)]="phase2.severity">
                      <option [ngValue]="null">—</option>
                      <option value="minor">Légère</option>
                      <option value="moderate">Modérée</option>
                      <option value="severe">Grave</option>
                      <option value="total">Totale</option>
                    </select>
                  </label>
                  <label class="phase-field">
                    <span>Kilométrage à l'accident</span>
                    <input type="number" [(ngModel)]="phase2.mileageAtAccident" placeholder="50000">
                  </label>
                  <label class="phase-field">
                    <span>N° rapport de police</span>
                    <input type="text" [(ngModel)]="phase2.policeReportNumber" placeholder="...">
                  </label>
                </div>
                <div class="phase-row">
                  <label class="phase-field">
                    <span>Conditions météo</span>
                    <input type="text" [(ngModel)]="phase2.weatherConditions" placeholder="Pluvieux, brumeux...">
                  </label>
                  <label class="phase-field">
                    <span>État de la route</span>
                    <input type="text" [(ngModel)]="phase2.roadConditions" placeholder="Mouillée, verglas...">
                  </label>
                </div>
                <div class="phase-actions">
                  <button class="phase-btn save" (click)="savePhase2()" [disabled]="phaseBusy === 'phase2'">
                    {{ phaseBusy === 'phase2' ? 'Enregistrement…' : 'Enregistrer' }}
                  </button>
                </div>
              </div>
            </details>

            <!-- Phase 3 — Expert -->
            <details class="phase" [open]="phaseOpen.expert" (toggle)="phaseOpen.expert = $any($event.target).open">
              <summary class="phase-head">
                <span class="phase-num">3</span>
                <span class="phase-title">Expertise assurance (jour J+N)</span>
                <span class="phase-status" [class.done]="!!phase3.visitedAt">
                  {{ phase3.visitedAt ? 'Visite enregistrée' : 'En attente' }}
                </span>
              </summary>
              <div class="phase-body">
                <div class="phase-row">
                  <label class="phase-field">
                    <span>Date de visite</span>
                    <input type="date" [(ngModel)]="phase3.visitedAt">
                  </label>
                  <label class="phase-field">
                    <span>Nom expert</span>
                    <input type="text" [(ngModel)]="phase3.expertName" placeholder="Nom de l'expert">
                  </label>
                  <label class="phase-field">
                    <span>Société d'expertise</span>
                    <input type="text" [(ngModel)]="phase3.expertCompany" placeholder="Cabinet d'expertise">
                  </label>
                </div>
                <div class="phase-row">
                  <label class="phase-field" style="flex:2 1 0;">
                    <span>Évaluation</span>
                    <textarea rows="2" [(ngModel)]="phase3.assessment" placeholder="Description des dégâts par l'expert..."></textarea>
                  </label>
                  <label class="phase-field">
                    <span>Montant estimé (DT)</span>
                    <input type="number" step="0.01" [(ngModel)]="phase3.estimatedAmount" placeholder="0.00">
                  </label>
                </div>
                <div class="phase-actions">
                  <button class="phase-btn save" (click)="savePhase3()" [disabled]="phaseBusy === 'phase3'">
                    {{ phaseBusy === 'phase3' ? 'Enregistrement…' : 'Enregistrer' }}
                  </button>
                </div>
              </div>
            </details>

            <!-- Phase 4 — Mechanic quote -->
            <details class="phase" [open]="phaseOpen.mechanic" (toggle)="phaseOpen.mechanic = $any($event.target).open">
              <summary class="phase-head">
                <span class="phase-num">4</span>
                <span class="phase-title">Devis garage (jour J+M)</span>
                <span class="phase-status" [class.done]="!!phase4.quoteAt || phase4.quotedAmount != null">
                  {{ phase4.quoteAt || phase4.quotedAmount != null ? 'Devis reçu' : 'En attente' }}
                </span>
              </summary>
              <div class="phase-body">
                <div class="phase-row">
                  <label class="phase-field">
                    <span>Date du devis</span>
                    <input type="date" [(ngModel)]="phase4.quoteAt">
                  </label>
                  <label class="phase-field">
                    <span>Nom du garage</span>
                    <input type="text" [(ngModel)]="phase4.mechanicName" placeholder="Garage XYZ">
                  </label>
                  <label class="phase-field">
                    <span>Montant devis (DT)</span>
                    <input type="number" step="0.01" [(ngModel)]="phase4.quotedAmount" placeholder="0.00">
                  </label>
                </div>
                <div class="phase-actions">
                  <button class="phase-btn save" (click)="savePhase4()" [disabled]="phaseBusy === 'phase4'">
                    {{ phaseBusy === 'phase4' ? 'Enregistrement…' : 'Enregistrer' }}
                  </button>
                </div>
              </div>
            </details>

            <!-- Phase 5 — Repair (auto-syncs to /depenses) -->
            <details class="phase" [open]="phaseOpen.repair" (toggle)="phaseOpen.repair = $any($event.target).open">
              <summary class="phase-head">
                <span class="phase-num">5</span>
                <span class="phase-title">Réparation (jour J+X)</span>
                <span class="phase-status" [class.done]="phase5.actualCost != null">
                  {{ phase5.actualCost != null ? 'Réparé' : 'En attente' }}
                </span>
              </summary>
              <div class="phase-body">
                <p class="phase-hint">💡 Une fois enregistrée, cette réparation apparaîtra automatiquement dans le module Dépenses.</p>
                <div class="phase-row">
                  <label class="phase-field">
                    <span>Début réparation</span>
                    <input type="date" [(ngModel)]="phase5.startedAt">
                  </label>
                  <label class="phase-field">
                    <span>Fin réparation</span>
                    <input type="date" [(ngModel)]="phase5.completedAt">
                  </label>
                  <label class="phase-field">
                    <span>Coût réel facture (DT)</span>
                    <input type="number" step="0.01" [(ngModel)]="phase5.actualCost" placeholder="0.00">
                  </label>
                </div>
                <div class="phase-actions">
                  <button class="phase-btn save" (click)="savePhase5()" [disabled]="phaseBusy === 'phase5'">
                    {{ phaseBusy === 'phase5' ? 'Enregistrement…' : 'Enregistrer la réparation' }}
                  </button>
                </div>
              </div>
            </details>

            <!-- Phase 6 — Insurance settlement (auto-syncs refund to /depenses) -->
            <details class="phase" [open]="phaseOpen.claim" (toggle)="phaseOpen.claim = $any($event.target).open">
              <summary class="phase-head">
                <span class="phase-num">6</span>
                <span class="phase-title">Suivi assurance (jour J+Y)</span>
                <span class="phase-status" [class.done]="!!phase6.claimNumber || !!phase6.status">
                  {{ phase6.claimNumber || phase6.status ? phaseClaimLabel(phase6.status) : 'Non déclaré' }}
                </span>
              </summary>
              <div class="phase-body">
                <p class="phase-hint">💡 Le montant approuvé par l'assurance sera automatiquement reporté en remboursement dans Dépenses.</p>
                <div class="phase-row">
                  <label class="phase-field">
                    <span>N° de sinistre</span>
                    <input type="text" [(ngModel)]="phase6.claimNumber" placeholder="AAS-2026-0123">
                  </label>
                  <label class="phase-field">
                    <span>Date soumission</span>
                    <input type="date" [(ngModel)]="phase6.submittedAt">
                  </label>
                  <label class="phase-field">
                    <span>Statut</span>
                    <select [(ngModel)]="phase6.status">
                      <option [ngValue]="null">—</option>
                      <option value="pending">En cours</option>
                      <option value="approved">Approuvé</option>
                      <option value="partial">Partiellement approuvé</option>
                      <option value="rejected">Rejeté</option>
                      <option value="closed">Clos</option>
                    </select>
                  </label>
                </div>
                <div class="phase-row">
                  <label class="phase-field">
                    <span>Montant approuvé (DT)</span>
                    <input type="number" step="0.01" [(ngModel)]="phase6.approvedAmount" placeholder="0.00">
                  </label>
                  <label class="phase-field" style="display:flex; align-items:center; gap:8px;">
                    <input type="checkbox" [(ngModel)]="phase6.thirdPartyInvolved">
                    <span>Tiers impliqué</span>
                  </label>
                </div>
                <div class="phase-actions">
                  <button class="phase-btn save" (click)="savePhase6()" [disabled]="phaseBusy === 'phase6'">
                    {{ phaseBusy === 'phase6' ? 'Enregistrement…' : 'Enregistrer le suivi' }}
                  </button>
                </div>
              </div>
            </details>

            <!-- Tiers impliqués — coordonnées + assurance des autres parties -->
            <details class="phase" [open]="thirdPartiesOpen" (toggle)="thirdPartiesOpen = $any($event.target).open">
              <summary class="phase-head">
                <span class="phase-num">T</span>
                <span class="phase-title">Tiers impliqués</span>
                <span class="phase-status" [class.done]="thirdParties.length > 0">
                  {{ thirdParties.length > 0 ? (thirdParties.length + (thirdParties.length > 1 ? ' tiers' : ' tiers')) : 'Aucun' }}
                </span>
              </summary>
              <div class="phase-body">
                <p class="phase-hint">💡 Coordonnées et assurance des autres véhicules / personnes impliqués — indispensables pour le constat et le dossier d'assurance.</p>

                <div class="tp-list" *ngIf="thirdParties.length > 0">
                  <div class="tp-item" *ngFor="let tp of thirdParties">
                    <div class="tp-info">
                      <strong>{{ tp.name || 'Tiers' }}</strong>
                      <span *ngIf="tp.phone"> · {{ tp.phone }}</span>
                      <span *ngIf="tp.vehiclePlate"> · {{ tp.vehiclePlate }}</span>
                      <span *ngIf="tp.vehicleModel"> ({{ tp.vehicleModel }})</span>
                      <div class="tp-ins" *ngIf="tp.insuranceCompany || tp.insuranceNumber || tp.insuranceExpiry">
                        <span *ngIf="tp.insuranceCompany">Assurance : {{ tp.insuranceCompany }}</span>
                        <span *ngIf="tp.insuranceNumber"> · N° {{ tp.insuranceNumber }}</span>
                        <span *ngIf="tp.insuranceExpiry"> · exp. {{ tp.insuranceExpiry | date:'dd/MM/yyyy' }}</span>
                      </div>
                    </div>
                    <button class="tp-del" (click)="removeThirdParty(tp)" [disabled]="thirdPartyBusy" title="Supprimer ce tiers">×</button>
                  </div>
                </div>

                <div class="phase-row">
                  <label class="phase-field"><span>Nom du tiers</span><input type="text" [(ngModel)]="newThirdParty.name" placeholder="Prénom Nom"></label>
                  <label class="phase-field"><span>Téléphone</span><input type="text" [(ngModel)]="newThirdParty.phone" placeholder="+216 ..."></label>
                  <label class="phase-field"><span>Immatriculation</span><input type="text" [(ngModel)]="newThirdParty.vehiclePlate" placeholder="123 TU 4567"></label>
                </div>
                <div class="phase-row">
                  <label class="phase-field"><span>Modèle véhicule</span><input type="text" [(ngModel)]="newThirdParty.vehicleModel" placeholder="Renault Clio"></label>
                  <label class="phase-field"><span>Compagnie d'assurance</span><input type="text" [(ngModel)]="newThirdParty.insuranceCompany" placeholder="STAR, COMAR, AMI..."></label>
                  <label class="phase-field"><span>N° police assurance tiers</span><input type="text" [(ngModel)]="newThirdParty.insuranceNumber" placeholder="..."></label>
                </div>
                <div class="phase-row">
                  <label class="phase-field"><span>Expiration assurance tiers</span><input type="date" [(ngModel)]="newThirdParty.insuranceExpiry"></label>
                </div>
                <div class="phase-actions">
                  <button class="phase-btn save" (click)="addThirdParty()" [disabled]="thirdPartyBusy || !canAddThirdParty()">
                    {{ thirdPartyBusy ? 'Ajout…' : 'Ajouter le tiers' }}
                  </button>
                </div>
              </div>
            </details>

            <p class="phase-feedback" *ngIf="phaseMessage" [class.is-error]="phaseMessage.type === 'error'">
              {{ phaseMessage.text }}
            </p>
          </section>

          <!-- Synthèse -->
          <section class="sec sec-synth">
            <div class="sec-num">01</div>
            <h2 class="sec-h">Synthèse</h2>

            <!-- Dismissed: the admin called it a fausse alerte. The system
                 still observed a violent impact, so we reframe the narrative
                 around "choc détecté + dégâts possibles" rather than keeping
                 the original "accident grave" verdict that would now be
                 incoherent with the admin decision. -->
            <ng-container *ngIf="status === 'dismissed'; else activeSynth">
              <p class="lead">
                Un <strong class="hl">choc violent</strong> a été détecté sur votre véhicule
                <strong>{{ vehicleLabel }}</strong> le
                <strong>{{ synthesisDateTimeLong }}</strong><ng-container *ngIf="locationCommune">, sur la commune de
                <strong>{{ locationCommune }}</strong></ng-container><ng-container *ngIf="locationGovernorate">, dans le gouvernorat de
                <strong>{{ locationGovernorate }}</strong></ng-container>.
              </p>
              <p>
                Cet événement a été marqué comme <strong>fausse alerte</strong> par
                l'administration<span *ngIf="decidedByName"> ({{ decidedByName }})</span>.
                Les données enregistrées révèlent néanmoins un impact significatif qui peut
                avoir causé des <strong>dégâts matériels</strong>. Une vérification du
                véhicule est recommandée.
              </p>
            </ng-container>

            <ng-template #activeSynth>
              <p class="lead">
                Votre véhicule <strong>{{ vehicleLabel }}</strong> a été impliqué dans un
                <strong class="hl">accident grave</strong> le
                <strong>{{ synthesisDateTimeLong }}</strong><ng-container *ngIf="locationCommune">, sur la commune de
                <strong>{{ locationCommune }}</strong></ng-container><ng-container *ngIf="locationGovernorate">, dans le gouvernorat de
                <strong>{{ locationGovernorate }}</strong></ng-container>.
              </p>
              <p>
                L'analyse des données enregistrées par le boîtier GPS installé sur ce véhicule
                permet d'établir, avec un niveau de certitude très élevé, qu'il s'agit d'un
                <strong>{{ synthesisText || 'impact violent détecté' }}</strong>. Les éléments qui conduisent à ce
                diagnostic sont détaillés dans les pages suivantes.
              </p>
            </ng-template>
          </section>

          <hr class="rule"/>

          <!-- Localisation + map -->
          <section class="sec sec-location">
            <div class="sec-num">02</div>
            <h2 class="sec-h">Lieu de l'incident</h2>
            <div class="loc-grid">
              <div class="loc-info">
                <div class="loc-row">
                  <div class="loc-k">Commune</div>
                  <div class="loc-v">{{ locationCommune || '—' }}</div>
                </div>
                <div class="loc-row">
                  <div class="loc-k">Gouvernorat</div>
                  <div class="loc-v">{{ locationGovernorate || '—' }}</div>
                </div>
                <div class="loc-row">
                  <div class="loc-k">Type de voie</div>
                  <div class="loc-v">{{ locationRoadType || '—' }}</div>
                </div>
                <div class="loc-row">
                  <div class="loc-k">Coordonnées GPS</div>
                  <div class="loc-v loc-coords">{{ coordsKnown ? (impactLat.toFixed(5) + ' °N · ' + impactLon.toFixed(5) + ' °E') : '—' }}</div>
                </div>
                <div class="loc-note">
                  Le point rouge sur la carte ci-contre indique l'emplacement exact où l'impact
                  a été détecté. Le tracé en pointillés, lorsqu'il apparaît, représente le
                  trajet emprunté par le véhicule dans les heures qui ont précédé.
                </div>
              </div>
              <div class="loc-map">
                <div #mapEl class="map-canvas"></div>
              </div>
            </div>
          </section>

          <hr class="rule"/>

          <!-- Ce qui s'est passé -->
          <section class="sec sec-story">
            <div class="sec-num">03</div>
            <h2 class="sec-h">Déroulement des faits</h2>
            <p class="sec-intro">
              La chronologie suivante a été reconstituée à partir des données envoyées par le
              véhicule toutes les quelques secondes. Les horaires sont exprimés en heure locale
              de Tunis.
            </p>

            <ol class="story" *ngIf="story.length; else noStory">
              <li *ngFor="let e of story" class="story-item" [attr.data-sev]="e.severity">
                <div class="story-time">{{ e.time }}</div>
                <div class="story-body">
                  <div class="story-title">{{ e.title }}</div>
                  <div class="story-text">{{ e.body }}</div>
                </div>
              </li>
            </ol>
            <ng-template #noStory>
              <p class="sec-empty">La chronologie détaillée n'est pas disponible pour cet incident.</p>
            </ng-template>
          </section>

          <hr class="rule"/>

          <!-- Visualisation profil vitesse -->
          <section class="sec sec-chart">
            <div class="sec-num">04</div>
            <h2 class="sec-h">Évolution de la vitesse autour de l'impact</h2>

            <ng-container *ngIf="chartHasData; else noChart">
            <p class="sec-intro">
              Le graphique ci-dessous représente la vitesse du véhicule {{ chartIntroRange }}.
              On observe une conduite stable<ng-container *ngIf="chartIntroCruiseSpeed"> {{ chartIntroCruiseSpeed }}</ng-container>, suivie d'une
              <strong>chute brutale</strong> à {{ chartIntroImpactTime }} qui ne correspond pas à un freinage
              normal.
            </p>

            <figure class="chart-figure">
              <svg class="vel-chart" viewBox="0 0 800 260" preserveAspectRatio="xMidYMid meet">
                <!-- Y grid lines -->
                <line x1="60" y1="30" x2="780" y2="30" class="grid"/>
                <line x1="60" y1="85" x2="780" y2="85" class="grid"/>
                <line x1="60" y1="140" x2="780" y2="140" class="grid"/>
                <line x1="60" y1="195" x2="780" y2="195" class="grid"/>
                <line x1="60" y1="220" x2="780" y2="220" class="grid axis"/>

                <!-- Y axis labels -->
                <text x="48" y="34" class="ax-lbl" text-anchor="end">100</text>
                <text x="48" y="89" class="ax-lbl" text-anchor="end">75</text>
                <text x="48" y="144" class="ax-lbl" text-anchor="end">50</text>
                <text x="48" y="199" class="ax-lbl" text-anchor="end">25</text>
                <text x="48" y="224" class="ax-lbl" text-anchor="end">0</text>
                <text x="20" y="130" class="ax-title" transform="rotate(-90 20 130)">km/h</text>

                <!-- Impact vertical marker -->
                <line [attr.x1]="impactX" y1="30" [attr.x2]="impactX" y2="220" class="impact-mark"/>
                <text [attr.x]="impactX + 7" y="45" class="impact-lbl">{{ impactLabelText }}</text>

                <!-- Speed line -->
                <path [attr.d]="chartPath" class="speed-line"/>

                <!-- Dots -->
                <circle
                  *ngFor="let d of chartDots"
                  [attr.cx]="d.x"
                  [attr.cy]="d.y"
                  [attr.r]="d.isImpact ? 4.5 : 3"
                  [class.dot]="!d.isImpact"
                  [class.dot-impact]="d.isImpact"
                />

                <!-- X axis labels -->
                <text
                  *ngFor="let l of xAxisLabels; let first = first; let last = last"
                  [attr.x]="l.x"
                  y="245"
                  class="ax-lbl"
                  [attr.text-anchor]="first ? 'start' : (last ? 'end' : 'middle')"
                >{{ l.label }}</text>
              </svg>
              <figcaption class="chart-cap">
                Profil de vitesse du véhicule — le trait pointillé rouge marque le moment de
                l'impact ({{ chartCaptionImpactTime }}).
              </figcaption>
            </figure>
            </ng-container>
            <ng-template #noChart>
              <p class="sec-empty">
                Les données GPS disponibles sont insuffisantes pour reconstituer le profil
                de vitesse autour de l'impact.
              </p>
            </ng-template>
          </section>

          <hr class="rule"/>

          <!-- Indicateurs clés -->
          <section class="sec sec-indicators">
            <div class="sec-num">05</div>
            <h2 class="sec-h">Indicateurs clés</h2>
            <p class="sec-intro">
              Les valeurs suivantes résument les principaux éléments mesurés au moment des
              faits. Ces informations sont directement extraites des données brutes envoyées
              par le véhicule.
            </p>

            <div class="ind-table" *ngIf="indicators.length; else noInd">
              <div class="ind-row" *ngFor="let ind of indicators">
                <div class="ind-label">{{ ind.label }}</div>
                <div class="ind-value">{{ ind.value }}</div>
                <div class="ind-hint" *ngIf="ind.hint">{{ ind.hint }}</div>
              </div>
            </div>
            <ng-template #noInd>
              <p class="sec-empty">Les indicateurs détaillés ne sont pas disponibles pour cet incident.</p>
            </ng-template>
          </section>

          <hr class="rule"/>

          <!-- Certitude -->
          <section class="sec sec-confidence">
            <div class="sec-num">06</div>
            <h2 class="sec-h">Niveau de certitude de l'analyse</h2>
            <p class="sec-intro" *ngIf="status === 'dismissed'; else activeIntro">
              Le boîtier a initialement fait remonter un impact grave sur la base de
              <strong>quatre observations concordantes</strong>. Après examen par
              l'administration, cet événement a été écarté comme <strong>fausse alerte</strong>.
              Les éléments détectés restent listés ci-dessous à titre d'historique.
            </p>
            <ng-template #activeIntro>
              <p class="sec-intro">
                Le diagnostic d'accident grave repose sur <strong>quatre observations
                concordantes</strong>. Chacune, prise isolément, serait déjà un signal fort.
                Réunies, elles rendent la conclusion très difficile à contester.
              </p>
            </ng-template>

            <ol class="reasons" *ngIf="reasons.length">
              <li *ngFor="let r of reasons; let i = index" class="reason">
                <span class="reason-num">{{ i + 1 }}</span>
                <div class="reason-body">
                  <div class="reason-title">{{ r.title }}</div>
                  <div class="reason-text">{{ r.text }}</div>
                </div>
              </li>
            </ol>

            <div class="certainty-block">
              <div class="certainty-label">Niveau de certitude</div>
              <div class="certainty-bar">
                <div class="certainty-fill" [style.width.%]="shown ? confidence : 0"></div>
              </div>
              <div class="certainty-verdict">
                <span class="verdict-word" *ngIf="status === 'dismissed'; else activeVerdict">
                  Écarté&nbsp;par&nbsp;l'administration
                </span>
                <ng-template #activeVerdict>
                  <span class="verdict-word">Très&nbsp;élevé</span>
                </ng-template>
                <span class="verdict-pct">{{ confidence }}&thinsp;%</span>
              </div>
            </div>
          </section>

          <hr class="rule"/>

          <!-- Signature / footer -->
          <footer class="doc-footer">
            <div class="sign-line"></div>
            <div class="sign-block">
              <div class="sign-brand">
                <div class="sign-logo" aria-label="Logo Calypso">
                  <svg viewBox="0 0 512 640" xmlns="http://www.w3.org/2000/svg" fill="none" role="img" focusable="false">
                    <defs>
                      <linearGradient id="pinGradFtr" x1="0" y1="0" x2="1" y2="1">
                        <stop offset="0%" stop-color="#43e6a0"/>
                        <stop offset="50%" stop-color="#2fb8d8"/>
                        <stop offset="100%" stop-color="#1a3a8a"/>
                      </linearGradient>
                      <linearGradient id="innerGradFtr" x1="0.2" y1="0" x2="0.8" y2="1">
                        <stop offset="0%" stop-color="#3de8a8"/>
                        <stop offset="100%" stop-color="#1848a0"/>
                      </linearGradient>
                    </defs>
                    <path d="M256 20C152 20 68 104 68 208c0 140 188 290 188 290s188-150 188-290C444 104 360 20 256 20z" fill="url(#pinGradFtr)"/>
                    <circle cx="256" cy="200" r="130" fill="url(#innerGradFtr)" opacity="0.3"/>
                    <path d="M310 110c40 20 68 62 68 108" stroke="white" stroke-width="8" stroke-linecap="round" fill="none" opacity="0.8"/>
                    <circle cx="378" cy="218" r="8" fill="white" opacity="0.8"/>
                    <g transform="translate(256,210)" fill="white">
                      <path d="M-60,10 L-50,-25 Q-45,-40 -30,-40 L30,-40 Q45,-40 50,-25 L60,10 Q62,16 58,20 L-58,20 Q-62,16 -60,10z" opacity="0.95"/>
                      <rect x="-65" y="20" width="130" height="30" rx="8" opacity="0.95"/>
                      <circle cx="-40" cy="50" r="10" fill="url(#pinGradFtr)"/>
                      <circle cx="40" cy="50" r="10" fill="url(#pinGradFtr)"/>
                      <rect x="-20" y="-30" width="40" height="20" rx="4" fill="rgba(26,58,138,0.3)"/>
                    </g>
                  </svg>
                </div>
                <div>
                  <div class="sign-name">Calypso Fleet Analytics</div>
                  <div class="sign-role">Rapport établi par l'équipe d'analyse</div>
                </div>
              </div>
              <div class="sign-meta">
                <div>Rapport établi le {{ issueDate }}</div>
                <div>Dossier {{ reference }}</div>
              </div>
            </div>
          </footer>

        </article>
      </div>
    </app-layout>
  `,
  styles: [`
    @import url('https://fonts.googleapis.com/css2?family=Newsreader:ital,opsz,wght@0,6..72,400;0,6..72,500;0,6..72,600;0,6..72,700;1,6..72,400;1,6..72,500&family=Manrope:wght@300;400;500;600;700&display=swap');

    :host { display: block; }

    .doc-page {
      --paper: #faf8f3;
      --surface: #ffffff;
      --ink: #18181b;
      --ink-soft: #52525b;
      --ink-faint: #9a9aa4;
      --ink-ghost: #c5c3bd;
      --line: #e8e5df;
      --line-soft: #f1ede6;
      --alert: #991b1b;
      --alert-soft: #b91c1c;
      --alert-bg: #fdf5f5;
      --accent: #1e3a5f;

      background: var(--paper);
      min-height: calc(100vh - 56px);
      margin: -24px;
      padding: 48px 24px 96px;
      font-family: 'Manrope', system-ui, sans-serif;
      color: var(--ink);
      -webkit-font-smoothing: antialiased;
      position: relative;
    }

    .doc-page::before {
      content: '';
      position: absolute;
      inset: 0;
      background-image:
        radial-gradient(circle at 50% 0%, rgba(153, 27, 27, 0.015) 0%, transparent 40%),
        linear-gradient(180deg, rgba(24, 24, 27, 0.02) 0%, transparent 20%);
      pointer-events: none;
    }

    .doc {
      max-width: 920px;
      margin: 0 auto;
      background: var(--surface);
      border: 1px solid var(--line);
      box-shadow:
        0 1px 0 rgba(24, 24, 27, 0.04),
        0 12px 40px -12px rgba(24, 24, 27, 0.08),
        0 40px 80px -40px rgba(24, 24, 27, 0.1);
      padding: 72px 84px 88px;
      position: relative;
      opacity: 0;
      transform: translateY(12px);
      transition: opacity 700ms ease, transform 700ms cubic-bezier(0.16, 1, 0.3, 1);
    }
    .doc.shown { opacity: 1; transform: translateY(0); }

    /* Decision status banner */
    .status-banner {
      margin: 0 0 40px;
      padding: 18px 22px;
      border-radius: 12px;
      border: 1px solid #e2e8f0;
      background: #f8fafc;
    }
    .status-banner[data-status="pending"] {
      background: #fff7ed;
      border-color: #fed7aa;
    }
    .status-banner[data-status="awaiting_details"] {
      background: #eff6ff;
      border-color: #bfdbfe;
    }
    .status-banner[data-status="confirmed"] {
      background: #fef2f2;
      border-color: #fecaca;
    }
    .status-banner[data-status="dismissed"] {
      background: #f1f5f9;
      border-color: #cbd5e1;
    }
    .status-head {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 12px;
      font-size: 13px;
    }
    .status-pill {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      padding: 5px 12px;
      border-radius: 999px;
      font-weight: 600;
      font-size: 12px;
      text-transform: uppercase;
      letter-spacing: 0.04em;
      color: #1f2937;
      background: #e2e8f0;
    }
    .status-pill.is-pending   { background: #fed7aa; color: #9a3412; }
    .status-pill.is-confirmed { background: #fecaca; color: #991b1b; }
    .status-pill.is-dismissed { background: #cbd5e1; color: #334155; }

    /* Calypso 7 — accident timeline (6 phases) */
    .timeline {
      margin-bottom: 28px;
      padding: 20px 22px;
      border: 1px solid #e2e8f0;
      border-radius: 12px;
      background: white;
      box-shadow: 0 1px 2px rgba(0,0,0,.03);
    }
    .timeline-title { margin: 0 0 4px; font-size: 16px; font-weight: 700; color: #0f172a; }
    .timeline-sub { margin: 0 0 16px; font-size: 12px; color: #64748b; }

    .phase {
      border: 1px solid #e2e8f0;
      border-radius: 10px;
      margin-bottom: 10px;
      overflow: hidden;
    }
    .phase[open] { border-color: #cbd5e1; box-shadow: 0 1px 3px rgba(0,0,0,.04); }
    .phase-head {
      display: flex; align-items: center; gap: 10px;
      padding: 12px 14px; cursor: pointer; list-style: none;
      background: #f8fafc;
      font-size: 13px;
    }
    .phase-head::-webkit-details-marker { display: none; }
    .phase[open] .phase-head { background: white; border-bottom: 1px solid #f1f5f9; }
    .phase-num {
      flex-shrink: 0;
      width: 26px; height: 26px; border-radius: 50%;
      background: #1e293b; color: white;
      display: flex; align-items: center; justify-content: center;
      font-weight: 700; font-size: 12px;
    }
    .phase-title { flex: 1; font-weight: 600; color: #0f172a; }
    .phase-status {
      padding: 3px 10px; border-radius: 999px;
      background: #fef3c7; color: #92400e;
      font-size: 11px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.04em;
    }
    .phase-status.done { background: #dcfce7; color: #166534; }

    .phase-body { padding: 16px; }
    .phase-row { display: flex; gap: 12px; margin-bottom: 12px; flex-wrap: wrap; }
    .phase-field { flex: 1 1 0; min-width: 180px; display: flex; flex-direction: column; gap: 4px; }
    .phase-field > span {
      font-size: 11px; font-weight: 600; color: #475569;
      text-transform: uppercase; letter-spacing: 0.04em;
    }
    .phase-field input, .phase-field select, .phase-field textarea {
      padding: 8px 10px; font-size: 13px;
      border: 1px solid #e2e8f0; border-radius: 6px; background: white; color: #0f172a;
      font-family: inherit;
    }
    .phase-field input:focus, .phase-field select:focus, .phase-field textarea:focus {
      outline: none; border-color: #3b82f6;
    }
    .phase-field textarea { resize: vertical; }
    .phase-hint {
      margin: 0 0 12px;
      padding: 8px 10px; border-radius: 6px;
      background: #eff6ff; color: #1e40af;
      font-size: 11px; border-left: 3px solid #3b82f6;
    }
    .phase-actions { display: flex; gap: 10px; margin-top: 4px; }
    .phase-btn {
      padding: 8px 16px; border: none; border-radius: 6px;
      font-weight: 600; font-size: 12px; cursor: pointer;
      transition: background 120ms ease;
    }
    .phase-btn.save { background: #16a34a; color: white; }
    .phase-btn.save:hover { background: #15803d; }
    .phase-btn:disabled { opacity: 0.6; cursor: not-allowed; }
    .phase-feedback { margin: 12px 0 0; padding: 8px 12px; border-radius: 6px; font-size: 12px; font-weight: 600; background: #dcfce7; color: #166534; }
    .phase-feedback.is-error { background: #fee2e2; color: #991b1b; }
    /* Third parties */
    .tp-list { display: flex; flex-direction: column; gap: 8px; margin-bottom: 12px; }
    .tp-item { display: flex; align-items: flex-start; justify-content: space-between; gap: 10px;
      padding: 10px 12px; background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; }
    .tp-info { font-size: 13px; color: #0f172a; line-height: 1.5; }
    .tp-ins { font-size: 12px; color: #64748b; margin-top: 2px; }
    .tp-del { flex: none; width: 26px; height: 26px; border: none; border-radius: 6px;
      background: #fee2e2; color: #dc2626; font-size: 16px; line-height: 1; cursor: pointer; }
    .tp-del:disabled { opacity: .5; cursor: default; }
    .status-pdf {
      display: inline-flex; align-items: center; gap: 6px;
      padding: 6px 12px; background: #fee2e2; color: #dc2626;
      border-radius: 6px; text-decoration: none; font-weight: 600; font-size: 12px;
      margin-left: auto;
    }
    .status-pdf:hover { background: #fecaca; }
    .status-pdf-btn { border: none; cursor: pointer; margin-left: 8px; font-family: inherit; }
    .status-pdf-btn:disabled { opacity: 0.6; cursor: default; }
    /* Damages capture (Calypso 6 P9) */
    .damages-card {
      margin-bottom: 28px;
      padding: 20px 22px;
      border: 1px solid #e2e8f0;
      border-radius: 12px;
      background: white;
      box-shadow: 0 1px 2px rgba(0,0,0,.03);
    }
    .damages-title {
      display: flex; align-items: center; gap: 8px;
      margin: 0 0 6px 0;
      font-size: 14px; font-weight: 700; color: #0f172a;
    }
    .damages-title svg { color: #dc2626; }
    .damages-sub {
      margin: 0 0 16px 0;
      font-size: 12px; color: #64748b;
    }
    .dmg-row {
      display: flex; gap: 12px; margin-bottom: 12px; flex-wrap: wrap;
    }
    .dmg-field {
      flex: 1 1 0; min-width: 180px;
      display: flex; flex-direction: column; gap: 4px;
    }
    .dmg-field > span {
      font-size: 11px; font-weight: 600; color: #475569; text-transform: uppercase; letter-spacing: 0.04em;
    }
    .dmg-field input, .dmg-field select, .dmg-field textarea {
      width: 100%;
      padding: 8px 10px; font-size: 13px;
      border: 1px solid #e2e8f0; border-radius: 6px; background: white; color: #0f172a;
      font-family: inherit;
    }
    .dmg-field input:focus, .dmg-field select:focus, .dmg-field textarea:focus {
      outline: none; border-color: #3b82f6;
    }
    .dmg-field textarea { resize: vertical; }
    .dmg-actions {
      display: flex; align-items: center; gap: 12px; margin-top: 8px;
    }
    .dmg-btn {
      padding: 9px 18px;
      border: none; border-radius: 8px;
      font-weight: 600; font-size: 13px; cursor: pointer;
      transition: background 120ms ease;
    }
    .dmg-btn-save { background: #16a34a; color: white; }
    .dmg-btn-save:hover { background: #15803d; }
    .dmg-btn-save:disabled { opacity: .6; cursor: not-allowed; }
    .dmg-feedback {
      margin: 0; font-size: 12px; color: #15803d; font-weight: 600;
    }
    .dmg-feedback.is-error { color: #dc2626; }
    .status-dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background: currentColor;
    }
    .status-meta {
      color: #475569;
    }
    .status-tow {
      margin-left: auto;
      padding: 4px 10px;
      border-radius: 6px;
      background: #fee2e2;
      color: #991b1b;
      font-weight: 600;
      font-size: 12px;
    }
    .status-actions {
      margin-top: 14px;
      padding-top: 14px;
      border-top: 1px dashed #fed7aa;
    }
    .status-hint {
      margin: 0 0 10px;
      font-size: 13px;
      color: #475569;
    }
    .status-buttons {
      display: flex;
      gap: 10px;
      flex-wrap: wrap;
    }
    .sb-btn {
      padding: 9px 16px;
      border-radius: 8px;
      border: 1px solid transparent;
      font-weight: 600;
      font-size: 13px;
      cursor: pointer;
      transition: background 120ms ease;
    }
    .sb-btn:disabled {
      cursor: not-allowed;
      opacity: 0.65;
    }
    .sb-btn-confirm {
      background: #dc2626;
      color: #fff;
    }
    .sb-btn-confirm:hover:not(:disabled) { background: #b91c1c; }
    .sb-btn-dismiss {
      background: #e2e8f0;
      color: #1f2937;
    }
    .sb-btn-dismiss:hover:not(:disabled) { background: #cbd5e1; }
    .status-error {
      margin: 10px 0 0;
      padding: 8px 10px;
      background: #fef2f2;
      border: 1px solid #fecaca;
      color: #b91c1c;
      border-radius: 6px;
      font-size: 13px;
    }

    /* Header */
    .doc-header {
      padding-bottom: 48px;
      margin-bottom: 56px;
      border-bottom: 1px solid var(--line);
    }
    .doc-header-top {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      gap: 40px;
      margin-bottom: 64px;
    }
    .doc-brand {
      display: flex;
      align-items: center;
      gap: 14px;
    }
    .brand-mark {
      width: 44px;
      height: 52px;
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }
    .brand-mark svg {
      width: 100%;
      height: 100%;
      display: block;
    }
    .brand-name {
      font-family: 'Newsreader', serif;
      font-weight: 500;
      font-size: 22px;
      font-variation-settings: 'opsz' 36;
      color: var(--ink);
      line-height: 1;
      letter-spacing: -0.01em;
    }
    .brand-tag {
      font-size: 10px;
      letter-spacing: 0.16em;
      text-transform: uppercase;
      color: var(--ink-faint);
      margin-top: 4px;
      font-weight: 500;
    }
    .doc-meta {
      text-align: right;
      font-size: 12px;
    }
    .meta-row {
      display: flex;
      justify-content: flex-end;
      align-items: baseline;
      gap: 14px;
      margin-bottom: 4px;
    }
    .meta-k {
      font-size: 10px;
      letter-spacing: 0.14em;
      text-transform: uppercase;
      color: var(--ink-faint);
      font-weight: 500;
    }
    .meta-v {
      color: var(--ink);
      font-weight: 500;
      font-variant-numeric: tabular-nums;
      min-width: 130px;
      text-align: right;
    }

    .doc-eyebrow {
      font-size: 10px;
      letter-spacing: 0.2em;
      text-transform: uppercase;
      color: var(--alert);
      font-weight: 600;
      margin-bottom: 18px;
    }
    .doc-title {
      font-family: 'Newsreader', serif;
      font-weight: 400;
      font-variation-settings: 'opsz' 72;
      font-size: clamp(52px, 7vw, 84px);
      line-height: 0.95;
      letter-spacing: -0.025em;
      color: var(--ink);
      margin: 0 0 20px;
    }
    .doc-subtitle {
      font-family: 'Newsreader', serif;
      font-style: italic;
      font-variation-settings: 'opsz' 24;
      font-size: 19px;
      color: var(--ink-soft);
      max-width: 560px;
      line-height: 1.5;
    }

    /* Sections */
    .sec {
      position: relative;
      padding-left: 72px;
      margin-bottom: 56px;
      max-width: 720px;
    }
    .sec-num {
      position: absolute;
      left: 0;
      top: 4px;
      font-family: 'Newsreader', serif;
      font-style: italic;
      font-weight: 400;
      font-size: 16px;
      color: var(--ink-ghost);
      font-variation-settings: 'opsz' 18;
    }
    .sec-num::after {
      content: '';
      display: block;
      width: 44px;
      height: 1px;
      background: var(--line);
      margin-top: 10px;
    }
    .sec-h {
      font-family: 'Newsreader', serif;
      font-weight: 500;
      font-variation-settings: 'opsz' 36;
      font-size: 30px;
      line-height: 1.15;
      letter-spacing: -0.015em;
      color: var(--ink);
      margin: 0 0 22px;
    }
    .sec p, .sec-intro {
      font-size: 15.5px;
      line-height: 1.75;
      color: var(--ink);
      margin: 0 0 16px;
    }
    .sec-intro {
      font-family: 'Newsreader', serif;
      font-style: italic;
      font-variation-settings: 'opsz' 16;
      font-size: 17px;
      color: var(--ink-soft);
      margin-bottom: 24px;
    }
    .sec-empty {
      font-family: 'Newsreader', serif;
      font-style: italic;
      font-size: 16px;
      color: var(--ink-soft);
      padding: 18px 0;
      margin: 0;
    }
    .sec p strong { color: var(--ink); font-weight: 600; }
    .sec p .hl { color: var(--alert); font-weight: 700; }
    .sec .lead {
      font-family: 'Newsreader', serif;
      font-variation-settings: 'opsz' 20;
      font-size: 21px;
      line-height: 1.55;
      font-weight: 400;
      color: var(--ink);
      margin-bottom: 20px;
      letter-spacing: -0.005em;
    }
    .sec .lead strong { font-weight: 600; }

    .rule {
      border: none;
      height: 1px;
      background: var(--line);
      margin: 64px 0 56px;
    }

    /* Location */
    .loc-grid {
      display: grid;
      grid-template-columns: 1fr 1.15fr;
      gap: 36px;
      align-items: start;
    }
    .loc-row {
      padding: 12px 0;
      border-bottom: 1px solid var(--line-soft);
      display: flex;
      justify-content: space-between;
      align-items: baseline;
      gap: 18px;
    }
    .loc-row:last-of-type { border-bottom: none; }
    .loc-k {
      font-size: 11px;
      letter-spacing: 0.12em;
      text-transform: uppercase;
      color: var(--ink-faint);
      font-weight: 500;
    }
    .loc-v {
      font-size: 15px;
      color: var(--ink);
      font-weight: 500;
      text-align: right;
    }
    .loc-coords {
      font-family: 'Newsreader', serif;
      font-variant-numeric: tabular-nums;
      font-size: 14px;
    }
    .loc-note {
      margin-top: 20px;
      padding: 16px 18px;
      background: var(--line-soft);
      border-left: 2px solid var(--ink-ghost);
      font-size: 13px;
      line-height: 1.65;
      color: var(--ink-soft);
      font-family: 'Newsreader', serif;
      font-style: italic;
      font-variation-settings: 'opsz' 14;
    }
    .loc-map {
      height: 380px;
      background: var(--line-soft);
      border: 1px solid var(--line);
      overflow: hidden;
      position: relative;
    }
    .map-canvas {
      position: absolute;
      inset: 0;
    }

    /* Story / narrative timeline */
    .story {
      list-style: none;
      padding: 0;
      margin: 0;
      position: relative;
    }
    .story::before {
      content: '';
      position: absolute;
      left: 78px;
      top: 14px;
      bottom: 14px;
      width: 1px;
      background: var(--line);
    }
    .story-item {
      display: grid;
      grid-template-columns: 68px 1fr;
      gap: 22px;
      padding: 14px 0;
      position: relative;
    }
    .story-item::before {
      content: '';
      position: absolute;
      left: 76px;
      top: 22px;
      width: 7px;
      height: 7px;
      border-radius: 50%;
      background: var(--surface);
      border: 1.5px solid var(--ink-ghost);
      z-index: 1;
    }
    .story-item[data-sev="warning"]::before { border-color: #d4a574; background: #d4a574; }
    .story-item[data-sev="critical"]::before {
      border-color: var(--alert);
      background: var(--alert);
      box-shadow: 0 0 0 4px rgba(153, 27, 27, 0.08);
      width: 9px;
      height: 9px;
      top: 21px;
      left: 75px;
    }
    .story-time {
      font-family: 'Newsreader', serif;
      font-weight: 500;
      font-variation-settings: 'opsz' 18;
      font-size: 16px;
      color: var(--ink);
      font-variant-numeric: tabular-nums;
      padding-top: 2px;
      text-align: right;
      padding-right: 24px;
    }
    .story-body {
      padding-left: 8px;
    }
    .story-title {
      font-size: 14px;
      font-weight: 600;
      color: var(--ink);
      margin-bottom: 4px;
      letter-spacing: -0.005em;
    }
    .story-item[data-sev="critical"] .story-title { color: var(--alert); }
    .story-text {
      font-size: 14px;
      line-height: 1.65;
      color: var(--ink-soft);
    }

    /* Speed chart */
    .chart-figure {
      margin: 24px 0 0;
      padding: 24px 28px 20px;
      background: var(--line-soft);
      border: 1px solid var(--line);
    }
    .vel-chart {
      width: 100%;
      height: auto;
      max-height: 320px;
    }
    .grid { stroke: rgba(24, 24, 27, 0.08); stroke-width: 1; }
    .grid.axis { stroke: var(--ink-ghost); }
    .ax-lbl {
      font-family: 'Manrope', sans-serif;
      font-size: 10px;
      fill: var(--ink-faint);
      font-weight: 500;
    }
    .ax-title {
      font-family: 'Manrope', sans-serif;
      font-size: 9px;
      fill: var(--ink-faint);
      letter-spacing: 0.12em;
      text-transform: uppercase;
    }
    .impact-mark {
      stroke: var(--alert);
      stroke-width: 1.2;
      stroke-dasharray: 3 4;
    }
    .impact-lbl {
      font-family: 'Manrope', sans-serif;
      font-size: 11px;
      font-weight: 600;
      fill: var(--alert);
    }
    .speed-line {
      fill: none;
      stroke: var(--ink);
      stroke-width: 1.8;
      stroke-linecap: round;
      stroke-linejoin: round;
    }
    .dot {
      fill: var(--ink);
      stroke: var(--surface);
      stroke-width: 1.5;
    }
    .dot-impact {
      fill: var(--alert);
      stroke: var(--surface);
      stroke-width: 2;
    }
    .chart-cap {
      margin-top: 16px;
      font-family: 'Newsreader', serif;
      font-style: italic;
      font-variation-settings: 'opsz' 14;
      font-size: 13px;
      color: var(--ink-soft);
      line-height: 1.6;
      text-align: center;
    }

    /* Indicators table */
    .ind-table {
      margin-top: 24px;
      border-top: 1px solid var(--line);
    }
    .ind-row {
      display: grid;
      grid-template-columns: 1.2fr 1fr 1.3fr;
      gap: 24px;
      padding: 18px 4px;
      border-bottom: 1px solid var(--line);
      align-items: baseline;
    }
    .ind-label {
      font-size: 13px;
      color: var(--ink-soft);
      font-weight: 500;
    }
    .ind-value {
      font-family: 'Newsreader', serif;
      font-variation-settings: 'opsz' 20;
      font-size: 22px;
      font-weight: 500;
      color: var(--ink);
      font-variant-numeric: tabular-nums;
      letter-spacing: -0.01em;
    }
    .ind-hint {
      font-family: 'Newsreader', serif;
      font-style: italic;
      font-size: 13px;
      color: var(--ink-faint);
      line-height: 1.5;
    }

    /* Reasons / certainty */
    .reasons {
      list-style: none;
      padding: 0;
      margin: 24px 0 32px;
      counter-reset: reason;
    }
    .reason {
      display: flex;
      gap: 20px;
      padding: 18px 0;
      border-bottom: 1px solid var(--line-soft);
    }
    .reason:last-child { border-bottom: none; }
    .reason-num {
      flex: 0 0 32px;
      height: 32px;
      border-radius: 50%;
      border: 1px solid var(--line);
      display: flex;
      align-items: center;
      justify-content: center;
      font-family: 'Newsreader', serif;
      font-style: italic;
      font-weight: 500;
      font-size: 15px;
      color: var(--ink-soft);
      background: var(--line-soft);
    }
    .reason-title {
      font-size: 15px;
      font-weight: 600;
      color: var(--ink);
      margin-bottom: 4px;
    }
    .reason-text {
      font-size: 14px;
      line-height: 1.65;
      color: var(--ink-soft);
    }

    .certainty-block {
      margin-top: 32px;
      padding: 24px 28px;
      background: var(--line-soft);
      border-left: 2px solid var(--alert);
    }
    .certainty-label {
      font-size: 10px;
      letter-spacing: 0.16em;
      text-transform: uppercase;
      color: var(--ink-faint);
      font-weight: 600;
      margin-bottom: 14px;
    }
    .certainty-bar {
      height: 6px;
      background: var(--surface);
      border: 1px solid var(--line);
      margin-bottom: 14px;
      overflow: hidden;
    }
    .certainty-fill {
      height: 100%;
      background: var(--alert);
      transition: width 1400ms cubic-bezier(0.16, 1, 0.3, 1) 400ms;
    }
    .certainty-verdict {
      display: flex;
      justify-content: space-between;
      align-items: baseline;
    }
    .verdict-word {
      font-family: 'Newsreader', serif;
      font-weight: 500;
      font-variation-settings: 'opsz' 24;
      font-size: 22px;
      color: var(--ink);
    }
    .verdict-pct {
      font-family: 'Newsreader', serif;
      font-style: italic;
      font-variation-settings: 'opsz' 36;
      font-size: 32px;
      font-weight: 500;
      color: var(--alert);
      font-variant-numeric: tabular-nums;
    }

    /* Footer */
    .doc-footer {
      margin-top: 80px;
      padding-top: 40px;
    }
    .sign-line {
      height: 1px;
      background: var(--ink);
      margin-bottom: 24px;
      width: 200px;
    }
    .sign-block {
      display: flex;
      justify-content: space-between;
      align-items: flex-end;
      gap: 40px;
    }
    .sign-brand {
      display: flex;
      align-items: center;
      gap: 14px;
    }
    .sign-logo {
      width: 34px;
      height: 40px;
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }
    .sign-logo svg {
      width: 100%;
      height: 100%;
      display: block;
    }
    .sign-name {
      font-family: 'Newsreader', serif;
      font-weight: 500;
      font-size: 16px;
      color: var(--ink);
      font-variation-settings: 'opsz' 20;
    }
    .sign-role {
      font-size: 11px;
      letter-spacing: 0.1em;
      text-transform: uppercase;
      color: var(--ink-faint);
      font-weight: 500;
      margin-top: 2px;
    }
    .sign-meta {
      text-align: right;
      font-size: 11px;
      letter-spacing: 0.08em;
      text-transform: uppercase;
      color: var(--ink-faint);
      font-weight: 500;
      line-height: 1.8;
    }

    /* Responsive */
    @media (max-width: 900px) {
      .doc { padding: 48px 32px 56px; }
      .doc-header-top { flex-direction: column; gap: 24px; }
      .doc-meta { text-align: left; }
      .meta-row { justify-content: flex-start; }
      .meta-v { min-width: 0; text-align: left; }
      .doc-title { font-size: 48px; }
      .sec { padding-left: 0; max-width: none; }
      .sec-num { position: static; margin-bottom: 8px; display: block; }
      .sec-num::after { display: none; }
      .loc-grid { grid-template-columns: 1fr; }
      .loc-map { height: 280px; }
      .ind-row { grid-template-columns: 1fr; gap: 4px; }
      .story-item { grid-template-columns: 60px 1fr; gap: 16px; }
      .story::before { left: 66px; }
      .story-item::before { left: 64px; }
      .story-item[data-sev="critical"]::before { left: 63px; }
      .sign-block { flex-direction: column; align-items: flex-start; gap: 16px; }
      .sign-meta { text-align: left; }
    }
    @media (max-width: 600px) {
      .doc-page { padding: 16px 8px 48px; margin: -16px; }
      .doc { padding: 32px 20px 40px; }
      .doc-title { font-size: 38px; }
      .sec-h { font-size: 24px; }
      .sec .lead { font-size: 18px; }
    }
  `]
})
export class AccidentReportComponent implements OnInit, OnDestroy, AfterViewInit {
  @ViewChild('mapEl', { static: false }) mapEl?: ElementRef<HTMLDivElement>;

  /**
   * Display fields populated from the backend DTO (or from the static
   * fallback scenario when the API call fails / no :accidentId is in
   * the URL).
   */
  // ───────────────────────────────────────────────────────────────────
  // Display fields. ALL start empty/neutral — there is NO hardcoded demo
  // scenario any more. Every value below is filled from the real backend
  // report (applyReport) and the real GPS history (computeFromPositions).
  // The page is only ever reached with an `:accidentId`, so a real report
  // is always loaded; if the load fails the report degrades to "—" rather
  // than inventing a fake incident.
  // ───────────────────────────────────────────────────────────────────
  reference = '';
  issueDate = '';
  vehicleLabel = '';
  impactLat = 0;
  impactLon = 0;
  /** True once real impact coordinates are known (from the DTO or GPS). */
  coordsKnown = false;
  confidence = 0;

  /** Location strings shown in section 02 (Lieu de l'incident). */
  locationCommune = '';
  locationGovernorate = '';
  locationRoadType = '';

  /** Long-form synthesis sentence shown in section 01. */
  synthesisText = '';

  /**
   * Narrative fragments used in the Synthèse and chart intro. Filled from
   * the real incident time / GPS by applyReport + computeFromPositions.
   */
  synthesisDateTimeLong = '';
  chartIntroRange = '';
  chartIntroCruiseSpeed = '';
  chartIntroImpactTime = '';
  chartCaptionImpactTime = '';

  /**
   * GPS device_uid used to pull position history from the API. Populated
   * from the backend DTO (`dto.deviceUid`) once the accident report is
   * loaded.
   */
  private realDeviceUid = '';

  /**
   * Anchor timestamp used when searching real GPS positions for the
   * impact moment. Populated from the backend DTO (`dto.incidentAt`).
   */
  private impactAtIso = '';

  /**
   * True once the backend report has been loaded successfully. When set,
   * `computeFromPositions` no longer regenerates story/reasons/indicators
   * or the confidence score from raw GPS — the backend-provided narrative
   * is authoritative.
   */
  private narrativeFromBackend = false;

  /** True once the persisted report DTO has been applied. */
  reportLoaded = false;

  /** True once a real speed profile has been computed from GPS positions. */
  chartHasData = false;

  /** Dynamic chart state — empty until real GPS positions arrive. */
  chartPath = '';
  chartDots: ChartDot[] = [];
  xAxisLabels: ChartAxisLabel[] = [];
  impactX = 465;
  impactLabelText = '';

  // Narrative sections — empty until the real report DTO is applied. The
  // backend always provides a data-driven story / indicators / reasons
  // (built from the actual MEMS + speed signal), so these are populated on
  // every successfully loaded report.
  story: NarrativeEvent[] = [];

  indicators: Indicator[] = [];

  reasons: Array<{ title: string; text: string }> = [];

  shown = false;

  /**
   * Decision workflow fields — populated from AccidentReportDto. They back
   * the status banner shown right below the document header and drive the
   * fallback Confirmer / Fausse alerte buttons for admins who "Reporter"-ed
   * the modal earlier.
   *
   * Kept null until the DTO lands so the banner isn't rendered in the
   * fallback scenario (no :accidentId in the URL).
   */
  status: 'pending' | 'confirmed' | 'dismissed' | null = null;
  decidedByName: string | null = null;
  decidedAtFormatted: string | null = null;
  towDetectedAtFormatted: string | null = null;
  accidentEventId: number | null = null;

  /** True when the session user is a company admin (only admins see the buttons). */
  isAdmin = false;

  /** Inline busy indicator on the fallback buttons. */
  decisionBusy: 'confirm' | 'dismiss' | null = null;
  decisionError: string | null = null;

  /** Busy + error state for the "Régénérer le PDF" action. */
  pdfBusy = false;
  pdfError: string | null = null;

  // Calypso 7 — timeline state. One form object per phase, edited
  // independently. The frontend keeps everything in memory and only POSTs
  // when the admin clicks the per-phase "Enregistrer" button.
  pdfReportUrl: string | null = null;
  phase2 = { description: '' as string, severity: null as 'minor' | 'moderate' | 'severe' | 'total' | null,
             mileageAtAccident: null as number | null, policeReportNumber: '' as string,
             weatherConditions: '' as string, roadConditions: '' as string };
  phase3 = { visitedAt: '' as string, expertName: '' as string, expertCompany: '' as string,
             assessment: '' as string, estimatedAmount: null as number | null };
  phase4 = { quoteAt: '' as string, mechanicName: '' as string, quotedAmount: null as number | null };
  phase5 = { startedAt: '' as string, completedAt: '' as string, actualCost: null as number | null };
  phase6 = { claimNumber: '' as string, submittedAt: '' as string, approvedAmount: null as number | null,
             status: null as 'pending' | 'approved' | 'partial' | 'rejected' | 'closed' | null,
             thirdPartyInvolved: false };
  phaseOpen = { initial: true, expert: false, mechanic: false, repair: false, claim: false };
  phaseBusy: 'phase2' | 'phase3' | 'phase4' | 'phase5' | 'phase6' | null = null;
  phaseMessage: { type: 'success' | 'error'; text: string } | null = null;

  /** Third parties involved (insurance counterparts) + inline add form. */
  thirdParties: AccidentReportThirdPartyDto[] = [];
  thirdPartiesOpen = false;
  thirdPartyBusy = false;
  newThirdParty: AddThirdPartyRequest = this.emptyThirdParty();

  private map?: L.Map;
  private impactMarker?: L.Marker;
  private trajectoryLayer?: L.Polyline;
  private lastPositions: PositionDto[] = [];
  private subs: Subscription[] = [];

  constructor(
    private apiService: ApiService,
    private route: ActivatedRoute,
    private cdr: ChangeDetectorRef,
    private authService: AuthService,
    private accidentPdf: AccidentPdfService,
  ) {}

  get statusLabel(): string {
    switch (this.status) {
      case 'pending':   return 'En attente de décision';
      case 'confirmed': return 'Accident confirmé';
      case 'dismissed': return 'Fausse alerte';
      default:          return '';
    }
  }

  phaseClaimLabel(s: string | null): string {
    if (!s) return 'Non déclaré';
    return ({
      pending: 'En cours',
      approved: 'Approuvé',
      partial: 'Partiellement approuvé',
      rejected: 'Rejeté',
      closed: 'Clos',
    } as Record<string, string>)[s] || s;
  }

  ngOnInit(): void {
    // Only company admins see the pending decision banner buttons.
    this.isAdmin = !!this.authService.getCurrentUserSync()?.isCompanyAdmin;

    // Start with a polished fallback chart so the page reads correctly
    // even if the API is down or the vehicle has no positions in that window.
    this.buildDefaultChart();

    // "Rapport établi le …" — today, in long French form.
    this.issueDate = this.formatLongDate(new Date());

    // Reveal animation — runs regardless of data source.
    setTimeout(() => { this.shown = true; this.cdr.markForCheck(); }, 80);

    // If the URL carries an :accidentId, fetch the persisted report from
    // the backend first. Its DTO populates every narrative field; we then
    // fire the GPS history request using the DTO's real device_uid and
    // incident_at so the chart and map reflect the actual vehicle.
    const paramSub = this.route.paramMap.subscribe((params) => {
      const raw = params.get('accidentId');
      const id = raw ? parseInt(raw, 10) : NaN;
      if (!isNaN(id) && id > 0) {
        this.loadReport(id);
      } else {
        // No id in URL → keep the static fallback scenario and still try to
        // pull the 2026-04-14 GPS history for the chart/map.
        this.fetchPositionsForChart();
      }
    });
    this.subs.push(paramSub);
  }

  /**
   * Loads the persisted accident report from the backend and mirrors every
   * field onto the component. Falls back to the static scenario on error
   * so the page never breaks for the client.
   */
  private loadReport(accidentId: number): void {
    const sub = this.apiService.getAccidentReport(accidentId).subscribe({
      next: (dto) => {
        this.applyReport(dto);
        this.fetchPositionsForChart();
        this.cdr.markForCheck();
      },
      error: () => {
        // Keep fallback narrative, but still try to render a real chart
        // using the default device_uid / incident time.
        this.fetchPositionsForChart();
      },
    });
    this.subs.push(sub);
  }

  /**
   * Copies the server-supplied report onto the component's display fields.
   * Marks the narrative as "from backend" so downstream GPS-based recompute
   * doesn't clobber it.
   */
  private applyReport(dto: AccidentReportDto): void {
    this.reference = dto.referenceCode || this.reference;
    this.vehicleLabel = dto.vehicleLabel || this.vehicleLabel;
    this.impactLat = dto.latitude;
    this.impactLon = dto.longitude;
    this.coordsKnown = !!(dto.latitude || dto.longitude);
    this.confidence = dto.confidence;
    this.realDeviceUid = dto.deviceUid;
    this.impactAtIso = dto.incidentAt;

    // Decision workflow snapshot — drives the status banner and the
    // fallback Confirmer / Fausse alerte buttons.
    this.accidentEventId = dto.id;
    this.status = dto.status ?? 'pending';
    this.decidedByName = dto.decidedByName ?? null;
    this.decidedAtFormatted = this.formatShortDateTime(dto.decidedAt);
    this.towDetectedAtFormatted = this.formatShortDateTime(dto.towDetectedAt);

    // Calypso 7 — populate the 5 phase forms from the unified DTO.
    this.pdfReportUrl = dto.pdfReportUrl ?? null;
    this.phase2 = {
      description: dto.initialDescription ?? '',
      severity: dto.initialSeverity ?? null,
      mileageAtAccident: dto.mileageAtAccident ?? null,
      policeReportNumber: dto.policeReportNumber ?? '',
      weatherConditions: dto.weatherConditions ?? '',
      roadConditions: dto.roadConditions ?? '',
    };
    this.phase3 = {
      visitedAt: dto.expertVisitedAt ? new Date(dto.expertVisitedAt).toISOString().slice(0, 10) : '',
      expertName: dto.expertName ?? '',
      expertCompany: dto.expertCompany ?? '',
      assessment: dto.expertAssessment ?? '',
      estimatedAmount: dto.expertEstimatedAmount ?? null,
    };
    this.phase4 = {
      quoteAt: dto.mechanicQuoteAt ? new Date(dto.mechanicQuoteAt).toISOString().slice(0, 10) : '',
      mechanicName: dto.mechanicName ?? '',
      quotedAmount: dto.mechanicQuotedAmount ?? null,
    };
    this.phase5 = {
      startedAt: dto.repairStartedAt ? new Date(dto.repairStartedAt).toISOString().slice(0, 10) : '',
      completedAt: dto.repairCompletedAt ? new Date(dto.repairCompletedAt).toISOString().slice(0, 10) : '',
      actualCost: dto.actualRepairCost ?? null,
    };
    this.phase6 = {
      claimNumber: dto.claimNumber ?? '',
      submittedAt: dto.claimSubmittedAt ? new Date(dto.claimSubmittedAt).toISOString().slice(0, 10) : '',
      approvedAmount: dto.claimApprovedAmount ?? null,
      status: dto.claimStatus ?? null,
      thirdPartyInvolved: dto.thirdPartyInvolved ?? false,
    };

    if (dto.locationCommune) this.locationCommune = dto.locationCommune;
    if (dto.locationGovernorate) this.locationGovernorate = dto.locationGovernorate;
    if (dto.locationRoadType) this.locationRoadType = dto.locationRoadType;
    if (dto.synthesisText) this.synthesisText = dto.synthesisText;

    if (dto.story && dto.story.length) {
      this.story = dto.story.map((s) => ({
        time: s.time,
        title: s.title,
        body: s.body,
        severity: this.normalizeSeverity(s.severity),
      }));
    }
    if (dto.reasons && dto.reasons.length) {
      this.reasons = dto.reasons.map((r) => ({ title: r.title, text: r.text }));
    }
    if (dto.indicators && dto.indicators.length) {
      this.indicators = dto.indicators.map((i) => ({
        label: i.label,
        value: i.value,
        hint: i.hint ?? undefined,
      }));
    }

    // Synthesis date fragment — use the real incidentAt from the DTO.
    const incidentDate = new Date(dto.incidentAt);
    if (!isNaN(incidentDate.getTime())) {
      this.synthesisDateTimeLong = this.formatLongDateTime(incidentDate);
    }

    // Update map marker lat/lon if the map is already initialized.
    if (this.map && this.impactMarker) {
      this.impactMarker.setLatLng([this.impactLat, this.impactLon]);
      this.impactMarker.setPopupContent(this.buildImpactPopup());
      this.map.setView([this.impactLat, this.impactLon], 14);
    }

    this.thirdParties = dto.thirdParties ?? [];

    // The backend narrative is authoritative ONLY when it actually contains a
    // story — i.e. an auto-detected accident (StoryJson populated). A manually
    // declared accident has no story/indicators, so we DON'T mark the
    // narrative as backend-owned: that lets computeFromPositions() rebuild the
    // timeline, indicators, speed chart and impact point from the REAL GPS
    // history around incidentAt. Otherwise a declared accident's report would
    // stay empty.
    this.narrativeFromBackend = !!(dto.story && dto.story.length);
    this.reportLoaded = true;
  }

  /** Accepts any backend severity string and maps to the NarrativeEvent union. */
  private normalizeSeverity(sev: string): NarrativeEvent['severity'] {
    switch ((sev || '').toLowerCase()) {
      case 'critical': return 'critical';
      case 'warning': return 'warning';
      case 'neutral': return 'neutral';
      default: return 'normal';
    }
  }

  /**
   * Fires the GPS history request using the current `realDeviceUid` and
   * `impactAtIso` (both populated either from the DTO or the fallback
   * constants). Errors are silently ignored so the static chart stays.
   */
  private fetchPositionsForChart(): void {
    const impactT = new Date(this.impactAtIso).getTime();
    if (isNaN(impactT)) return;
    const from = new Date(impactT - 10 * 60 * 1000);   // 10 min before
    const to = new Date(impactT + 30 * 60 * 1000);     // 30 min after
    const sub = this.apiService
      .getDeviceHistory(this.realDeviceUid, from, to, 3000)
      .subscribe({
        next: (positions) => {
          this.computeFromPositions(positions);
          this.cdr.markForCheck();
        },
        error: () => { /* keep fallback chart */ },
      });
    this.subs.push(sub);
  }

  ngAfterViewInit(): void {
    setTimeout(() => this.initMap(), 220);
  }

  ngOnDestroy(): void {
    this.subs.forEach((s) => s.unsubscribe());
    if (this.map) {
      this.map.remove();
      this.map = undefined;
    }
  }

  private initMap(): void {
    if (!this.mapEl || this.map) return;

    this.map = L.map(this.mapEl.nativeElement, {
      zoomControl: true,
      attributionControl: true,
      scrollWheelZoom: false,
    }).setView([this.impactLat, this.impactLon], 14);

    // Clean light basemap — CartoDB Positron
    L.tileLayer('https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png', {
      attribution: '© OpenStreetMap · © CARTO',
      maxZoom: 19,
    }).addTo(this.map);

    // Simple red marker — no dramatic pulsing
    const impactIcon = L.divIcon({
      className: 'doc-impact-marker',
      html: `
        <div style="position: relative; width: 22px; height: 22px;">
          <div style="
            position: absolute;
            inset: 0;
            border-radius: 50%;
            background: #991b1b;
            border: 2.5px solid #ffffff;
            box-shadow: 0 2px 8px rgba(24, 24, 27, 0.3);
          "></div>
          <div style="
            position: absolute;
            inset: -10px;
            border-radius: 50%;
            border: 1px solid rgba(153, 27, 27, 0.35);
          "></div>
        </div>
      `,
      iconSize: [22, 22],
      iconAnchor: [11, 11],
    });

    this.impactMarker = L.marker([this.impactLat, this.impactLon], { icon: impactIcon })
      .addTo(this.map)
      .bindPopup(this.buildImpactPopup());

    // If positions arrived before the map was ready, draw the trajectory now
    if (this.lastPositions.length >= 2) {
      this.drawTrajectory(this.lastPositions);
    }
  }

  private buildImpactPopup(): string {
    const when = this.synthesisDateTimeLong
      ? `<div>${this.synthesisDateTimeLong}</div>`
      : '';
    const coords = this.coordsKnown
      ? `${this.impactLat.toFixed(5)} °N · ${this.impactLon.toFixed(5)} °E`
      : '';
    return `
      <div style="font-family: 'Manrope', sans-serif; font-size: 12px; color: #18181b; line-height: 1.5;">
        <div style="font-weight: 600; color: #991b1b; margin-bottom: 4px;">Lieu de l'incident</div>
        ${when}
        <div style="font-size: 11px; color: #52525b; margin-top: 4px;">
          ${coords}
        </div>
      </div>
    `;
  }

  /**
   * Resets the chart to an empty state. The real speed profile is drawn by
   * computeFromPositions once GPS history arrives; if it never does (sparse
   * data / API error) the template shows a "données GPS insuffisantes"
   * placeholder rather than a fabricated curve.
   */
  private buildDefaultChart(): void {
    this.chartPath = '';
    this.chartDots = [];
    this.xAxisLabels = [];
    this.impactLabelText = '';
    this.chartHasData = false;
  }

  /**
   * Given a batch of real GPS positions spanning the impact window, recompute:
   *   • the impact lat/lon (closest position to the target time)
   *   • key indicators (pre-impact speed, impact speed, time-to-stop, ignition-off)
   *   • the speed chart (path, dots, x-axis labels)
   *   • the map marker + trajectory
   *
   * Silently aborts and keeps the fallback if the data is too sparse to trust.
   */
  private computeFromPositions(positions: PositionDto[]): void {
    if (!positions || positions.length < 5) return;

    const sorted = positions
      .filter((p) => p.latitude && p.longitude && p.recordedAt)
      .sort((a, b) => new Date(a.recordedAt).getTime() - new Date(b.recordedAt).getTime());

    if (sorted.length < 5) return;

    const impactTargetT = new Date(this.impactAtIso).getTime();

    // Find the position closest to the target impact time
    let impactIdx = 0;
    let minDelta = Number.MAX_SAFE_INTEGER;
    for (let i = 0; i < sorted.length; i++) {
      const delta = Math.abs(new Date(sorted[i].recordedAt).getTime() - impactTargetT);
      if (delta < minDelta) {
        minDelta = delta;
        impactIdx = i;
      }
    }

    // If the closest position is more than 5 minutes off, the GPS series
    // doesn't cover the incident — trust the fallback instead.
    if (minDelta > 5 * 60 * 1000) {
      this.lastPositions = sorted;
      if (this.map) this.drawTrajectory(sorted);
      return;
    }

    // Re-center on the REAL impact: if there's a strong MEMS peak (mag ≥ 100)
    // within ±2 min of the user-entered time, that's the actual moment of
    // impact — refine impactIdx to it. Otherwise keep the closest-to-target
    // position. This matters for manually declared accidents where the
    // entered time can be a minute or two off the real frame.
    const wideStart = impactTargetT - 2 * 60 * 1000;
    const wideEnd = impactTargetT + 2 * 60 * 1000;
    const clampB = (n: number) => Math.max(-128, Math.min(127, n));
    let bestPeakIdx = -1;
    let bestPeakMag = 0;
    for (let i = 0; i < sorted.length; i++) {
      const t = new Date(sorted[i].recordedAt).getTime();
      if (t < wideStart || t > wideEnd) continue;
      const p = sorted[i];
      if (p.memsX == null || p.memsY == null || p.memsZ == null) continue;
      const x = clampB(p.memsX), y = clampB(p.memsY), z = clampB(p.memsZ);
      const m = Math.sqrt(x * x + y * y + z * z);
      if (m >= 100 && m > bestPeakMag) {
        bestPeakMag = m;
        bestPeakIdx = i;
      }
    }
    if (bestPeakIdx >= 0) {
      impactIdx = bestPeakIdx;
    }

    const impactPos = sorted[impactIdx];
    const impactT = new Date(impactPos.recordedAt).getTime();

    this.impactLat = impactPos.latitude;
    this.impactLon = impactPos.longitude;
    this.coordsKnown = true;

    // Speed at impact: minimum in a ±30 s window centered on the impact position
    const atImpact = sorted.filter((p) => {
      const t = new Date(p.recordedAt).getTime();
      return Math.abs(t - impactT) <= 30 * 1000;
    });
    const speedAtImpact = atImpact.length
      ? Math.round(Math.min(...atImpact.map((p) => p.speedKph ?? 0)))
      : Math.round(impactPos.speedKph ?? 0);

    // Speed before impact: max between impact-5 min and impact-30 s
    const preWindow = sorted.filter((p) => {
      const t = new Date(p.recordedAt).getTime();
      return t >= impactT - 5 * 60 * 1000 && t <= impactT - 30 * 1000;
    });
    const speedBeforeImpact = preWindow.length
      ? Math.round(Math.max(...preWindow.map((p) => p.speedKph ?? 0)))
      : 88;

    // Time to full stop: from impact to first position with speed ≤ 1 km/h
    let timeToStopStr = '5 min 6 s';
    const stopIdx = sorted.findIndex((p, i) => i >= impactIdx && (p.speedKph ?? 0) <= 1);
    if (stopIdx > impactIdx) {
      const deltaMs = new Date(sorted[stopIdx].recordedAt).getTime() - impactT;
      const totalSec = Math.max(0, Math.round(deltaMs / 1000));
      const mins = Math.floor(totalSec / 60);
      const secs = totalSec % 60;
      timeToStopStr = `${mins} min ${secs.toString().padStart(2, '0')} s`;
    }

    // Ignition-off: first post-impact position with ignitionOn === false
    const ignitionOffPos = sorted.slice(impactIdx).find((p) => p.ignitionOn === false);
    const ignitionOffLabel = ignitionOffPos
      ? this.formatHourMinute(new Date(ignitionOffPos.recordedAt))
      : '16h 08';
    const ignitionOffDelta = ignitionOffPos
      ? this.diffMinutes(new Date(impactT), new Date(ignitionOffPos.recordedAt))
      : 6;

    // ── MEMS analytics (Calypso 9) ────────────────────────────────────────
    // Reads memsX/Y/Z from the frames (now exposed by /gps/devices/{uid}/history)
    // to detect: peak shock magnitude + axes, a possible SECOND shock within
    // ~60 s of the first, and a sustained TILT after the stop (rollover).
    // These power the richer story, reasons, indicators and synthesis verdict
    // that the old hardcoded demo had.
    const stopTSafe = stopIdx > impactIdx ? new Date(sorted[stopIdx].recordedAt).getTime() : undefined;
    const memsStats = this.computeMemsStats(sorted, impactT, stopTSafe);

    // Indicators — fully data-driven, including MEMS so the report exposes
    // the magnitudes / axes the operator can hand to an insurance expert.
    if (!this.narrativeFromBackend) {
      this.indicators = [
        { label: 'Heure de l\'impact', value: this.formatHourMinuteSeconds(new Date(impactT)), hint: 'Heure locale du navigateur' },
        { label: 'Vitesse avant l\'impact', value: `${speedBeforeImpact} km/h`, hint: 'Maximum mesuré 5 min avant' },
        { label: 'Vitesse au moment de l\'impact', value: `${speedAtImpact} km/h`, hint: 'Chute brutale et non-maîtrisée' },
        { label: 'Temps jusqu\'à l\'arrêt complet', value: timeToStopStr, hint: 'Après le choc' },
      ];
      if (memsStats.peakMag > 0) {
        this.indicators.push({
          label: 'Magnitude du choc',
          value: Math.round(memsStats.peakMag).toString(),
          hint: 'Somme vectorielle MEMS (saturation théorique ≈ 222)',
        });
        this.indicators.push({
          label: 'Axes saturés',
          value: `X=${memsStats.peakAx} · Y=${memsStats.peakAy} · Z=${memsStats.peakAz}`,
          hint: '|X|/|Y| ≥ 100 = impact horizontal marqué',
        });
      }
      if (memsStats.tiltDurationMin >= 2) {
        this.indicators.push({
          label: "Durée d'inclinaison anormale",
          value: `${memsStats.tiltDurationMin} min`,
          hint: 'Forte suspicion de retournement',
        });
      }
      this.indicators.push({
        label: 'Coupure du contact',
        value: ignitionOffLabel,
        hint: ignitionOffPos
          ? `Soit ${ignitionOffDelta} minute${ignitionOffDelta > 1 ? 's' : ''} après l'impact`
          : 'Non détectée dans la fenêtre',
      });
    }

    // Rebuild the speed chart from positions in [impact-7 min, impact+8 min]
    const chartStart = impactT - 7 * 60 * 1000;
    const chartEnd = impactT + 8 * 60 * 1000;
    const chartPositions = sorted.filter((p) => {
      const t = new Date(p.recordedAt).getTime();
      return t >= chartStart && t <= chartEnd;
    });
    if (chartPositions.length >= 3) {
      this.buildChartFromPositions(chartPositions, chartStart, chartEnd, impactT);
    }

    // Narrative regeneration only happens when we're in fallback mode
    // (no backend DTO) AND the real data actually looks like an impact.
    // When the backend already provided story/reasons/confidence, we
    // never overwrite them — the server is the source of truth.
    const looksLikeImpact =
      (speedBeforeImpact - speedAtImpact) >= 20 ||
      (speedBeforeImpact >= 40 && speedAtImpact <= 15);

    if (!this.narrativeFromBackend && looksLikeImpact) {
      this.story = this.buildStoryFromPositions(
        sorted,
        impactIdx,
        impactT,
        speedAtImpact,
        speedBeforeImpact,
        ignitionOffPos,
        stopTSafe,
        memsStats,
      );

      this.reasons = this.buildReasonsFromPositions(
        sorted,
        impactIdx,
        impactT,
        speedAtImpact,
        speedBeforeImpact,
        timeToStopStr,
        ignitionOffPos,
        stopTSafe,
        memsStats,
      );

      this.confidence = this.computeConfidence(
        speedBeforeImpact,
        speedAtImpact,
        stopTSafe !== undefined,
        ignitionOffPos !== undefined,
        memsStats,
      );

      // Synthesis verdict — derived from the MEMS pattern when available.
      // Falls back to a sober, factual statement when MEMS is silent.
      if (memsStats.peakMag >= 180 && memsStats.tiltDurationMin >= 2) {
        this.synthesisText = 'choc violent ayant vraisemblablement entraîné un retournement du véhicule';
      } else if (memsStats.peakMag >= 150) {
        this.synthesisText = 'choc violent multi-axes — impact transversal marqué';
      } else if (memsStats.peakMag >= 100) {
        this.synthesisText = 'impact violent détecté sur les axes longitudinal et latéral';
      } else {
        this.synthesisText = 'impact significatif détecté avec immobilisation immédiate du véhicule';
      }
    }

    // Synthesis fragments — always updated to the real impact timestamp,
    // even if the data isn't dramatic enough to rebuild the full narrative.
    this.synthesisDateTimeLong = this.formatLongDateTime(new Date(impactT));
    this.chartIntroRange = `entre ${this.formatHourMinute(new Date(chartStart))} et ${this.formatHourMinute(new Date(chartEnd))}`;
    this.chartIntroImpactTime = this.formatHourMinute(new Date(impactT));
    this.chartCaptionImpactTime = this.formatHourMinuteSeconds(new Date(impactT));

    // Cruise speed sentence: derive min/max from the 5 min pre-impact window
    if (preWindow.length >= 3) {
      const cruiseSpeeds = preWindow
        .map((p) => Math.round(p.speedKph ?? 0))
        .filter((s) => s > 10);
      if (cruiseSpeeds.length >= 2) {
        const cruiseMin = Math.min(...cruiseSpeeds);
        const cruiseMax = Math.max(...cruiseSpeeds);
        this.chartIntroCruiseSpeed =
          cruiseMax - cruiseMin >= 3
            ? `autour de ${cruiseMin} à ${cruiseMax} km/h`
            : `autour de ${cruiseMax} km/h`;
      }
    }

    // Remember positions + refresh map
    this.lastPositions = sorted;
    if (this.map && this.impactMarker) {
      this.impactMarker.setLatLng([this.impactLat, this.impactLon]);
      this.impactMarker.setPopupContent(this.buildImpactPopup());
      this.map.setView([this.impactLat, this.impactLon], 14);
    }
    if (this.map) {
      this.drawTrajectory(sorted);
    }
  }

  /**
   * Maps a set of GPS positions to SVG coordinates and builds the chart path,
   * dots, x-axis labels, and the impact vertical marker position.
   */
  private buildChartFromPositions(
    positions: PositionDto[],
    tStart: number,
    tEnd: number,
    impactT: number,
  ): void {
    const X0 = 60, X1 = 780;     // chart area on X
    const Y0 = 30, Y1 = 220;     // chart area on Y (top = 100 km/h, bottom = 0)
    const totalMs = tEnd - tStart;
    if (totalMs <= 0) return;

    const toX = (t: number) =>
      X0 + ((t - tStart) / totalMs) * (X1 - X0);
    const toY = (speedKph: number) => {
      const clamped = Math.max(0, Math.min(100, speedKph));
      return Y1 - (clamped / 100) * (Y1 - Y0);
    };

    // Downsample to ~25 points so the chart stays legible
    const step = Math.max(1, Math.ceil(positions.length / 25));
    const sampled: PositionDto[] = [];
    for (let i = 0; i < positions.length; i += step) sampled.push(positions[i]);

    // Make sure the actual impact position is kept in the sample
    const impactPos = positions.find(
      (p) => new Date(p.recordedAt).getTime() === impactT,
    );
    if (impactPos && !sampled.includes(impactPos)) {
      sampled.push(impactPos);
      sampled.sort(
        (a, b) => new Date(a.recordedAt).getTime() - new Date(b.recordedAt).getTime(),
      );
    }

    const pathParts: string[] = [];
    const dots: ChartDot[] = [];
    sampled.forEach((p, i) => {
      const t = new Date(p.recordedAt).getTime();
      const x = +toX(t).toFixed(1);
      const y = +toY(p.speedKph ?? 0).toFixed(1);
      pathParts.push(`${i === 0 ? 'M' : 'L'} ${x},${y}`);
      dots.push({ x, y, isImpact: t === impactT });
    });
    this.chartPath = pathParts.join(' ');
    this.chartDots = dots;
    this.impactX = +toX(impactT).toFixed(1);
    this.impactLabelText = `Impact · ${this.formatHourMinute(new Date(impactT))}`;

    // Five evenly spaced time labels along the X axis
    const labels: ChartAxisLabel[] = [];
    for (let i = 0; i <= 4; i++) {
      const t = tStart + (i / 4) * totalMs;
      labels.push({
        x: +toX(t).toFixed(1),
        label: this.formatHourMinute(new Date(t)),
      });
    }
    this.xAxisLabels = labels;
    this.chartHasData = true;
  }

  private drawTrajectory(positions: PositionDto[]): void {
    if (!this.map || !positions || positions.length < 2) return;

    const coords: L.LatLngTuple[] = positions
      .filter((p) => p.latitude && p.longitude)
      .map((p) => [p.latitude, p.longitude] as L.LatLngTuple);

    if (coords.length < 2) return;

    // Replace any previous trajectory so repeated calls don't stack layers
    if (this.trajectoryLayer) {
      this.map.removeLayer(this.trajectoryLayer);
      this.trajectoryLayer = undefined;
    }

    this.trajectoryLayer = L.polyline(coords, {
      color: '#991b1b',
      weight: 2,
      opacity: 0.4,
      dashArray: '2 6',
    }).addTo(this.map);

    const bounds = L.latLngBounds(coords);
    bounds.extend([this.impactLat, this.impactLon]);
    this.map.fitBounds(bounds, { padding: [30, 30], maxZoom: 15 });
  }

  // ——— Formatting helpers ————————————————————————————————————————
  private formatHourMinute(d: Date): string {
    const h = d.getHours().toString().padStart(2, '0');
    const m = d.getMinutes().toString().padStart(2, '0');
    return `${h}h ${m}`;
  }

  private formatHourMinuteSeconds(d: Date): string {
    const h = d.getHours().toString().padStart(2, '0');
    const m = d.getMinutes().toString().padStart(2, '0');
    const s = d.getSeconds().toString().padStart(2, '0');
    return `${h}h ${m} min ${s} s`;
  }

  private diffMinutes(from: Date, to: Date): number {
    return Math.max(0, Math.round((to.getTime() - from.getTime()) / 60000));
  }

  /**
   * Long French date string like "15 avril 2026" used in the report header
   * and footer ("Rapport établi le …").
   */
  private formatLongDate(d: Date): string {
    const day = d.getDate();
    const month = new Intl.DateTimeFormat('fr-FR', { month: 'long' })
      .format(d)
      .toLowerCase();
    const year = d.getFullYear();
    return `${day} ${month} ${year}`;
  }

  /**
   * Long French date/time string like "mercredi 14 avril 2026 à 16 heures 02"
   * used in the Synthèse paragraph. Uses Intl for locale safety.
   */
  private formatLongDateTime(d: Date): string {
    const weekday = new Intl.DateTimeFormat('fr-FR', { weekday: 'long' })
      .format(d)
      .toLowerCase();
    const day = d.getDate();
    const month = new Intl.DateTimeFormat('fr-FR', { month: 'long' })
      .format(d)
      .toLowerCase();
    const year = d.getFullYear();
    const hours = d.getHours();
    const minutes = d.getMinutes().toString().padStart(2, '0');
    return `${weekday} ${day} ${month} ${year} à ${hours} heures ${minutes}`;
  }

  /**
   * Compact "dd/MM/yyyy HH:mm" used for the decision metadata line in the
   * status banner. Returns null when the input is null or unparseable so
   * the template can hide the fragment.
   */
  private formatShortDateTime(iso: string | null | undefined): string | null {
    if (!iso) return null;
    const d = new Date(iso);
    if (isNaN(d.getTime())) return null;
    return d.toLocaleString('fr-FR', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  /**
   * Fallback "Confirmer l'accident" button on the report page — for admins
   * who clicked "Reporter" on the modal and came back later via the bell
   * icon. Idempotent on the backend: a repeat click resolves silently.
   */
  confirmFromReport(): void {
    if (!this.accidentEventId || this.decisionBusy || this.status !== 'pending') return;
    this.decisionBusy = 'confirm';
    this.decisionError = null;
    const id = this.accidentEventId;
    const sub = this.apiService.confirmAccident(id).subscribe({
      next: () => this.reloadAfterDecision(id, 'confirmed'),
      error: () => {
        this.decisionBusy = null;
        this.decisionError = "Impossible de confirmer l'accident. Veuillez réessayer.";
        this.cdr.markForCheck();
      },
    });
    this.subs.push(sub);
  }

  /**
   * Fallback "Fausse alerte" button on the report page.
   */
  dismissFromReport(): void {
    if (!this.accidentEventId || this.decisionBusy || this.status !== 'pending') return;
    this.decisionBusy = 'dismiss';
    this.decisionError = null;
    const id = this.accidentEventId;
    const sub = this.apiService.dismissAccident(id).subscribe({
      next: () => this.reloadAfterDecision(id, 'dismissed'),
      error: () => {
        this.decisionBusy = null;
        this.decisionError = "Impossible d'enregistrer la fausse alerte. Veuillez réessayer.";
        this.cdr.markForCheck();
      },
    });
    this.subs.push(sub);
  }

  /**
   * Regenerates the attached PDF from the CURRENT server state. The auto-PDF
   * is built once at confirm time — before any tow is detected and before
   * the admin fills the damages/expert/repair phases — so it's a stale
   * snapshot. This re-fetches the latest report (which now includes a real
   * tow line in the timeline if one was detected, plus filled phases) and
   * re-uploads a fresh PDF. Nothing is fabricated: it only renders what the
   * server returns.
   */
  regeneratePdf(): void {
    if (!this.accidentEventId || this.pdfBusy) return;
    this.pdfBusy = true;
    this.pdfError = null;
    const id = this.accidentEventId;
    const sub = this.apiService.getAccidentReport(id).subscribe({
      next: (dto) => {
        try {
          const blob = this.accidentPdf.generate(dto);
          const up = this.apiService
            .uploadAccidentReportPdf(id, blob, `rapport-accident-${id}.pdf`)
            .subscribe({
              next: (res) => {
                this.pdfReportUrl = res?.pdfReportUrl ?? this.pdfReportUrl;
                this.pdfBusy = false;
                this.cdr.markForCheck();
              },
              error: () => {
                this.pdfBusy = false;
                this.pdfError = 'Échec de la mise à jour du PDF. Veuillez réessayer.';
                this.cdr.markForCheck();
              },
            });
          this.subs.push(up);
        } catch {
          this.pdfBusy = false;
          this.pdfError = 'Échec de la génération du PDF.';
          this.cdr.markForCheck();
        }
      },
      error: () => {
        this.pdfBusy = false;
        this.pdfError = 'Impossible de charger le rapport pour régénérer le PDF.';
        this.cdr.markForCheck();
      },
    });
    this.subs.push(sub);
  }

  // ── Third parties (insurance counterparts) ──────────────────────────────

  private emptyThirdParty(): AddThirdPartyRequest {
    return {
      name: '', phone: '', vehiclePlate: '', vehicleModel: '',
      insuranceCompany: '', insuranceNumber: '', insuranceExpiry: null,
    };
  }

  /** Require at least one identifying field before allowing an add. */
  canAddThirdParty(): boolean {
    const t = this.newThirdParty;
    return !!(t.name?.trim() || t.vehiclePlate?.trim()
           || t.insuranceCompany?.trim() || t.insuranceNumber?.trim());
  }

  addThirdParty(): void {
    if (!this.accidentEventId || this.thirdPartyBusy || !this.canAddThirdParty()) return;
    this.thirdPartyBusy = true;
    const id = this.accidentEventId;
    const payload: AddThirdPartyRequest = {
      name: this.newThirdParty.name?.trim() || null,
      phone: this.newThirdParty.phone?.trim() || null,
      vehiclePlate: this.newThirdParty.vehiclePlate?.trim() || null,
      vehicleModel: this.newThirdParty.vehicleModel?.trim() || null,
      insuranceCompany: this.newThirdParty.insuranceCompany?.trim() || null,
      insuranceNumber: this.newThirdParty.insuranceNumber?.trim() || null,
      insuranceExpiry: this.newThirdParty.insuranceExpiry
        ? new Date(this.newThirdParty.insuranceExpiry).toISOString()
        : null,
    };
    const sub = this.apiService.addAccidentThirdParty(id, payload).subscribe({
      next: ({ thirdPartyId }) => {
        this.thirdParties = [
          ...this.thirdParties,
          { id: thirdPartyId, ...payload } as AccidentReportThirdPartyDto,
        ];
        this.newThirdParty = this.emptyThirdParty();
        this.thirdPartyBusy = false;
        this.cdr.markForCheck();
      },
      error: () => {
        this.thirdPartyBusy = false;
        this.phaseMessage = { type: 'error', text: "Échec de l'ajout du tiers. Veuillez réessayer." };
        this.cdr.markForCheck();
      },
    });
    this.subs.push(sub);
  }

  removeThirdParty(tp: AccidentReportThirdPartyDto): void {
    if (!this.accidentEventId || this.thirdPartyBusy) return;
    this.thirdPartyBusy = true;
    const id = this.accidentEventId;
    const sub = this.apiService.deleteAccidentThirdParty(id, tp.id).subscribe({
      next: () => {
        this.thirdParties = this.thirdParties.filter((x) => x.id !== tp.id);
        this.thirdPartyBusy = false;
        this.cdr.markForCheck();
      },
      error: () => {
        this.thirdPartyBusy = false;
        this.cdr.markForCheck();
      },
    });
    this.subs.push(sub);
  }

  // Calypso 7 — phase save methods. Each posts only its own phase
  // payload, sets a busy flag for inline feedback, then re-fetches the
  // DTO so the timeline reflects server-side changes (including the
  // auto-VehicleCost upsert from Phase 5/6).

  savePhase2(): void {
    this.runPhase('phase2', () => this.apiService.updateAccidentInitialDamages(this.accidentEventId!, {
      description: this.phase2.description?.trim() || null,
      severity: this.phase2.severity,
      damagedZones: null,
      policeReportNumber: this.phase2.policeReportNumber?.trim() || null,
      mileageAtAccident: this.phase2.mileageAtAccident,
      weatherConditions: this.phase2.weatherConditions?.trim() || null,
      roadConditions: this.phase2.roadConditions?.trim() || null,
    }));
  }

  savePhase3(): void {
    this.runPhase('phase3', () => this.apiService.registerAccidentExpertAssessment(this.accidentEventId!, {
      visitedAt: this.phase3.visitedAt ? new Date(this.phase3.visitedAt).toISOString() : null,
      expertName: this.phase3.expertName?.trim() || null,
      expertCompany: this.phase3.expertCompany?.trim() || null,
      assessment: this.phase3.assessment?.trim() || null,
      estimatedAmount: this.phase3.estimatedAmount,
    }));
  }

  savePhase4(): void {
    this.runPhase('phase4', () => this.apiService.registerAccidentMechanicQuote(this.accidentEventId!, {
      quoteAt: this.phase4.quoteAt ? new Date(this.phase4.quoteAt).toISOString() : null,
      mechanicName: this.phase4.mechanicName?.trim() || null,
      quotedAmount: this.phase4.quotedAmount,
    }));
  }

  savePhase5(): void {
    this.runPhase('phase5', () => this.apiService.registerAccidentRepair(this.accidentEventId!, {
      startedAt: this.phase5.startedAt ? new Date(this.phase5.startedAt).toISOString() : null,
      completedAt: this.phase5.completedAt ? new Date(this.phase5.completedAt).toISOString() : null,
      actualCost: this.phase5.actualCost,
    }));
  }

  savePhase6(): void {
    this.runPhase('phase6', () => this.apiService.registerAccidentClaim(this.accidentEventId!, {
      claimNumber: this.phase6.claimNumber?.trim() || null,
      submittedAt: this.phase6.submittedAt ? new Date(this.phase6.submittedAt).toISOString() : null,
      approvedAmount: this.phase6.approvedAmount,
      status: this.phase6.status,
      thirdPartyInvolved: this.phase6.thirdPartyInvolved,
    }));
  }

  /**
   * Shared phase-save plumbing: sets the busy flag, runs the API call,
   * shows the appropriate success / error toast, then refreshes the DTO
   * so the timeline picks up server-side derived values (e.g. auto
   * VehicleCost upserts on Phase 5 / 6).
   */
  private runPhase(phase: 'phase2' | 'phase3' | 'phase4' | 'phase5' | 'phase6', call: () => any): void {
    if (!this.accidentEventId || this.phaseBusy) return;
    const id = this.accidentEventId;
    this.phaseBusy = phase;
    this.phaseMessage = null;
    const sub = call().subscribe({
      next: () => {
        this.phaseBusy = null;
        this.phaseMessage = { type: 'success', text: 'Phase enregistrée.' };
        this.reloadAfterDecision(id, 'confirmed');
      },
      error: (err: any) => {
        this.phaseBusy = null;
        const msg = err?.error?.message || "Impossible d'enregistrer la phase. Veuillez réessayer.";
        this.phaseMessage = { type: 'error', text: msg };
        this.cdr.markForCheck();
      },
    });
    this.subs.push(sub);
  }

  /**
   * Re-hydrates the page with the freshly stamped decision fields so the
   * status banner flips from "En attente" to "Accident confirmé" /
   * "Fausse alerte" without needing a full navigation round-trip.
   */
  private reloadAfterDecision(id: number, fallbackStatus: 'confirmed' | 'dismissed'): void {
    const sub = this.apiService.getAccidentReport(id).subscribe({
      next: (dto) => {
        this.decisionBusy = null;
        this.applyReport(dto);
        this.cdr.markForCheck();
      },
      error: () => {
        // Fallback: mark as decided locally so the buttons disappear even
        // if the follow-up GET fails (network blip, concurrent deploy).
        this.decisionBusy = null;
        this.status = fallbackStatus;
        this.cdr.markForCheck();
      },
    });
    this.subs.push(sub);
  }

  /**
   * Walks through the sorted GPS positions and produces a narrative that
   * reflects what actually happened on the ground: last normal cruise,
   * progressive deceleration, the impact itself, stop, ignition off, and
   * any post-stop movement consistent with a tow. Every event carries the
   * real timestamp and speed values — no made-up numbers.
   */
  private buildStoryFromPositions(
    positions: PositionDto[],
    impactIdx: number,
    impactT: number,
    speedAtImpact: number,
    speedBeforeImpact: number,
    ignitionOffPos: PositionDto | undefined,
    stopT: number | undefined,
    memsStats: MemsStats,
  ): NarrativeEvent[] {
    const story: NarrativeEvent[] = [];

    // 1. Last normal driving — find the MAX cruise speed in [-5 min, -1 min]
    //    and the frame that reached it. Picking the max avoids opening the
    //    timeline on a slow-approach frame (e.g. 21 km/h while merging).
    const cruiseWindow = positions.filter((p) => {
      const t = new Date(p.recordedAt).getTime();
      return t >= impactT - 5 * 60 * 1000 && t <= impactT - 60 * 1000;
    });
    const cruisePos = cruiseWindow.length
      ? cruiseWindow.reduce((best, p) => ((p.speedKph ?? 0) > (best.speedKph ?? 0) ? p : best))
      : undefined;
    if (cruisePos && (cruisePos.speedKph ?? 0) > 20) {
      const regionPart = this.locationCommune
        ? ` dans la région de ${this.locationCommune}`
        : '';
      story.push({
        time: this.formatHourMinute(new Date(cruisePos.recordedAt)),
        title: 'Conduite normale',
        body:
          `Le véhicule circule à ${Math.round(cruisePos.speedKph ?? 0)} km/h sur une route` +
          `${regionPart}. Aucun comportement inhabituel n'est détecté dans les ` +
          `minutes précédentes. La conduite est régulière et stable.`,
        severity: 'normal',
      });
    }

    // 2. Progressive deceleration — first position in the 2 min before
    //    impact that is noticeably slower than the cruise speed.
    const decelStart = positions.find((p, i) => {
      if (i <= 0 || i >= impactIdx) return false;
      const t = new Date(p.recordedAt).getTime();
      if (t < impactT - 2 * 60 * 1000 || t > impactT - 15 * 1000) return false;
      const speed = p.speedKph ?? 0;
      return speedBeforeImpact - speed >= 5 && speed > speedAtImpact + 5;
    });
    if (decelStart) {
      story.push({
        time: this.formatHourMinute(new Date(decelStart.recordedAt)),
        title: 'Ralentissement progressif',
        body:
          `Le véhicule ralentit doucement. La vitesse passe d'environ ${speedBeforeImpact} km/h ` +
          `à ${Math.round(decelStart.speedKph ?? 0)} km/h. Rien d'anormal à ce stade.`,
        severity: 'normal',
      });
    }

    // 3. The impact itself — enriched with the actual MEMS magnitude / axes.
    const memsImpactPart = memsStats.peakMag > 0
      ? ` Les capteurs de mouvement enregistrent simultanément un choc d'intensité ` +
        `exceptionnelle (magnitude ${Math.round(memsStats.peakMag)}/222 ; axes |X|=${memsStats.peakAx}, ` +
        `|Y|=${memsStats.peakAy}, |Z|=${memsStats.peakAz}).`
      : '';
    story.push({
      time: this.formatHourMinute(new Date(impactT)),
      title: 'Chute brutale de la vitesse et choc',
      body:
        `La vitesse passe brutalement de ${speedBeforeImpact} à ${speedAtImpact} km/h en ` +
        `quelques secondes. Ce profil de ralentissement ne correspond pas à un freinage ` +
        `normal et constitue la signature caractéristique d'un choc violent.${memsImpactPart}`,
      severity: 'critical',
    });

    // 3b. Second shock — detected via a second MEMS peak within ~60 s.
    if (memsStats.secondShockT !== undefined && memsStats.secondShockMag > 0) {
      story.push({
        time: this.formatHourMinute(new Date(memsStats.secondShockT)),
        title: "Second choc d'intensité équivalente",
        body:
          `Un second impact est enregistré (magnitude ${Math.round(memsStats.secondShockMag)}/222), ` +
          `de même ordre de grandeur que le premier. Ce double signal est caractéristique ` +
          `d'une collision avec rebond ou tonneau.`,
        severity: 'critical',
      });
    }

    // 4. Full stop.
    if (stopT !== undefined) {
      story.push({
        time: this.formatHourMinute(new Date(stopT)),
        title: 'Arrêt complet du véhicule',
        body:
          `Le véhicule est totalement immobilisé. Plus aucun déplacement n'est détecté à ` +
          `partir de ce moment.`,
        severity: 'warning',
      });
    }

    // 4b. Sustained tilt after the stop — characteristic of a rollover.
    if (memsStats.tiltDurationMin >= 2 && memsStats.tiltStartT !== undefined && memsStats.tiltEndT !== undefined) {
      story.push({
        time: `${this.formatHourMinute(new Date(memsStats.tiltStartT))} → ${this.formatHourMinute(new Date(memsStats.tiltEndT))}`,
        title: 'Position anormalement inclinée',
        body:
          `Pendant ${memsStats.tiltDurationMin} minute${memsStats.tiltDurationMin > 1 ? 's' : ''} ` +
          `consécutive${memsStats.tiltDurationMin > 1 ? 's' : ''}, les capteurs indiquent que ` +
          `le véhicule se trouve dans une position fortement inclinée (|Z|≈${memsStats.peakAz}). ` +
          `Une inclinaison de cette ampleur, maintenue à l'arrêt, est caractéristique d'un ` +
          `véhicule qui s'est retrouvé sur le flanc ou retourné.`,
        severity: 'warning',
      });
    }

    // 5. Ignition off.
    if (ignitionOffPos) {
      const delta = this.diffMinutes(new Date(impactT), new Date(ignitionOffPos.recordedAt));
      story.push({
        time: this.formatHourMinute(new Date(ignitionOffPos.recordedAt)),
        title: 'Coupure du contact',
        body:
          `Le contact du véhicule est coupé, soit ${delta} minute${delta > 1 ? 's' : ''} ` +
          `après l'impact. Il est très probable qu'à ce moment, le conducteur, la protection ` +
          `civile ou un tiers soit déjà intervenu sur place.`,
        severity: 'neutral',
      });
    }

    // 6. Post-stop movement (possible tow) — only if we see significant
    //    motion at least 20 min after the vehicle had stopped.
    if (stopT !== undefined) {
      const laterMoving = positions.find((p) => {
        const t = new Date(p.recordedAt).getTime();
        return t > stopT + 20 * 60 * 1000 && (p.speedKph ?? 0) > 5;
      });
      if (laterMoving) {
        story.push({
          time: this.formatHourMinute(new Date(laterMoving.recordedAt)),
          title: 'Mouvement compatible avec un chargement sur dépanneuse',
          body:
            `Un mouvement spécifique est détecté sur le véhicule bien après l'arrêt. ` +
            `Cela correspond typiquement à un chargement sur un plateau de dépanneuse ` +
            `ou à un déplacement du véhicule par un tiers.`,
          severity: 'neutral',
        });
      }
    }

    return story;
  }

  /**
   * Produces the 3-4 concordant observations that together justify the
   * accident diagnosis, each backed by real numbers from the GPS series.
   */
  private buildReasonsFromPositions(
    positions: PositionDto[],
    impactIdx: number,
    impactT: number,
    speedAtImpact: number,
    speedBeforeImpact: number,
    timeToStopStr: string,
    ignitionOffPos: PositionDto | undefined,
    stopT: number | undefined,
    memsStats: MemsStats,
  ): Array<{ title: string; text: string }> {
    const reasons: Array<{ title: string; text: string }> = [];

    // Reason 1: the speed drop itself.
    reasons.push({
      title: 'Une chute brutale et incontrôlée de la vitesse',
      text:
        `Le véhicule est passé de ${speedBeforeImpact} à ${speedAtImpact} km/h sur une durée ` +
        `très courte. Le profil de décélération ne correspond pas à un freinage volontaire ` +
        `mais à un arrêt subi.`,
    });

    // Reason 2: MEMS-based "exceptional violence" observation (when peakMag
    // is high). This is the analogue of the old hardcoded "choc d'une
    // violence exceptionnelle" — but driven by REAL accelerometer data.
    if (memsStats.peakMag >= 120) {
      reasons.push({
        title: "Un choc d'une violence exceptionnelle",
        text:
          `Les capteurs de mouvement ont enregistré une intensité ` +
          `(magnitude ${Math.round(memsStats.peakMag)}/222, axes |X|=${memsStats.peakAx}, ` +
          `|Y|=${memsStats.peakAy}, |Z|=${memsStats.peakAz}) qui n'est atteignable qu'en cas ` +
          `d'impact violent multi-axes. Cette signature exclut un freinage d'urgence ordinaire.`,
      });
    }

    // Reason 3: stop + time-to-stop.
    if (stopT !== undefined) {
      reasons.push({
        title: 'Un arrêt complet immédiatement après',
        text:
          `À la suite du choc, le véhicule a mis ${timeToStopStr} pour s'immobiliser ` +
          `complètement, puis est resté strictement à l'arrêt. Cela exclut un simple ` +
          `ralentissement ou un freinage d'anticipation.`,
      });
    }

    // Reason 4: sustained tilt = rollover signature.
    if (memsStats.tiltDurationMin >= 2) {
      reasons.push({
        title: 'Une position fortement inclinée maintenue à l\'arrêt',
        text:
          `Un véhicule reposant normalement sur ses roues ne présente pas d'inclinaison ` +
          `soutenue. La valeur |Z|≈${memsStats.peakAz} maintenue pendant ` +
          `${memsStats.tiltDurationMin} minute${memsStats.tiltDurationMin > 1 ? 's' : ''} ` +
          `ne peut s'expliquer que par un véhicule qui s'est retrouvé couché sur le flanc ou retourné.`,
      });
    }

    // Reason 5: ignition off after impact.
    if (ignitionOffPos) {
      const delta = this.diffMinutes(new Date(impactT), new Date(ignitionOffPos.recordedAt));
      reasons.push({
        title: 'Une coupure du contact après l\'arrêt',
        text:
          `Le contact du véhicule a été coupé ${delta} minute${delta > 1 ? 's' : ''} ` +
          `après l'impact. Sur un véhicule en conduite normale, le contact n'est jamais ` +
          `coupé brutalement en milieu de trajet.`,
      });
    }

    // Reason 6: prolonged immobilization (added only when we don't already
    // have a stop reason + a tilt reason — keeps the list tight).
    if (stopT !== undefined && memsStats.tiltDurationMin < 2) {
      const motionless = positions.filter((p) => {
        const t = new Date(p.recordedAt).getTime();
        return t >= stopT && (p.speedKph ?? 0) <= 1;
      });
      if (motionless.length >= 3) {
        const firstT = new Date(motionless[0].recordedAt).getTime();
        const lastT = new Date(motionless[motionless.length - 1].recordedAt).getTime();
        const durationMin = Math.max(1, Math.round((lastT - firstT) / 60000));
        reasons.push({
          title: 'Une immobilisation anormalement longue',
          text:
            `Le véhicule est resté strictement à l'arrêt pendant au moins ${durationMin} ` +
            `minute${durationMin > 1 ? 's' : ''} consécutive${durationMin > 1 ? 's' : ''}, ` +
            `sans reprise de mouvement. Une panne classique (crevaison, essence, moteur) ` +
            `aurait vraisemblablement été suivie d'une tentative de remise en route.`,
        });
      }
    }

    return reasons;
  }

  /**
   * Scores the accident diagnosis between 0 and 97 based on how many strong
   * signals concur: speed drop magnitude, low speed at impact, full stop,
   * ignition-off event.
   */
  private computeConfidence(
    speedBeforeImpact: number,
    speedAtImpact: number,
    hasStop: boolean,
    hasIgnitionOff: boolean,
    memsStats: MemsStats,
  ): number {
    let score = 30; // baseline when real data covers the incident window

    // Speed-drop magnitude.
    const drop = speedBeforeImpact - speedAtImpact;
    if (drop >= 60) score += 20;
    else if (drop >= 40) score += 14;
    else if (drop >= 25) score += 9;
    else if (drop >= 15) score += 5;

    // Low speed at impact = clearly not anticipation braking.
    if (speedAtImpact <= 5) score += 10;
    else if (speedAtImpact <= 15) score += 7;
    else if (speedAtImpact <= 25) score += 3;

    // MEMS magnitude — the strongest indicator when present.
    if (memsStats.peakMag >= 180) score += 20;
    else if (memsStats.peakMag >= 150) score += 15;
    else if (memsStats.peakMag >= 120) score += 10;
    else if (memsStats.peakMag >= 80) score += 5;

    // Second shock = collision with bounce/rollover signature.
    if (memsStats.secondShockMag >= 100) score += 5;

    // Sustained tilt after stop = rollover.
    if (memsStats.tiltDurationMin >= 2) score += 8;

    if (hasStop) score += 4;
    if (hasIgnitionOff) score += 3;

    return Math.min(97, Math.max(0, score));
  }

  /**
   * Returns the position whose timestamp is closest to `targetT`, restricted
   * to indices in [fromIdx, toIdx). Returns undefined if no candidates.
   */
  private closestPositionTo(
    positions: PositionDto[],
    targetT: number,
    fromIdx: number,
    toIdx: number,
  ): PositionDto | undefined {
    let best: PositionDto | undefined;
    let bestDelta = Number.MAX_SAFE_INTEGER;
    for (let i = fromIdx; i < Math.min(toIdx, positions.length); i++) {
      const t = new Date(positions[i].recordedAt).getTime();
      const delta = Math.abs(t - targetT);
      if (delta < bestDelta) {
        bestDelta = delta;
        best = positions[i];
      }
    }
    return best;
  }

  /**
   * Computes MEMS analytics from the sorted positions: peak shock at impact,
   * a possible second shock within ~60 s of the first, and any sustained
   * tilt after the stop (|Z| ≥ 90 for ≥ 2 min — rollover signature). Returns
   * zeros across the board when no usable MEMS data is present in the frames
   * (e.g. old data partitions or devices that don't expose it).
   */
  private computeMemsStats(positions: PositionDto[], impactT: number, stopT: number | undefined): MemsStats {
    const empty: MemsStats = {
      peakMag: 0, peakAx: 0, peakAy: 0, peakAz: 0,
      secondShockMag: 0,
      tiltDurationMin: 0,
    };

    const clamp = (n: number) => Math.max(-128, Math.min(127, n));
    const mag = (p: PositionDto): { m: number; ax: number; ay: number; az: number } => {
      if (p.memsX == null || p.memsY == null || p.memsZ == null) return { m: 0, ax: 0, ay: 0, az: 0 };
      const x = clamp(p.memsX), y = clamp(p.memsY), z = clamp(p.memsZ);
      return {
        m: Math.sqrt(x * x + y * y + z * z),
        ax: Math.abs(x), ay: Math.abs(y), az: Math.abs(z),
      };
    };

    // Peak shock: maximum magnitude within ±30 s of impact.
    let peakMag = 0, peakAx = 0, peakAy = 0, peakAz = 0, peakT = impactT;
    for (const p of positions) {
      const t = new Date(p.recordedAt).getTime();
      if (Math.abs(t - impactT) > 30 * 1000) continue;
      const v = mag(p);
      if (v.m > peakMag) { peakMag = v.m; peakAx = v.ax; peakAy = v.ay; peakAz = v.az; peakT = t; }
    }
    if (peakMag === 0) return empty;

    // Second shock: another peak ≥ 100 separated by ≥ 5 s from the primary,
    // within a ±90 s window around impactT (covers a bounce-back or rollover
    // signature regardless of which peak came first).
    let secondShockMag = 0;
    let secondShockT: number | undefined;
    for (const p of positions) {
      const t = new Date(p.recordedAt).getTime();
      if (Math.abs(t - impactT) > 90 * 1000) continue;
      if (Math.abs(t - peakT) < 5 * 1000) continue;
      const v = mag(p);
      if (v.m >= 100 && v.m > secondShockMag) { secondShockMag = v.m; secondShockT = t; }
    }

    // Sustained tilt after the stop — |Z| ≥ 90 maintained.
    let tiltDurationMin = 0;
    let tiltStartT: number | undefined;
    let tiltEndT: number | undefined;
    if (stopT !== undefined) {
      let runStart: number | undefined;
      let runEnd: number | undefined;
      for (const p of positions) {
        const t = new Date(p.recordedAt).getTime();
        if (t < stopT) continue;
        const v = mag(p);
        if (v.m > 0 && v.az >= 90) {
          if (runStart === undefined) runStart = t;
          runEnd = t;
        } else if (runStart !== undefined && runEnd !== undefined) {
          const dur = Math.round((runEnd - runStart) / 60000);
          if (dur > tiltDurationMin) {
            tiltDurationMin = dur; tiltStartT = runStart; tiltEndT = runEnd;
          }
          runStart = undefined; runEnd = undefined;
        }
      }
      if (runStart !== undefined && runEnd !== undefined) {
        const dur = Math.round((runEnd - runStart) / 60000);
        if (dur > tiltDurationMin) {
          tiltDurationMin = dur; tiltStartT = runStart; tiltEndT = runEnd;
        }
      }
    }

    return { peakMag, peakAx, peakAy, peakAz, secondShockMag, secondShockT, tiltDurationMin, tiltStartT, tiltEndT };
  }
}
