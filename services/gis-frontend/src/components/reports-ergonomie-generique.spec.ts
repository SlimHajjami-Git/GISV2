// marked n'est livré qu'en ESM, que jest ne transforme pas dans node_modules : le charger
// avec le composant fait échouer la suite avant le premier test. Même contournement que
// reports.component.spec.ts.
jest.mock('marked', () => ({ marked: { parse: (s: string) => s } }));

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { FormsModule } from '@angular/forms';
import { of } from 'rxjs';
import { ReportsComponent } from './reports.component';
import { ApiService } from '../services/api.service';
import { UserPreferencesService } from '../services/user-preferences.service';
import { AppDistancePipe, AppSpeedPipe, AppVolumePipe } from '../pipes/user-preference-pipes';

/**
 * Ergonomie des DOUZE rapports du tableau générique (recette de Karim du
 * 19/09/2026 : « applique les règles pour les autres rapports »). Quatre règles
 * verrouillées ici, parce qu'aucune ne se voit dans un test fonctionnel :
 *  1. aucune colonne ne reste vide selon le périmètre choisi ;
 *  2. le compteur de pagination compte des ÉLÉMENTS, pas des séparateurs ;
 *  3. une valeur et son unité ne se séparent jamais ;
 *  4. le cadre du tableau est borné, et la PAGE ne l'est pas.
 */
// Le tsconfig des specs n'embarque pas les types Node : on déclare le strict
// nécessaire pour relire la feuille de style du composant, qui porte des règles
// que seul un test de fichier peut verrouiller (le cadre borné, la page qui ne
// l'est pas).
declare const require: (m: string) => any;
declare const __dirname: string;

