import { UserPreferencesService } from './user-preferences.service';
import { AppNumberPipe, nombreFr, lireNombreFr } from '../pipes/user-preference-pipes';
import { ReportExportService } from './report-export.service';

/** Textes des rapports : « 12,5 km » au lieu de « 12.5 km », et relus correctement
 *  par le tri des colonnes et par l'export Excel (recette du 14/10/2026). */
describe('nombreFr / lireNombreFr', () => {
  it('écrit au format français', () => {
    expect(nombreFr(1234.56, 1)).toBe('1\u202F234,6');
    expect(nombreFr(NaN, 1)).toBe('-');
  });

  it('relit le format français comme l’ancien format anglais', () => {
    expect(lireNombreFr('1\u202F234,5 km')).toBe(1234.5);
    expect(lireNombreFr('12,5 km')).toBe(12.5);
    expect(lireNombreFr('12.5 km')).toBe(12.5);
    expect(lireNombreFr(42)).toBe(42);
    expect(lireNombreFr('Toyota')).toBeNaN();
  });

  it("l'export Excel garde un nombre écrit au format français", () => {
    const service = new ReportExportService({} as any, {} as any);
    const cellule = (v: string) => (service as any).cellValue({}, 'v', { v });
    expect(cellule('1\u202F234,50')).toBe(1234.5);
    expect(cellule('12,5')).toBe(12.5);
    expect(cellule('12.5')).toBe(12.5);
    expect(cellule('00123')).toBe('00123');
    expect(cellule('12,5 km')).toBe('12,5 km');
  });
});

/** Le pipe `number` d'Angular (locale en-US) écrivait « 161,980 » km dans Carburant. */
describe('AppNumberPipe', () => {
  const pipe = new AppNumberPipe();

  it('sépare les milliers par une espace et les décimales par une virgule', () => {
    expect(pipe.transform(161980)).toBe('161\u202F980');
    expect(pipe.transform(36.11, 2)).toBe('36,11');
    expect(pipe.transform(6.86, 1)).toBe('6,9');
    expect(pipe.transform(null)).toBe('-');
  });
});

/**
 * Décimales d'un prix unitaire (prix au litre, coût au km) : le dinar se compte en
 * millimes, l'euro en centimes. Les 3 décimales s'affichaient partout, euros compris
 * (recette de Karim du 14/10/2026).
 */
describe('UserPreferencesService.unitPriceDigits', () => {
  let prefs: UserPreferencesService;

  beforeEach(() => {
    localStorage.clear();
    prefs = new UserPreferencesService();
  });

  it('garde 3 décimales en dinar tunisien, ancien code « DT » compris', () => {
    prefs.update({ currency: 'TND' as any });
    expect(prefs.unitPriceDigits).toBe(3);
    prefs.update({ currency: 'DT' as any });
    expect(prefs.unitPriceDigits).toBe(3);
  });

  it('passe à 2 décimales en euro et dans les autres devises', () => {
    prefs.update({ currency: 'EUR' as any });
    expect(prefs.unitPriceDigits).toBe(2);
    expect(prefs.formatCurrency(1.98, prefs.unitPriceDigits)).toBe('1,98\u202F€');
    prefs.update({ currency: 'DZD' as any });
    expect(prefs.unitPriceDigits).toBe(2);
  });
});
