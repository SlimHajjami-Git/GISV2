import { parseScanQuotaInput, SCAN_QUOTA_INVALID_MESSAGE, SCAN_QUOTA_MAX } from './scan-quota.helpers';

/**
 * Recette du 17/09/2026 — « Scan de factures (IA) : Enregistrer ne marche pas ».
 * En prod, aucune des 17 sociétés n'avait de limite enregistrée : la saisie ne
 * partait jamais. Deux causes, verrouillées ici.
 */
describe('parseScanQuotaInput (quota mensuel de scans IA)', () => {
  describe('bug 1 — [(ngModel)] sur type="number" fournit un NOMBRE, pas une chaîne', () => {
    it("accepte un nombre tapé par l'administrateur (l'ancien code appelait .trim() dessus et plantait)", () => {
      expect(parseScanQuotaInput(50)).toEqual({ ok: true, limit: 50 });
    });

    it('accepte aussi la chaîne initiale chargée depuis la fiche', () => {
      expect(parseScanQuotaInput('20')).toEqual({ ok: true, limit: 20 });
    });

    it('vide ⇒ null (défaut plateforme), que le champ soit une chaîne vide ou null (champ effacé)', () => {
      expect(parseScanQuotaInput('')).toEqual({ ok: true, limit: null });
      expect(parseScanQuotaInput('   ')).toEqual({ ok: true, limit: null });
      expect(parseScanQuotaInput(null)).toEqual({ ok: true, limit: null });
      expect(parseScanQuotaInput(undefined)).toEqual({ ok: true, limit: null });
    });
  });

  describe('bug 2 — 0 = désactiver, et non « vide = défaut »', () => {
    it('0 (nombre) est conservé tel quel', () => {
      expect(parseScanQuotaInput(0)).toEqual({ ok: true, limit: 0 });
    });

    it("'0' (chaîne) est conservé tel quel", () => {
      expect(parseScanQuotaInput('0')).toEqual({ ok: true, limit: 0 });
    });
  });

  describe('bornes et valeurs invalides', () => {
    it('tronque les décimales vers le bas', () => {
      expect(parseScanQuotaInput(20.7)).toEqual({ ok: true, limit: 20 });
      expect(parseScanQuotaInput('12.9')).toEqual({ ok: true, limit: 12 });
    });

    it('accepte exactement le maximum et refuse au-delà', () => {
      expect(parseScanQuotaInput(SCAN_QUOTA_MAX)).toEqual({ ok: true, limit: SCAN_QUOTA_MAX });
      expect(parseScanQuotaInput(SCAN_QUOTA_MAX + 1)).toEqual({ ok: false, message: SCAN_QUOTA_INVALID_MESSAGE });
    });

    it('refuse les négatifs et ce qui n’est pas un nombre fini', () => {
      expect(parseScanQuotaInput(-1)).toEqual({ ok: false, message: SCAN_QUOTA_INVALID_MESSAGE });
      expect(parseScanQuotaInput('abc')).toEqual({ ok: false, message: SCAN_QUOTA_INVALID_MESSAGE });
      expect(parseScanQuotaInput('Infinity')).toEqual({ ok: false, message: SCAN_QUOTA_INVALID_MESSAGE });
      expect(parseScanQuotaInput(NaN)).toEqual({ ok: false, message: SCAN_QUOTA_INVALID_MESSAGE });
    });
  });
});
