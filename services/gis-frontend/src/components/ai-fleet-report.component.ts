import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { ApiService } from '../services/api.service';
import { marked } from 'marked';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { USER_PREF_PIPES } from '../pipes/user-preference-pipes';

@Component({
  selector: 'app-ai-fleet-report',
  standalone: true,
  imports: [CommonModule, FormsModule, ...USER_PREF_PIPES],
  template: `
    <div class="report-page">
      <!-- Header -->
      <div class="report-header">
        <div class="header-left">
          <button class="btn-back" (click)="router.navigate(['/dashboard'])">&#x2190;</button>
          <div>
            <h1>Rapport IA - Analyse de Flotte</h1>
            <p class="subtitle" *ngIf="reportData">{{ reportData.companyInfo.name }} &middot; {{ reportData.companyInfo.vehicleCount }} vehicules &middot; Genere le {{ reportData.generatedAt | date:'dd/MM/yyyy HH:mm' }}</p>
          </div>
        </div>
        <div class="header-actions">
          <select class="period-select" [(ngModel)]="selectedPeriod">
            <option value="week">7 derniers jours</option>
            <option value="month">30 derniers jours</option>
            <option value="quarter">90 derniers jours</option>
            <option value="year">365 derniers jours</option>
          </select>
          <button class="btn-generate" (click)="generateReport()" [disabled]="loading">
            <span *ngIf="!loading">&#x1F916; Generer le rapport</span>
            <span *ngIf="loading" class="loading-text">
              <span class="spinner"></span> Analyse en cours...
            </span>
          </button>
        </div>
      </div>

      <!-- Loading state -->
      <div class="loading-overlay" *ngIf="loading">
        <div class="loading-card">
          <div class="pulse-icon">&#x1F916;</div>
          <h3>L'IA analyse votre flotte...</h3>
          <p>Collecte des donnees vehicules, maintenance, carburant, alertes et couts.</p>
          <div class="progress-bar"><div class="progress-fill"></div></div>
        </div>
      </div>

      <!-- Error -->
      <div class="error-banner" *ngIf="error">
        <span>&#x26A0; {{ error }}</span>
        <button (click)="error = ''">&#x2715;</button>
      </div>

      <!-- Report Content -->
      <div class="report-content" *ngIf="reportData && !loading">

        <!-- KPI Summary Cards -->
        <div class="kpi-row">
          <div class="kpi-card">
            <div class="kpi-icon" style="background:#6366f1">&#x1F697;</div>
            <div class="kpi-info">
              <span class="kpi-value">{{ reportData.fleetSummary.totalVehicles }}</span>
              <span class="kpi-label">Vehicules</span>
            </div>
          </div>
          <div class="kpi-card">
            <div class="kpi-icon" style="background:#10b981">&#x1F6E3;</div>
            <div class="kpi-info">
              <span class="kpi-value">{{ reportData.fleetSummary.totalDistance | number:'1.0-0' }}</span>
              <span class="kpi-label">km parcourus</span>
            </div>
          </div>
          <div class="kpi-card">
            <div class="kpi-icon" style="background:#f59e0b">&#x1F4B0;</div>
            <div class="kpi-info">
              <span class="kpi-value">{{ reportData.fleetSummary.totalCosts | appCurrency:0 }}</span>
              <span class="kpi-label">Couts totaux</span>
            </div>
          </div>
          <div class="kpi-card">
            <div class="kpi-icon" style="background:#22c55e">&#x2764;</div>
            <div class="kpi-info">
              <span class="kpi-value">{{ reportData.fleetSummary.avgHealthScore }}/100</span>
              <span class="kpi-label">Sante moyenne</span>
            </div>
          </div>
          <div class="kpi-card">
            <div class="kpi-icon" style="background:#ef4444">&#x26A0;</div>
            <div class="kpi-info">
              <span class="kpi-value">{{ reportData.fleetSummary.totalAlerts }}</span>
              <span class="kpi-label">Alertes</span>
            </div>
          </div>
          <div class="kpi-card">
            <div class="kpi-icon" style="background:#8b5cf6">&#x1F527;</div>
            <div class="kpi-info">
              <span class="kpi-value">{{ reportData.fleetSummary.overdueSchedules }}</span>
              <span class="kpi-label">Entretiens en retard</span>
            </div>
          </div>
        </div>

        <!-- Charts Row -->
        <div class="charts-row">
          <!-- Health Distribution -->
          <div class="chart-card">
            <h3>Distribution sante</h3>
            <div class="health-bars">
              <div class="hbar" *ngFor="let item of healthItems">
                <span class="hbar-label">{{ item.label }}</span>
                <div class="hbar-track">
                  <div class="hbar-fill" [style.width.%]="item.pct" [style.background]="item.color"></div>
                </div>
                <span class="hbar-val">{{ item.count }}</span>
              </div>
            </div>
          </div>

          <!-- Cost Breakdown -->
          <div class="chart-card">
            <h3>Repartition des couts</h3>
            <div class="cost-grid">
              <div class="cost-item" *ngFor="let c of costItems">
                <div class="cost-dot" [style.background]="c.color"></div>
                <span class="cost-label">{{ c.label }}</span>
                <span class="cost-val">{{ c.value | appCurrency:0 }}</span>
                <span class="cost-pct">{{ c.pct | number:'1.0-0' }}%</span>
              </div>
            </div>
            <div class="cost-bar">
              <div *ngFor="let c of costItems" class="cost-seg" [style.width.%]="c.pct" [style.background]="c.color" [title]="c.label + ': ' + c.pct + '%'"></div>
            </div>
          </div>

          <!-- Top Fuel Consumers -->
          <div class="chart-card">
            <h3>Top consommation (L/100km)</h3>
            <div class="bar-list">
              <div class="blist-row" *ngFor="let item of reportData.charts.topFuelConsumers; let i = index">
                <span class="blist-rank">#{{ i+1 }}</span>
                <span class="blist-name">{{ item.name }}</span>
                <div class="blist-track">
                  <div class="blist-fill" [style.width.%]="getBarPct(item.value, maxFuel)" style="background:#f59e0b"></div>
                </div>
                <span class="blist-val">{{ item.value }}</span>
              </div>
              <div *ngIf="!reportData.charts.topFuelConsumers?.length" class="no-data">Aucune donnee FMS disponible</div>
            </div>
          </div>
        </div>

        <!-- Charts Row 2 -->
        <div class="charts-row">
          <!-- Mileage -->
          <div class="chart-card wide">
            <h3>Kilometrage par vehicule (km)</h3>
            <div class="bar-list">
              <div class="blist-row" *ngFor="let item of reportData.charts.mileageByVehicle; let i = index">
                <span class="blist-rank">#{{ i+1 }}</span>
                <span class="blist-name">{{ item.name }}</span>
                <div class="blist-track">
                  <div class="blist-fill" [style.width.%]="getBarPct(item.value, maxMileage)" style="background:#6366f1"></div>
                </div>
                <span class="blist-val">{{ item.value | number:'1.0-0' }}</span>
              </div>
            </div>
          </div>

          <!-- Driving Scores -->
          <div class="chart-card">
            <h3>Scores de conduite</h3>
            <div class="bar-list">
              <div class="blist-row" *ngFor="let item of reportData.charts.drivingScores; let i = index">
                <span class="blist-rank">#{{ i+1 }}</span>
                <span class="blist-name">{{ item.name }}</span>
                <div class="blist-track">
                  <div class="blist-fill" [style.width.%]="item.value" [style.background]="getScoreColor(item.value)"></div>
                </div>
                <span class="blist-val">{{ item.value }}</span>
              </div>
            </div>
          </div>
        </div>

        <!-- Vehicle Details Table -->
        <div class="table-card">
          <h3>Detail par vehicule</h3>
          <div class="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Vehicule</th>
                  <th>Marque/Modele</th>
                  <th>Km</th>
                  <th>Sante</th>
                  <th>Conduite</th>
                  <th>Conso</th>
                  <th>Couts</th>
                  <th>Alertes</th>
                  <th>Statut</th>
                </tr>
              </thead>
              <tbody>
                <tr *ngFor="let v of reportData.vehicleDetails" [class.warning-row]="v.healthScore < 50" [class.critical-row]="v.healthScore < 30">
                  <td><strong>{{ v.plate || v.name }}</strong></td>
                  <td>{{ v.brand }} {{ v.model }} {{ v.year || '' }}</td>
                  <td>{{ v.mileage | number:'1.0-0' }}</td>
                  <td>
                    <span class="badge" [style.background]="getHealthBg(v.healthLevel)">{{ v.healthScore }}/100</span>
                  </td>
                  <td>
                    <span class="badge" [style.background]="getScoreColor(v.drivingScore)">{{ v.drivingScore }}</span>
                  </td>
                  <td>{{ v.fuelConsumption > 0 ? (v.fuelConsumption + ' L') : '-' }}</td>
                  <td>{{ v.totalCosts | appCurrency:0 }}</td>
                  <td>{{ v.alertCount }}</td>
                  <td>
                    <span class="status-dot" [class.active]="v.status === 'available' || v.status === 'in_use'" [class.maint]="v.status === 'maintenance'"></span>
                    {{ v.status }}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <!-- AI Analysis -->
        <div class="analysis-card">
          <div class="analysis-header">
            <h3>&#x1F916; Analyse IA - Expert Flotte & TCO</h3>
            <span class="tokens-badge">{{ reportData.tokensUsed }} tokens</span>
          </div>
          <div class="analysis-body markdown-content" [innerHTML]="renderedAnalysis"></div>
        </div>

        <!-- Interactive Q&A -->
        <div class="qa-card">
          <h3>&#x1F4AC; Questions de suivi</h3>
          <p class="qa-hint">Posez une question sur le rapport. L'IA connait vos vehicules, votre type d'entreprise, et les problemes typiques de chaque marque/modele.</p>
          <div class="qa-suggestions">
            <button *ngFor="let s of suggestions" class="suggestion-btn" (click)="askQuestion(s)" [disabled]="askLoading">{{ s }}</button>
          </div>
          <div class="qa-messages" *ngIf="qaMessages.length > 0">
            <div *ngFor="let msg of qaMessages" class="qa-msg" [class.user]="msg.role === 'user'" [class.ai]="msg.role === 'ai'">
              <div class="qa-msg-header">{{ msg.role === 'user' ? 'Vous' : 'IA Expert' }}</div>
              <div class="qa-msg-body" [innerHTML]="msg.html"></div>
            </div>
          </div>
          <div class="qa-input-row">
            <input type="text" [(ngModel)]="questionInput" placeholder="Ex: Quel vehicule devrait etre remplace en priorite ?" (keydown.enter)="askQuestion(questionInput)" [disabled]="askLoading" />
            <button class="btn-ask" (click)="askQuestion(questionInput)" [disabled]="askLoading || !questionInput.trim()">
              <span *ngIf="!askLoading">Envoyer</span>
              <span *ngIf="askLoading" class="spinner-sm"></span>
            </button>
          </div>
        </div>

      </div>

      <!-- Empty state -->
      <div class="empty-state" *ngIf="!reportData && !loading">
        <div class="empty-icon">&#x1F916;</div>
        <h2>Rapport IA de Flotte</h2>
        <p>Generez un rapport complet analyse par intelligence artificielle.<br/>
        L'IA analysera tous vos vehicules, couts, maintenance et conduite pour vous donner des recommandations TCO personnalisees.</p>
        <button class="btn-generate large" (click)="generateReport()">&#x1F916; Generer le rapport</button>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; height: 100%; }
    .report-page { padding: 20px 24px; max-width: 1400px; margin: 0 auto; font-family: 'Inter', -apple-system, sans-serif; color: #1e293b; }

    /* Header */
    .report-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 24px; flex-wrap: wrap; gap: 12px; }
    .header-left { display: flex; align-items: center; gap: 12px; }
    .btn-back { background: none; border: 1px solid #e2e8f0; border-radius: 8px; padding: 6px 10px; cursor: pointer; font-size: 16px; color: #64748b; }
    .btn-back:hover { background: #f1f5f9; }
    h1 { font-size: 22px; font-weight: 700; margin: 0; }
    .subtitle { font-size: 12px; color: #94a3b8; margin: 2px 0 0; }
    .header-actions { display: flex; gap: 10px; align-items: center; }
    .period-select { padding: 8px 12px; border: 1px solid #e2e8f0; border-radius: 8px; font-size: 13px; background: #fff; cursor: pointer; }
    .btn-generate { padding: 10px 20px; background: linear-gradient(135deg, #6366f1, #8b5cf6); color: #fff; border: none; border-radius: 10px; font-weight: 600; font-size: 14px; cursor: pointer; transition: all 0.2s; }
    .btn-generate:hover { transform: translateY(-1px); box-shadow: 0 4px 12px rgba(99,102,241,0.4); }
    .btn-generate:disabled { opacity: 0.7; cursor: not-allowed; transform: none; }
    .btn-generate.large { padding: 14px 32px; font-size: 16px; }
    .loading-text { display: flex; align-items: center; gap: 8px; }

    /* Spinner */
    .spinner { width: 16px; height: 16px; border: 2px solid rgba(255,255,255,0.3); border-top-color: #fff; border-radius: 50%; animation: spin 0.8s linear infinite; display: inline-block; }
    .spinner-sm { width: 14px; height: 14px; border: 2px solid rgba(99,102,241,0.3); border-top-color: #6366f1; border-radius: 50%; animation: spin 0.8s linear infinite; display: inline-block; }
    @keyframes spin { to { transform: rotate(360deg); } }

    /* Loading overlay */
    .loading-overlay { display: flex; justify-content: center; padding: 80px 20px; }
    .loading-card { text-align: center; background: #fff; border-radius: 16px; padding: 40px; box-shadow: 0 4px 20px rgba(0,0,0,0.08); max-width: 400px; }
    .pulse-icon { font-size: 48px; animation: pulse 1.5s ease-in-out infinite; }
    @keyframes pulse { 0%, 100% { transform: scale(1); } 50% { transform: scale(1.1); } }
    .loading-card h3 { margin: 16px 0 8px; font-size: 18px; }
    .loading-card p { color: #64748b; font-size: 13px; margin: 0 0 20px; }
    .progress-bar { height: 4px; background: #e2e8f0; border-radius: 2px; overflow: hidden; }
    .progress-fill { height: 100%; width: 30%; background: linear-gradient(90deg, #6366f1, #8b5cf6); border-radius: 2px; animation: progress 2s ease-in-out infinite; }
    @keyframes progress { 0% { width: 10%; margin-left: 0; } 50% { width: 60%; margin-left: 20%; } 100% { width: 10%; margin-left: 90%; } }

    /* Error */
    .error-banner { display: flex; justify-content: space-between; align-items: center; background: #fef2f2; border: 1px solid #fecaca; color: #dc2626; padding: 12px 16px; border-radius: 10px; margin-bottom: 16px; font-size: 13px; }
    .error-banner button { background: none; border: none; color: #dc2626; cursor: pointer; font-size: 16px; }

    /* KPI Row */
    .kpi-row { display: grid; grid-template-columns: repeat(6, 1fr); gap: 12px; margin-bottom: 20px; }
    .kpi-card { background: #fff; border-radius: 12px; padding: 16px; display: flex; align-items: center; gap: 12px; box-shadow: 0 1px 3px rgba(0,0,0,0.06); border: 1px solid #f1f5f9; }
    .kpi-icon { width: 40px; height: 40px; border-radius: 10px; display: flex; align-items: center; justify-content: center; font-size: 18px; color: #fff; flex-shrink: 0; }
    .kpi-info { display: flex; flex-direction: column; }
    .kpi-value { font-size: 18px; font-weight: 700; line-height: 1.2; }
    .kpi-label { font-size: 11px; color: #94a3b8; }

    /* Charts */
    .charts-row { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 16px; margin-bottom: 16px; }
    .chart-card { background: #fff; border-radius: 12px; padding: 18px; box-shadow: 0 1px 3px rgba(0,0,0,0.06); border: 1px solid #f1f5f9; }
    .chart-card.wide { grid-column: span 2; }
    .chart-card h3 { font-size: 14px; font-weight: 600; margin: 0 0 14px; color: #334155; }

    /* Health bars */
    .health-bars { display: flex; flex-direction: column; gap: 8px; }
    .hbar { display: flex; align-items: center; gap: 8px; }
    .hbar-label { font-size: 11px; width: 60px; text-align: right; color: #64748b; }
    .hbar-track { flex: 1; height: 20px; background: #f1f5f9; border-radius: 4px; overflow: hidden; }
    .hbar-fill { height: 100%; border-radius: 4px; transition: width 0.5s; min-width: 2px; }
    .hbar-val { font-size: 12px; font-weight: 600; width: 24px; }

    /* Cost grid */
    .cost-grid { display: flex; flex-direction: column; gap: 6px; margin-bottom: 12px; }
    .cost-item { display: flex; align-items: center; gap: 8px; font-size: 12px; }
    .cost-dot { width: 10px; height: 10px; border-radius: 50%; flex-shrink: 0; }
    .cost-label { flex: 1; color: #64748b; }
    .cost-val { font-weight: 600; }
    .cost-pct { color: #94a3b8; font-size: 11px; width: 36px; text-align: right; }
    .cost-bar { display: flex; height: 10px; border-radius: 5px; overflow: hidden; }
    .cost-seg { transition: width 0.5s; }

    /* Bar list (fuel, mileage, scores) */
    .bar-list { display: flex; flex-direction: column; gap: 6px; }
    .blist-row { display: flex; align-items: center; gap: 6px; font-size: 12px; }
    .blist-rank { color: #94a3b8; width: 22px; text-align: center; font-size: 11px; }
    .blist-name { width: 80px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-weight: 500; }
    .blist-track { flex: 1; height: 16px; background: #f1f5f9; border-radius: 3px; overflow: hidden; }
    .blist-fill { height: 100%; border-radius: 3px; transition: width 0.5s; }
    .blist-val { font-weight: 600; width: 50px; text-align: right; }
    .no-data { text-align: center; color: #94a3b8; font-size: 12px; padding: 20px; }

    /* Table */
    .table-card { background: #fff; border-radius: 12px; padding: 18px; box-shadow: 0 1px 3px rgba(0,0,0,0.06); border: 1px solid #f1f5f9; margin-bottom: 16px; }
    .table-card h3 { font-size: 14px; font-weight: 600; margin: 0 0 12px; }
    .table-scroll { overflow-x: auto; }
    table { width: 100%; border-collapse: collapse; font-size: 12px; }
    th { text-align: left; padding: 8px 10px; border-bottom: 2px solid #e2e8f0; color: #64748b; font-weight: 600; font-size: 11px; text-transform: uppercase; letter-spacing: 0.5px; white-space: nowrap; }
    td { padding: 8px 10px; border-bottom: 1px solid #f1f5f9; }
    tr:hover { background: #f8fafc; }
    .warning-row { background: #fffbeb; }
    .critical-row { background: #fef2f2; }
    .badge { display: inline-block; padding: 2px 8px; border-radius: 6px; color: #fff; font-size: 11px; font-weight: 600; }
    .status-dot { display: inline-block; width: 8px; height: 8px; border-radius: 50%; margin-right: 4px; background: #94a3b8; }
    .status-dot.active { background: #22c55e; }
    .status-dot.maint { background: #f59e0b; }

    /* AI Analysis */
    .analysis-card { background: #fff; border-radius: 12px; box-shadow: 0 1px 3px rgba(0,0,0,0.06); border: 1px solid #f1f5f9; margin-bottom: 16px; overflow: hidden; }
    .analysis-header { display: flex; justify-content: space-between; align-items: center; padding: 16px 20px; background: linear-gradient(135deg, #6366f1, #8b5cf6); color: #fff; }
    .analysis-header h3 { margin: 0; font-size: 15px; }
    .tokens-badge { background: rgba(255,255,255,0.2); padding: 3px 10px; border-radius: 12px; font-size: 11px; }
    .analysis-body { padding: 20px; line-height: 1.7; font-size: 13px; }

    /* Markdown styling */
    .markdown-content :first-child { margin-top: 0; }
    :host ::ng-deep .markdown-content h2 { font-size: 16px; font-weight: 700; margin: 24px 0 8px; padding-bottom: 6px; border-bottom: 2px solid #e2e8f0; color: #1e293b; }
    :host ::ng-deep .markdown-content h3 { font-size: 14px; font-weight: 600; margin: 16px 0 6px; color: #334155; }
    :host ::ng-deep .markdown-content ul, :host ::ng-deep .markdown-content ol { padding-left: 20px; margin: 8px 0; }
    :host ::ng-deep .markdown-content li { margin: 4px 0; }
    :host ::ng-deep .markdown-content strong { color: #1e293b; }
    :host ::ng-deep .markdown-content p { margin: 6px 0; }
    :host ::ng-deep .markdown-content table { width: 100%; border-collapse: collapse; margin: 10px 0; font-size: 12px; }
    :host ::ng-deep .markdown-content table th { background: #f8fafc; padding: 6px 10px; border: 1px solid #e2e8f0; text-align: left; font-weight: 600; }
    :host ::ng-deep .markdown-content table td { padding: 6px 10px; border: 1px solid #e2e8f0; }

    /* Q&A Section */
    .qa-card { background: #fff; border-radius: 12px; padding: 20px; box-shadow: 0 1px 3px rgba(0,0,0,0.06); border: 1px solid #f1f5f9; }
    .qa-card h3 { margin: 0 0 6px; font-size: 15px; }
    .qa-hint { font-size: 12px; color: #94a3b8; margin: 0 0 12px; }
    .qa-suggestions { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 16px; }
    .suggestion-btn { padding: 6px 14px; background: #f1f5f9; border: 1px solid #e2e8f0; border-radius: 20px; font-size: 12px; cursor: pointer; color: #475569; transition: all 0.2s; }
    .suggestion-btn:hover { background: #e0e7ff; border-color: #6366f1; color: #6366f1; }
    .suggestion-btn:disabled { opacity: 0.5; cursor: not-allowed; }
    .qa-messages { margin-bottom: 16px; max-height: 400px; overflow-y: auto; display: flex; flex-direction: column; gap: 12px; }
    .qa-msg { padding: 12px 16px; border-radius: 10px; }
    .qa-msg.user { background: #e0e7ff; margin-left: 40px; }
    .qa-msg.ai { background: #f0fdf4; margin-right: 40px; }
    .qa-msg-header { font-size: 11px; font-weight: 600; margin-bottom: 4px; color: #64748b; }
    .qa-msg-body { font-size: 13px; line-height: 1.6; }
    .qa-input-row { display: flex; gap: 8px; }
    .qa-input-row input { flex: 1; padding: 10px 14px; border: 1px solid #e2e8f0; border-radius: 10px; font-size: 13px; outline: none; }
    .qa-input-row input:focus { border-color: #6366f1; box-shadow: 0 0 0 3px rgba(99,102,241,0.1); }
    .btn-ask { padding: 10px 20px; background: #6366f1; color: #fff; border: none; border-radius: 10px; font-weight: 600; font-size: 13px; cursor: pointer; }
    .btn-ask:disabled { opacity: 0.5; cursor: not-allowed; }

    /* Empty state */
    .empty-state { text-align: center; padding: 80px 20px; }
    .empty-icon { font-size: 64px; margin-bottom: 16px; }
    .empty-state h2 { font-size: 24px; margin: 0 0 12px; }
    .empty-state p { color: #64748b; font-size: 14px; max-width: 500px; margin: 0 auto 24px; line-height: 1.6; }

    /* Responsive */
    @media (max-width: 1100px) {
      .kpi-row { grid-template-columns: repeat(3, 1fr); }
      .charts-row { grid-template-columns: 1fr 1fr; }
      .chart-card.wide { grid-column: span 2; }
    }
    @media (max-width: 700px) {
      .kpi-row { grid-template-columns: repeat(2, 1fr); }
      .charts-row { grid-template-columns: 1fr; }
      .chart-card.wide { grid-column: span 1; }
      .report-header { flex-direction: column; align-items: stretch; }
    }
  `]
})
export class AiFleetReportComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();

  selectedPeriod = 'month';
  loading = false;
  askLoading = false;
  error = '';
  reportData: any = null;
  renderedAnalysis: SafeHtml = '';
  questionInput = '';
  qaMessages: { role: string; text: string; html: SafeHtml }[] = [];

  // Chart helpers
  healthItems: { label: string; count: number; pct: number; color: string }[] = [];
  costItems: { label: string; value: number; pct: number; color: string }[] = [];
  maxFuel = 1;
  maxMileage = 1;

  suggestions = [
    'Quel vehicule devrait etre remplace en priorite ?',
    'Quelles pieces sont a surveiller sur mes vehicules ?',
    'Comment reduire les couts de maintenance ?',
    'Quel est le cout total de possession par vehicule ?',
    'Comment ameliorer la consommation de carburant ?'
  ];

  constructor(
    public router: Router,
    private apiService: ApiService,
    private cdr: ChangeDetectorRef,
    private sanitizer: DomSanitizer
  ) {}

  ngOnInit() {}

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }

  generateReport() {
    this.loading = true;
    this.error = '';
    this.qaMessages = [];

    this.apiService.generateFleetReport(this.selectedPeriod).pipe(takeUntil(this.destroy$)).subscribe({
      next: (data) => {
        this.reportData = data;
        this.renderedAnalysis = this.renderMarkdown(data.aiAnalysis || '');
        this.buildChartData();
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.error = err.error?.message || 'Erreur lors de la generation du rapport';
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }

  askQuestion(question: string) {
    if (!question?.trim() || this.askLoading) return;
    this.askLoading = true;
    this.questionInput = '';

    this.qaMessages.push({ role: 'user', text: question, html: question });

    const context = this.reportData?.aiAnalysis || '';
    this.apiService.askFleetReport(question, context).pipe(takeUntil(this.destroy$)).subscribe({
      next: (data) => {
        this.qaMessages.push({ role: 'ai', text: data.answer, html: this.renderMarkdown(data.answer) });
        this.askLoading = false;
        this.cdr.detectChanges();
        // Scroll to bottom
        setTimeout(() => {
          const el = document.querySelector('.qa-messages');
          if (el) el.scrollTop = el.scrollHeight;
        }, 100);
      },
      error: (err) => {
        this.qaMessages.push({ role: 'ai', text: 'Erreur: ' + (err.error?.message || 'Service indisponible'), html: 'Erreur: ' + (err.error?.message || 'Service indisponible') });
        this.askLoading = false;
        this.cdr.detectChanges();
      }
    });
  }

  private buildChartData() {
    if (!this.reportData) return;
    const hd = this.reportData.charts.healthDistribution;
    const total = (hd.excellent || 0) + (hd.good || 0) + (hd.fair || 0) + (hd.poor || 0) + (hd.critical || 0);
    const pct = (v: number) => total > 0 ? (v / total) * 100 : 0;
    this.healthItems = [
      { label: 'Excellent', count: hd.excellent, pct: pct(hd.excellent), color: '#22c55e' },
      { label: 'Bon', count: hd.good, pct: pct(hd.good), color: '#3b82f6' },
      { label: 'Moyen', count: hd.fair, pct: pct(hd.fair), color: '#f59e0b' },
      { label: 'Faible', count: hd.poor, pct: pct(hd.poor), color: '#f97316' },
      { label: 'Critique', count: hd.critical, pct: pct(hd.critical), color: '#ef4444' }
    ];

    const cb = this.reportData.charts.costBreakdown;
    const totalCost = (cb.fuel || 0) + (cb.maintenance || 0) + (cb.repairs || 0) + (cb.other || 0);
    const cpct = (v: number) => totalCost > 0 ? (v / totalCost) * 100 : 0;
    this.costItems = [
      { label: 'Carburant', value: cb.fuel, pct: cpct(cb.fuel), color: '#f59e0b' },
      { label: 'Maintenance', value: cb.maintenance, pct: cpct(cb.maintenance), color: '#3b82f6' },
      { label: 'Reparations', value: cb.repairs, pct: cpct(cb.repairs), color: '#ef4444' },
      { label: 'Autres', value: cb.other, pct: cpct(cb.other), color: '#94a3b8' }
    ];

    this.maxFuel = Math.max(...(this.reportData.charts.topFuelConsumers?.map((f: any) => f.value) || [1]), 1);
    this.maxMileage = Math.max(...(this.reportData.charts.mileageByVehicle?.map((m: any) => m.value) || [1]), 1);
  }

  getBarPct(value: number, max: number): number {
    return max > 0 ? (value / max) * 100 : 0;
  }

  getScoreColor(score: number): string {
    if (score >= 80) return '#22c55e';
    if (score >= 60) return '#3b82f6';
    if (score >= 40) return '#f59e0b';
    return '#ef4444';
  }

  getHealthBg(level: string): string {
    switch (level) {
      case 'excellent': return '#22c55e';
      case 'good': return '#3b82f6';
      case 'fair': return '#f59e0b';
      case 'poor': return '#f97316';
      case 'critical': return '#ef4444';
      default: return '#94a3b8';
    }
  }

  private renderMarkdown(text: string): SafeHtml {
    try {
      const html = marked.parse(text, { async: false }) as string;
      return this.sanitizer.bypassSecurityTrustHtml(html);
    } catch {
      return text;
    }
  }
}
