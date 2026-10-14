import { UserPreferencesService } from './user-preferences.service';
import { AppNumberPipe } from '../pipes/user-preference-pipes';

/** Le pipe `number` d'Angular (locale en-US) écrivait « 161,980 » km dans Carburant. */
describe('AppNumberPipe', () => {
  const pipe = new AppNumberPipe();

  it('sépare les milliers par une espace et les décimales par une virgule', () => {
    expect(pipe.transform(161980)).toBe('161 980');
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
    expect(prefs.formatCurrency(1.98, prefs.unitPriceDigits)).toBe('1,98 €');
    prefs.update({ currency: 'DZD' as any });
    expect(prefs.unitPriceDigits).toBe(2);
  });
});