describe('Rapports génériques — ergonomie', () => {
  let composant: ReportsComponent;
  let fixture: ComponentFixture<ReportsComponent>;

  beforeEach(async () => {
    // Le service PDF précharge le logo par fetch(), absent de jsdom.
    (globalThis as any).fetch = jest.fn(() => Promise.resolve({ ok: false }));

    await TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule, FormsModule, ReportsComponent],
      providers: [ApiService]
    }).compileComponents();

    fixture = TestBed.createComponent(ReportsComponent);
    composant = fixture.componentInstance;
    const api = TestBed.inject(ApiService);
    jest.spyOn(api, 'isAuthenticated').mockReturnValue(true);
    jest.spyOn(api, 'getVehicles').mockReturnValue(of([]));
  });

  /** Affiche le tableau générique pour un type de rapport donné. */
  const afficher = (type: string, lignes: any[], vehicule = '') => {
    composant.selectedTemplate = composant.templates.find(t => t.type === type)!;
    composant.selectedVehicleId = vehicule;
    composant.tableData = lignes;
    composant.reportGenerated = true;
    composant.loading = false;
    fixture.detectChanges();
  };

  const entetes = (): string[] =>
    [...fixture.nativeElement.querySelectorAll('.table-section thead th')]
      .map((th: any) => (th.textContent || '').replace(/[↕▲▼]/g, '').trim());

  /** Colonnes dont AUCUNE cellule du corps ne porte de texte. */
  const colonnesVides = (): string[] => {
    const noms = entetes();
    const lignes = [...fixture.nativeElement.querySelectorAll('.table-section tbody tr')];
    return noms.filter((_nom, i) =>
      lignes.length > 0 && lignes.every((tr: any) => {
        const td = tr.children[i];
        return td && !(td.textContent || '').trim();
      })
    );
  };

  // ───────────────────────────── LOT A ─────────────────────────────

  describe('colonnes conditionnelles selon le périmètre', () => {
    /** Ligne du Kilométrage SANS véhicule choisi : une ligne = un véhicule,
     *  aucune date ni odomètre n'est produite (processMileageReportAllVehicles). */
    const ligneKmFlotte = {
      vehicleName: 'Commercial 01', plate: 'GD-421-NV',
      distance: '1 234,5 km', distanceValue: 1234.5, tripCount: 12,
      drivingTime: '3h 20min', maxSpeed: '118,0 km/h',
      avgDaily: '41,2 km/j', activeDays: '18/30'
    };

    it('ne rend ni « Date » ni « Odomètre » au Kilométrage de flotte — elles sortaient vides', () => {
      afficher('mileage', [ligneKmFlotte, { ...ligneKmFlotte, plate: 'GA-214-RK' }]);
      const noms = entetes();
      expect(noms).toEqual(['Véhicule', 'Distance', 'Trajets', 'Temps de conduite', 'Vitesse max']);
      expect(colonnesVides()).toEqual([]);
    });

    it('rend bien « Date » et « Odomètre » dès qu\'un véhicule est choisi', () => {
      afficher('mileage', [{
        date: 'lun. 15/09/26', distance: '82,4 km', distanceValue: 82.4, tripCount: 3,
        drivingTime: '1h 10min', maxSpeed: '96,0 km/h', odometer: '115 540 km'
      }], '4');
      expect(entetes()).toEqual(['Date', 'Distance', 'Trajets', 'Temps de conduite', 'Vitesse max', 'Odomètre']);
      expect(colonnesVides()).toEqual([]);
    });

    it('retire « Jour » du Kilométrage par période quand le périmètre est la flotte', () => {
      composant.selectedMileagePeriodType = 'day';
      afficher('mileage-period', [
        { period: 'GD-421-NV', distance: '1 234,5 km', distanceValue: 1234.5, tripCount: 12, drivingTime: '3h 20min', maxSpeed: '118,0 km/h' },
        { period: 'GA-214-RK', distance: '987,0 km', distanceValue: 987, tripCount: 9, drivingTime: '2h 40min', maxSpeed: '112,0 km/h' },
      ]);
      expect(entetes()).not.toContain('Jour');
      expect(colonnesVides()).toEqual([]);
    });

    it('garde « Jour » quand un véhicule est choisi : la ligne est alors une journée', () => {
      composant.selectedMileagePeriodType = 'day';
      afficher('mileage-period', [
        { period: '15/09/2026', dayOfWeek: 'lundi', distance: '82,4 km', distanceValue: 82.4, tripCount: 3, drivingTime: '1h 10min', maxSpeed: '96,0 km/h' },
      ], '4');
      expect(entetes()).toContain('Jour');
      expect(colonnesVides()).toEqual([]);
    });

    it('retire « Moy. jour » et « Jours actifs » du cumul mensuel de flotte', () => {
      composant.selectedMileagePeriodType = 'month';
      afficher('mileage-period', [
        { period: 'GD-421-NV', distance: '12 345,0 km', distanceValue: 12345, tripCount: 120, drivingTime: '48h' },
      ]);
      const noms = entetes();
      expect(noms).not.toContain('Moy. jour');
      expect(noms).not.toContain('Jours actifs');
      expect(colonnesVides()).toEqual([]);
    });

    /** Trajets et Arrêts : la colonne véhicule était DÉJÀ conditionnelle et
     *  alimentée des deux côtés. On verrouille ce qui marche pour qu'aucune
     *  correction de largeur ne le casse. */
    /** L'écran et les trois exports doivent montrer les MÊMES colonnes : sans
     *  ce garde-fou, le PDF et le classeur gardaient la colonne vide que
     *  l'écran venait d'abandonner. */
    it('retire aussi la colonne vide des exports Excel, PDF et CSV', () => {
      afficher('mileage', [ligneKmFlotte]);
      const config = (composant as any).buildExportConfig('mileage', 'Tous les véhicules', '', 'pdf');
      const entetesExport = config.columns.map((c: any) => c.header);
      expect(entetesExport).not.toContain('Date');
      expect(entetesExport).not.toContain('Odomètre');
      expect(entetesExport).toContain('Distance');
      // Relecture du 20/09/2026 : retirer les deux colonnes vides sans donner
      // la colonne « Véhicule » rendait le PDF et le classeur ILLISIBLES —
      // plus rien n'y disait de quel véhicule parlait la ligne.
      expect(entetesExport[0]).toBe('Véhicule');
      const rendu = config.formatters['_vehicule'](null, config.data[0]);
      expect(rendu).toBe('GD-421-NV');
    });

    it('nomme le véhicule par son NOM quand la plaque manque, comme à l\'écran', () => {
      afficher('mileage', [{ ...ligneKmFlotte, plate: '-' }]);
      const config = (composant as any).buildExportConfig('mileage', 'Tous les véhicules', '', 'pdf');
      expect(config.formatters['_vehicule'](null, config.data[0])).toBe('Commercial 01');
    });

    it('garde toutes les colonnes de l\'export quand un véhicule est choisi', () => {
      afficher('mileage', [{
        date: 'lun. 15/09/26', distance: '82,4 km', tripCount: 3,
        drivingTime: '1h 10min', maxSpeed: '96,0 km/h', odometer: '115 540 km'
      }], '4');
      const config = (composant as any).buildExportConfig('mileage', 'Commercial 01', '', 'pdf');
      const entetesExport = config.columns.map((c: any) => c.header);
      expect(entetesExport).toContain('Date');
      expect(entetesExport).toContain('Odomètre');
    });

    it('ne laisse aucune colonne vide au rapport Trajets, avec ou sans véhicule', () => {
      const trajet = {
        isTrip: true, tripNumber: 1, vehicleName: 'Commercial 01', vehiclePlate: 'GD-421-NV',
        startTime: '18/09/2026 07:10', endTime: '18/09/2026 08:24', duration: '1h 14min',
        distance: '42,8 km', maxSpeed: '112 km/h', startAddress: 'Tunis', endAddress: 'Ben Arous'
      };
      afficher('trips', [trajet]);
      expect(entetes()[0]).toBe('Immatriculation');
      expect(colonnesVides()).toEqual([]);

      afficher('trips', [trajet], '4');
      expect(entetes()[0]).toBe('Type');
      expect(colonnesVides()).toEqual([]);
    });
  });

  // ───────────────────────────── LOT A (compteur) ─────────────────────────────

  describe('compteur de pagination', () => {
    /** 30 jours, 200 trajets : l'écran annonçait 230 éléments. */
    const avecEntetesDeJour = (jours: number, parJour: number) => {
      const lignes: any[] = [];
      for (let j = 0; j < jours; j++) {
        lignes.push({ isDayHeader: true, dayLabel: `📅 ${j + 1}/09/2026` });
        for (let t = 0; t < parJour; t++) lignes.push({ isTrip: true, tripNumber: j * parJour + t });
      }
      return lignes;
    };

    it('ne compte PAS les lignes-titres de jour comme des éléments', () => {
      composant.tableData = avecEntetesDeJour(30, 200 / 30 | 0);
      expect(composant.tableData.length).toBe(30 + 30 * 6);
      expect(composant.itemCount).toBe(180);
    });

    it('rend un premier et un dernier élément cohérents page après page', () => {
      composant.tableData = avecEntetesDeJour(4, 9); // 4 titres + 36 trajets
      composant.pageSize = 10;

      composant.currentPage = 1;
      expect(composant.startItem).toBe(1);
      expect(composant.endItem).toBe(9); // 1 titre + 9 trajets sur la première page

      composant.currentPage = 2;
      expect(composant.startItem).toBe(10);
      expect(composant.endItem).toBe(18);

      composant.currentPage = 4;
      expect(composant.endItem).toBe(composant.itemCount);
      expect(composant.itemCount).toBe(36);
    });

    it('annonce zéro élément sur un rapport vide, jamais « 1 à 0 »', () => {
      composant.tableData = [];
      expect(composant.itemCount).toBe(0);
      expect(composant.startItem).toBe(0);
      expect(composant.endItem).toBe(0);
    });

    it('affiche le compte des ÉLÉMENTS dans la barre, pas celui des lignes', () => {
      composant.selectedTemplate = composant.templates.find(t => t.type === 'trips')!;
      composant.tableData = avecEntetesDeJour(3, 5); // 3 titres + 15 trajets
      composant.reportGenerated = true;
      composant.loading = false;
      fixture.detectChanges();
      const texte = (fixture.nativeElement.querySelector('.table-section .items-info') as HTMLElement).textContent || '';
      expect(texte).toContain('sur 15');
      expect(texte).not.toContain('sur 18');
    });
  });

  // ───────────────────────────── LOT D ─────────────────────────────

  describe('montants alignés et total épinglé', () => {
    // Devise posée explicitement : le repli du déploiement est le dinar, et un
    // test de mise en page ne doit pas dépendre d'environment.ts.
    beforeEach(() => TestBed.inject(UserPreferencesService).update({ currency: 'EUR' }));

    const couts = [
      { vehicleName: 'GD-421-NV', date: '18/09/2026', description: 'Réparation accident', supplierName: 'Garage Renault Lyon Est',
        laborCost: 700, partsCost: 600, totalCost: 1300,
        laborCostFormatted: '700,00 €', partsCostFormatted: '600,00 €', totalCostFormatted: '1 300,00 €' },
      { vehicleName: 'GB-587-TM', date: '11/09/2026', description: 'Vidange + filtre à huile', supplierName: '-',
        laborCost: 90, partsCost: 60, totalCost: 150,
        laborCostFormatted: '90,00 €', partsCostFormatted: '60,00 €', totalCostFormatted: '150,00 €' },
    ];

    it('additionne TOUT le rapport « Coûts », pas seulement la page affichée', () => {
      composant.tableData = [...couts, ...couts, ...couts];
      composant.pageSize = 2;
      expect(composant.totauxCouts()).toEqual({ laborCost: 2370, partsCost: 1980, totalCost: 4350 });
    });

    // Relecture du 22/09/2026 : « Annulée » est de nouveau proposée dans l'écran
    // Réparations. La carte « Coût total » écartait la ligne annulée (800), la
    // ligne de TOTAL l'additionnait (1 800), et rien ne la distinguait d'une
    // dépense réelle, ni à l'écran ni dans le PDF, l'Excel ou le CSV.
    describe('réparation annulée', () => {
      const reparations = () => [
        { id: 1, vehicleId: 7, vehiclePlate: 'GD-421-NV', repairDate: '2026-09-18', description: 'Embrayage',
          supplierName: 'Garage Nord', laborCost: 200, partsCost: 300, totalCost: 500, status: 'completed', repairType: 'mecanique' },
        { id: 2, vehicleId: 7, vehiclePlate: 'GD-421-NV', repairDate: '2026-09-15', description: 'Plaquettes',
          supplierName: 'Garage Nord', laborCost: 100, partsCost: 200, totalCost: 300, status: 'completed', repairType: 'freinage' },
        { id: 3, vehicleId: 8, vehiclePlate: 'GB-587-TM', repairDate: '2026-09-10', description: 'Boîte de vitesses',
          supplierName: 'Garage Sud', laborCost: 400, partsCost: 600, totalCost: 1000, status: 'Cancelled', repairType: 'mecanique' },
      ];

      it('reste listée, mais n\'entre pas dans la ligne de TOTAL — même chiffre que la carte', () => {
        composant.processRepairsReport(reparations());
        expect(composant.tableData.length).toBe(3);
        expect(composant.totauxCouts()).toEqual({ laborCost: 300, partsCost: 500, totalCost: 800 });
        expect(composant.statisticsData['Coût total']).toBe(composant.formatCurrency(800));
      });

      it('se signale dans la description, à l\'écran comme dans les exports', () => {
        composant.processRepairsReport(reparations());
        const annulee = composant.tableData.find((r: any) => r.reference !== undefined && r.totalCost === 1000);
        expect(annulee.description).toBe('Boîte de vitesses (annulée, hors coûts)');
        expect(composant.tableData.filter((r: any) => /annulée/.test(r.description)).length).toBe(1);

        composant.selectedTemplate = composant.templates.find(t => t.type === 'costs')!;
        const config = (composant as any).buildExportConfig('costs', 'Tous les véhicules', 'septembre 2026');
        const exportee = config.data.find((r: any) => r.totalCost === 1000);
        expect(exportee.description).toContain('(annulée, hors coûts)');
        expect(config.columns.map((c: any) => c.dataKey)).toContain('description');
      });

      it('barre ses montants et annonce l\'exclusion sur la ligne de TOTAL', () => {
        composant.processRepairsReport(reparations());
        afficher('costs', composant.tableData);
        const lignes = [...fixture.nativeElement.querySelectorAll('.table-section tbody tr')] as HTMLElement[];
        const barrees = lignes.filter(tr => tr.querySelectorAll('td.montant-annule').length === 3);
        expect(barrees.length).toBe(1);
        expect(barrees[0].textContent).toContain('Boîte de vitesses (annulée, hors coûts)');

        const pied = fixture.nativeElement.querySelector('.table-section tfoot .dept-total-row');
        const cellules = [...pied.querySelectorAll('td')].map((td: any) => td.textContent.replace(/\s+/g, ' ').trim());
        expect(cellules[0]).toContain('dont 1 annulée(s), hors total');
        expect(cellules[3]).toBe('800,00 €');
      });
    });

    it('épingle une ligne de total au rapport « Coûts », montants à droite', () => {
      afficher('costs', couts);
      const pied = fixture.nativeElement.querySelector('.table-section tfoot .dept-total-row');
      expect(pied).toBeTruthy();
      const cellules = [...pied.querySelectorAll('td')].map((td: any) => td.textContent.trim());
      // Quatre cellules : l'intitulé (colspan 4), puis main d'œuvre, pièces, total.
      expect(cellules[0]).toContain('TOTAL');
      expect(cellules[1]).toBe('790,00 €');
      expect(cellules[2]).toBe('660,00 €');
      expect(cellules[3]).toBe('1 450,00 €');
      // Les trois montants portent la classe dédiée, jamais un style sur .data-table td.
      expect(pied.querySelectorAll('td.num').length).toBe(3);
    });

    it('épingle un total au rapport « Entretiens » et ignore les échéances planifiées', () => {
      afficher('maintenance', [
        { vehicleName: 'GD-421-NV', date: '11/09/2026', type: 'Vidange', description: '-', supplierName: '-', cost: 160, costFormatted: '160,00 €', mileage: '115 540 km' },
        { vehicleName: 'GB-587-TM', date: '11/09/2026', type: 'Vidange', description: '-', supplierName: '-', cost: 150, costFormatted: '150,00 €', mileage: '96 558 km' },
        { vehicleName: 'GC-936-LP', date: '—', type: 'Révision', description: 'Prochaine échéance', supplierName: '-', cost: 0, costFormatted: '—', mileage: 'prévu à 60 000 km' },
      ]);
      expect(composant.totalEntretiens()).toBe(310);
      const pied = fixture.nativeElement.querySelector('.table-section tfoot .dept-total-row');
      expect((pied.querySelectorAll('td')[1] as HTMLElement).textContent!.trim()).toBe('310,00 €');
    });

    it('ne pose AUCUN total sur les rapports qui n\'additionnent rien', () => {
      afficher('stops', [{ time: '18/09/2026 09:00', endTime: '18/09/2026 09:40', duration: '40min', address: 'Tunis', typeCode: 'A', typeLabel: 'Arrêt', vehicleName: 'GD-421-NV' }]);
      expect(fixture.nativeElement.querySelector('.table-section tfoot')).toBeNull();
    });
  });

  // ───────────────────────────── LOT E ─────────────────────────────

  describe('largeur minimale des graphes', () => {
    const fenetre = (px: number) => Object.defineProperty(window, 'innerWidth', { value: px, configurable: true });

    beforeEach(() => fenetre(1536));

    const points = (n: number) => { composant.chartData = Array.from({ length: n }, (_, i) => ({ label: String(i), value: i })); };

    it('offre 1 252 px au graphe sur l\'écran de référence', () => {
      expect(composant.largeurDisponibleGraphe()).toBe(1252);
    });

    it('n\'affiche AUCUN bouton tant que le graphe tient — 24 barres horaires, 31 jours', () => {
      composant.selectedTemplate = composant.templates.find(t => t.type === 'speed-infraction')!;
      points(24); // « Infractions par heure de la journée »
      expect(composant.grapheDeborde()).toBe(false);
      expect(composant.showChartScrollButtons()).toBe(false);
      expect(composant.getChartMinWidth()).toBe('auto');

      composant.selectedTemplate = composant.templates.find(t => t.type === 'mileage')!;
      points(31); // un mois de kilométrage
      expect(composant.showChartScrollButtons()).toBe(false);
      expect(composant.getChartMinWidth()).toBe('auto');
    });

    it('impose une largeur ET des boutons dès que le graphe ne tient plus', () => {
      composant.selectedTemplate = composant.templates.find(t => t.type === 'mileage')!;
      points(40); // flotte de quarante véhicules : 1 600 px exigés pour 1 252 offerts
      expect(composant.grapheDeborde()).toBe(true);
      expect(composant.getChartMinWidth()).toBe('1600px');
      expect(composant.showChartScrollButtons()).toBe(true);
    });

    it('suit la fenêtre : le même graphe déborde sur un écran plus étroit', () => {
      composant.selectedTemplate = composant.templates.find(t => t.type === 'mileage')!;
      points(31);
      expect(composant.grapheDeborde()).toBe(false);
      fenetre(1280); // 996 px offerts, 1 240 exigés
      expect(composant.grapheDeborde()).toBe(true);
    });

    it('laisse Carburant se caler à la largeur du cadre, quoi qu\'il arrive', () => {
      composant.selectedTemplate = composant.templates.find(t => t.type === 'fuel')!;
      points(500);
      expect(composant.grapheDeborde()).toBe(false);
      expect(composant.getChartMinWidth()).toBe('auto');
      expect(composant.showChartScrollButtons()).toBe(false);
    });
  });

  // ───────────────────────────── LOT B ─────────────────────────────

  describe('cadre du tableau borné', () => {
    const css: string = require('fs').readFileSync(
      require('path').join(__dirname, 'reports.component.css'), 'utf8');

    it('borne le CADRE, enfant direct de .table-section, et lui donne son défilement', () => {
      const bloc = css.match(/\.table-section > \.table-wrapper \{[^}]*\}/);
      expect(bloc).toBeTruthy();
      expect(bloc![0]).toContain('overflow-y: auto');
      expect(bloc![0]).toContain('flex: 1 1 auto');
    });

    it('ne borne PAS la page : les cinq rapports de coûts n\'ont pas de défileur interne', () => {
      // Mesuré : .reports-page en height:calc(100vh - 42px) réparait le tableau
      // générique mais rendait le bas de « Coût d'exploitation réel »
      // INATTEIGNABLE. La borne est sur .workspace-content, que ces cinq
      // rapports n'utilisent pas.
      const page = css.match(/\.reports-page \{[^}]*\}/);
      expect(page![0]).toContain('min-height: calc(100vh - 42px)');
      expect(page![0]).not.toMatch(/(^|[^-])height: calc\(100vh/m);
      const espace = css.match(/\.workspace-content \{[^}]*\}/);
      expect(espace![0]).toContain('height: calc(100vh - 42px)');
    });

    it('ne touche à AUCUN des deux cadres déjà réglés par les rapports validés', () => {
      // Une règle globale sur .table-wrapper entrerait en conflit avec
      // .mf-recap-wrapper et .evolution-recap-wrapper.
      expect(css).toContain('.mf-workspace .table-wrapper.mf-recap-wrapper');
      expect(css).toContain('.evolution-workspace .evolution-recap-wrapper');
      const globales = css.match(/^\.table-wrapper \{[^}]*\}/gm) || [];
      for (const regle of globales) expect(regle).not.toContain('100vh');
    });

    it('pose le cadre là où la règle CSS l\'attend : .table-section > .table-wrapper', () => {
      afficher('stops', [{ time: '18/09/2026 09:00', endTime: '18/09/2026 09:40', duration: '40min', address: 'Tunis', typeCode: 'A', typeLabel: 'Arrêt', vehicleName: 'GD-421-NV' }]);
      const cadre = fixture.nativeElement.querySelector('.table-section > .table-wrapper');
      expect(cadre).toBeTruthy();
      expect(cadre.querySelector('table.data-table')).toBeTruthy();
      expect(fixture.nativeElement.querySelector('.workspace-content')).toBeTruthy();
    });
  });

  // ───────────────────────────── LOT C ─────────────────────────────

  describe('valeurs insécables', () => {
    const prefs = () => TestBed.inject(UserPreferencesService);

    it('ne laisse AUCUNE espace sécable dans un montant, quelle que soit la devise', () => {
      for (const code of ['TND', 'DZD', 'EUR'] as const) {
        prefs().update({ currency: code });
        const rendu = prefs().formatCurrency(2615.55);
        expect(rendu).not.toMatch(/[  ]/);
        expect(rendu.endsWith(' ' + (code === 'EUR' ? '€' : code))).toBe(true);
      }
    });

    it('tient « 2 615,55 TND » d\'un seul tenant — le constat de la recette', () => {
      prefs().update({ currency: 'TND' });
      expect(prefs().formatCurrency(2615.55)).toBe('2 615,55 TND');
    });

    it('rend le séparateur de milliers IDENTIQUE à celui de la devise', () => {
      prefs().update({ currency: 'DZD' });
      const rendu = prefs().formatCurrency(1234567.89);
      const espaces = new Set([...rendu].filter(c => /\s/u.test(c)));
      expect(espaces).toEqual(new Set([' ']));
    });

    /** Les unités suivent la même règle que la devise : « 118 km/h » ne se
     *  coupe pas davantage que « 2 615,55 TND ». Ces pipes servent tout
     *  l'écran, pas seulement les rapports. */
    it('tient la distance, la vitesse et le volume d\'un seul tenant', () => {
      const cdr = { markForCheck: () => {} } as any;
      const p = prefs();
      expect(new AppDistancePipe(p, cdr).transform(1234567, 0)).toBe('1 234 567 km');
      expect(new AppSpeedPipe(p, cdr).transform(118, 0)).toBe('118 km/h');
      expect(new AppVolumePipe(p, cdr).transform(52.4, 1)).toBe('52,4 L');
      for (const rendu of [new AppDistancePipe(p, cdr).transform(1234567, 0), new AppSpeedPipe(p, cdr).transform(118, 0)]) {
        expect(rendu).not.toMatch(/[  ]/);
      }
    });

    it('rend toujours « - » sur une valeur absente, sans espace parasite', () => {
      const cdr = { markForCheck: () => {} } as any;
      expect(new AppDistancePipe(prefs(), cdr).transform(null)).toBe('-');
      expect(new AppSpeedPipe(prefs(), cdr).transform(undefined)).toBe('-');
    });
  });

  // ───────────────── LOT F — remise en état du 20/09/2026 ─────────────────

  /**
   * LE TEST QUI AURAIT ATTRAPÉ LE DÉFAUT.
   *
   * Le lot du 19/09/2026 a posé « white-space: nowrap » sur
   * .monthly-cost-table tbody td.num. Ce sélecteur sert SEPT tableaux, dont le
   * récapitulatif d'« Évolution des coûts » — que Karim a validé — et dont le
   * cadre (.evolution-recap-wrapper) est en overflow-x: hidden : ce qui dépasse
   * y est COUPÉ, sans ascenseur et sans le moindre signe. En dinar, les
   * colonnes « Total » et « Var. / mois préc. » sortaient du champ.
   * Le banc précédent ne l'a pas vu : il comparait « avec » et « sans » nowrap
   * en gardant l'espace fine insécable des DEUX côtés — or elle empêche déjà la
   * coupure à elle seule, l'écart ne pouvait donc valoir que 0 px.
   *
   * Ici on compte les colonnes qui tiennent VRAIMENT dans le cadre, à partir
   * des chaînes réellement rendues. Les chasses ci-dessous sont mesurées au
   * banc, dans Chrome, police Inter, sur l'écran de référence de 1 536 px ; le
   * modèle rejoue la largeur exigée du tableau à moins d'un pixel (792,8 px
   * calculés contre 793 px mesurés dans le pire cas).
   */
  describe('récapitulatif d\'« Évolution des coûts » — colonnes visibles', () => {
    /** Chasses mesurées au banc (Chrome, Inter). */
    const PX = {
      chiffre: 8,        // 13 px en tabular-nums : tous les chiffres ont la même chasse
      fine: 1.6,         // espace FINE INSÉCABLE U+202F (séparateur de milliers fr-FR)
      virgule: 3.2,
      signe: 8,          // « - » et « + » ont aussi la chasse d'un chiffre
      euro: 8.8,
      pourcent: 12.8,
      lettre: 7.6,       // 13 px, texte courant
      espace: 6,         // « Septembre 2026 » = 9 x 7,6 + 6 + 4 x 8 = 106,4 px, la mesure exacte
      lettreEntete: 7.3, // 10,5 px, majuscules, interlettrage 0,6 px : moyenne des
                         // mots mesurés (CARBURANT 71 px, ENTRETIENS 71,6, RÉPARATIONS 77,8)
      gouttiere: 20,     // padding 10 px de part et d'autre (.evolution-workspace)
    };
    /** Largeur offerte au cadre à 1 536 px : mesurée, barre latérale de 260 px,
     *  gouttières du plan de travail et colonne 13fr de .cost-two-col comprises. */
    const BUDGET = 780;

    const largeur = (t: string, entete = false): number => {
      let w = 0;
      for (const c of t) {
        if (c >= '0' && c <= '9') w += PX.chiffre;
        else if (c === ' ') w += PX.fine;
        else if (c === ',') w += PX.virgule;
        else if (c === '-' || c === '+') w += PX.signe;
        else if (c === '€') w += PX.euro;
        else if (c === '%') w += PX.pourcent;
        else if (c === ' ') w += PX.espace;
        else w += entete ? PX.lettreEntete : PX.lettre;
      }
      return w;
    };

    /**
     * Largeur minimale d'une colonne. L'intitulé passe à la ligne (règle CSS
     * vérifiée plus bas) : c'est donc son MOT le plus long qui compte, pas
     * l'intitulé entier. La cellule, elle, ne se coupe jamais : la devise y est
     * collée par une espace fine insécable.
     */
    const largeurColonne = (entete: string, cellules: string[]): number =>
      PX.gouttiere + Math.max(
        ...entete.split(/\s+/).filter(Boolean).map(mot => largeur(mot, true)),
        ...cellules.map(c => largeur(c)));

    /** Colonnes du récapitulatif qui tiennent ENTIÈREMENT dans le cadre. */
    const colonnesVisibles = (): { visibles: number; total: number; exigee: number } => {
      const cadre = fixture.nativeElement.querySelector('.evolution-recap-wrapper');
      const entetes = [...cadre.querySelectorAll('thead th')] as HTMLElement[];
      const lignes = [...cadre.querySelectorAll('tbody tr, tfoot tr')] as HTMLElement[];
      let cumul = 0, visibles = 0;
      entetes.forEach((th, i) => {
        const cellules = lignes.map(tr => (tr.children[i]?.textContent || '').trim());
        cumul += largeurColonne((th.textContent || '').trim(), cellules);
        if (cumul <= BUDGET) visibles++;
      });
      return { visibles, total: entetes.length, exigee: Math.round(cumul) };
    };

    /** Douze mois de coûts, avec ou sans avoirs, à l'échelle demandée. */
    const afficherEvolution = (devise: 'EUR' | 'TND' | 'DZD', avoirs: boolean, echelle = 1) => {
      TestBed.inject(UserPreferencesService).update({ currency: devise });
      const noms = ['Janvier 2026', 'Février 2026', 'Mars 2026', 'Avril 2026', 'Mai 2026', 'Juin 2026',
        'Juillet 2026', 'Août 2026', 'Septembre 2026', 'Octobre 2026', 'Novembre 2026', 'Décembre 2026'];
      const months = noms.map((monthName, i) => {
        const fuelCost = (18420.55 + i * 1311.4) * echelle;
        const maintenanceCost = (6930.2 + i * 540.7) * echelle;
        const repairCost = (9480.9 + i * 812.3) * echelle;
        const otherCost = (3412.1 + i * 296.5) * echelle;
        const creditAmount = avoirs ? -(1305.4 + i * 171.2) * echelle : 0;
        return {
          year: 2026, month: i + 1, monthName, isPartial: false,
          fuelCost, maintenanceCost, repairCost, otherCost, creditAmount,
          totalCost: fuelCost + maintenanceCost + repairCost + otherCost + creditAmount,
          variationPct: i === 0 ? null : 7.6,
        };
      });
      const somme = (c: string) => months.reduce((s, m: any) => s + m[c], 0);
      composant.selectedTemplate = composant.templates.find(t => t.type === 'cost-evolution')!;
      composant.costEvolution = {
        vehicleName: 'Toute la flotte', plate: '', months,
        totalFuelCost: somme('fuelCost'), totalMaintenanceCost: somme('maintenanceCost'),
        totalRepairCost: somme('repairCost'), totalOtherCost: somme('otherCost'),
        totalCreditAmount: somme('creditAmount'), totalCost: somme('totalCost'),
        totalDistanceKm: 264006, averageMonthlyCost: somme('totalCost') / 12,
        highestMonth: months[11], lowestMonth: months[0],
      } as any;
      composant.reportGenerated = true;
      composant.loading = false;
      fixture.detectChanges();
    };

    it('garde ses SEPT colonnes en dinar — c\'est « Total » et « Var. / mois préc. » qui tombaient', () => {
      afficherEvolution('TND', false);
      const m = colonnesVisibles();
      expect(m.total).toBe(7);
      expect(m.visibles).toBe(7);
      expect(m.exigee).toBeLessThanOrEqual(780);
    });

    it('garde ses HUIT colonnes en dinar dès qu\'un avoir existe', () => {
      afficherEvolution('TND', true);
      const m = colonnesVisibles();
      expect(m.total).toBe(8);
      expect(m.visibles).toBe(8);
    });

    it('garde ses HUIT colonnes en dinar algérien, avoirs et ligne de TOTAL à sept chiffres', () => {
      afficherEvolution('DZD', true, 2);
      const m = colonnesVisibles();
      expect(m.total).toBe(8);
      expect(m.visibles).toBe(8);
    });

    it('garde ses colonnes en euro, avec et sans avoir', () => {
      afficherEvolution('EUR', false);
      expect(colonnesVisibles()).toMatchObject({ visibles: 7, total: 7 });
      afficherEvolution('EUR', true);
      expect(colonnesVisibles()).toMatchObject({ visibles: 8, total: 8 });
    });

    /** Le motif du dépôt (mfMontant / mfDeviseEntete) : montant NU dans la
     *  cellule, code devise dans l'intitulé. C'est lui qui rend la place. */
    it('porte le code devise dans l\'INTITULÉ, jamais dans la cellule', () => {
      afficherEvolution('TND', true);
      const cadre = fixture.nativeElement.querySelector('.evolution-recap-wrapper');
      const entetes = [...cadre.querySelectorAll('thead th')].map((t: any) => t.textContent.trim());
      expect(entetes).toEqual(['Mois', 'Carburant TND', 'Entretiens TND', 'Réparations TND',
        'Autres TND', 'Avoirs TND', 'Total TND', 'Var. / mois préc.']);
      const montants = [...cadre.querySelectorAll('tbody td.num')].map((t: any) => t.textContent.trim());
      expect(montants.length).toBeGreaterThan(0);
      for (const v of montants) expect(v).not.toContain('TND');
    });

    it('garde « € » dans la cellule : un symbole d\'un signe ne coûte pas une colonne', () => {
      afficherEvolution('EUR', false);
      const cadre = fixture.nativeElement.querySelector('.evolution-recap-wrapper');
      expect([...cadre.querySelectorAll('thead th')].map((t: any) => t.textContent.trim()))
        .toEqual(['Mois', 'Carburant', 'Entretiens', 'Réparations', 'Autres', 'Total', 'Var. / mois préc.']);
      expect((cadre.querySelector('tbody td.num') as HTMLElement).textContent).toContain('€');
    });

    /** Règle 3 : une valeur tient sur UNE ligne. Ni le montant ni la variation
     *  ne portent d'espace sécable — mais c'est la DONNÉE qui le garantit, pas
     *  un nowrap de feuille de style. */
    it('ne laisse aucune espace sécable dans le récapitulatif', () => {
      afficherEvolution('TND', true);
      const cadre = fixture.nativeElement.querySelector('.evolution-recap-wrapper');
      const valeurs = [...cadre.querySelectorAll('tbody td.num, tfoot td.num')]
        .map((t: any) => t.textContent.trim()).filter((v: string) => v !== '—');
      expect(valeurs.length).toBeGreaterThan(0);
      for (const v of valeurs) expect(v).not.toMatch(/[  ]/);
    });
  });

  // ───────────────── LOT F — les règles de feuille de style ─────────────────

  describe('aucun nowrap sur la famille des tableaux mensuels', () => {
    const css: string = require('fs').readFileSync(
      require('path').join(__dirname, 'reports.component.css'), 'utf8');

    /** Retire les commentaires : ils PARLENT du nowrap qu'on vient de retirer. */
    const sansCommentaires = css.replace(/\/\*[\s\S]*?\*\//g, '');

    it('ne pose PAS de nowrap sur .monthly-cost-table tbody td.num — sept tableaux en dépendent', () => {
      const bloc = sansCommentaires.match(/\.monthly-cost-table tbody td\.num \{[^}]*\}/);
      expect(bloc).toBeTruthy();
      expect(bloc![0]).not.toContain('nowrap');
    });

    it('ne pose PAS de nowrap sur la ligne de TOTAL, la plus large du tableau', () => {
      const bloc = sansCommentaires.match(/\.monthly-cost-table tfoot \.dept-total-row td\.num \{[^}]*\}/);
      expect(bloc).toBeTruthy();
      expect(bloc![0]).not.toContain('nowrap');
    });

    it('laisse les intitulés du récapitulatif passer à la ligne, à TOUTE largeur', () => {
      // Tenus sur une seule ligne, six intitulés portant « TND » réclamaient
      // 622 px des 786 offerts : « Total » et « Var. / mois préc. » sortaient
      // d'un cadre en overflow-x: hidden. La règle ne doit donc plus être
      // enfermée dans une requête de média.
      const i = sansCommentaires.indexOf('.evolution-workspace .evolution-recap-wrapper thead th { white-space: normal; }');
      expect(i).toBeGreaterThan(0);
      const avant = sansCommentaires.slice(0, i);
      const accolades = (avant.match(/\{/g) || []).length - (avant.match(/\}/g) || []).length;
      expect(accolades).toBe(0); // aucune requête de média ouverte au-dessus
    });

    it('garde le tableau GÉNÉRIQUE servi par .data-table td.num, qui ne bouge pas', () => {
      const bloc = sansCommentaires.match(/\.data-table td\.num \{[^}]*\}/);
      expect(bloc).toBeTruthy();
      expect(bloc![0]).toContain('white-space: nowrap');
    });
  });
});
