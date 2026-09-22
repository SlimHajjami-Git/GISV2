import {
  aideSaisieCredit, creditDepuisFiche, equivalenceScans, parseScanCreditInput, saisieInitialeCredit,
  SCAN_CREDIT_DEFAULT, SCAN_CREDIT_INVALID_MESSAGE, SCAN_CREDIT_MAX, SCAN_CREDIT_SEPARATOR_MESSAGE
} from './scan-quota.helpers';

/**
 * Fiche société admin — crédit IA mensuel du scan de factures, en jetons (22/09/2026 ;
 * avant : un nombre de scans). Les deux pièges de la recette du 17/09/2026 (« Enregistrer
 * ne marche pas ») restent verrouillés ici : ngModel type="number" fournit un NOMBRE, et
 * 0 (désactiver) ne doit jamais devenir « vide = défaut ». Troisième piège (22/09/2026) :
 * « 150 000 » ou « 150.000 » dans un champ number arrivaient null ou 150 — le champ est
 * devenu texte, le point et la virgule sont refusés.
 */
describe('parseScanCreditInput (crédit IA mensuel, en jetons)', () => {
  describe('bug 1 — [(ngModel)] sur type="number" fournit un NOMBRE, pas une chaîne', () => {
    it("accepte un nombre tapé par l'administrateur (l'ancien code appelait .trim() dessus et plantait)", () => {
      expect(parseScanCreditInput(90000)).toEqual({ ok: true, tokens: 90000 });
    });

    it('accepte aussi la chaîne initiale chargée depuis la fiche', () => {
      expect(parseScanCreditInput('60000')).toEqual({ ok: true, tokens: 60000 });
    });

    it('vide ⇒ null (défaut plateforme), que le champ soit une chaîne vide ou null (champ effacé)', () => {
      expect(parseScanCreditInput('')).toEqual({ ok: true, tokens: null });
      expect(parseScanCreditInput('   ')).toEqual({ ok: true, tokens: null });
      expect(parseScanCreditInput(null)).toEqual({ ok: true, tokens: null });
      expect(parseScanCreditInput(undefined)).toEqual({ ok: true, tokens: null });
    });
  });

  describe('bug 2 — 0 = désactiver, et non « vide = défaut »', () => {
    it('0 (nombre) est conservé tel quel', () => {
      expect(parseScanCreditInput(0)).toEqual({ ok: true, tokens: 0 });
    });

    it("'0' (chaîne) est conservé tel quel", () => {
      expect(parseScanCreditInput('0')).toEqual({ ok: true, tokens: 0 });
    });
  });

  describe('bornes et valeurs invalides', () => {
    it('un NOMBRE à décimales est tronqué vers le bas (pas d’ambiguïté de séparateur)', () => {
      expect(parseScanCreditInput(60000.7)).toEqual({ ok: true, tokens: 60000 });
    });

    it('bug 3 — « 150.000 » ou « 150,000 » : refusé avec un message, jamais lu comme 150 jetons', () => {
      expect(parseScanCreditInput('150.000')).toEqual({ ok: false, message: SCAN_CREDIT_SEPARATOR_MESSAGE });
      expect(parseScanCreditInput('150,000')).toEqual({ ok: false, message: SCAN_CREDIT_SEPARATOR_MESSAGE });
      expect(parseScanCreditInput('1500.9')).toEqual({ ok: false, message: SCAN_CREDIT_SEPARATOR_MESSAGE });
    });

    it('chaîne : chiffres seulement (ni signe, ni notation « 1e5 »), espaces permises', () => {
      expect(parseScanCreditInput('-5')).toEqual({ ok: false, message: SCAN_CREDIT_INVALID_MESSAGE });
      expect(parseScanCreditInput('+5')).toEqual({ ok: false, message: SCAN_CREDIT_INVALID_MESSAGE });
      expect(parseScanCreditInput('1e5')).toEqual({ ok: false, message: SCAN_CREDIT_INVALID_MESSAGE });
      expect(parseScanCreditInput('10000001')).toEqual({ ok: false, message: SCAN_CREDIT_INVALID_MESSAGE });
      expect(parseScanCreditInput('150 000')).toEqual({ ok: true, tokens: 150000 });
      expect(parseScanCreditInput('150 000')).toEqual({ ok: true, tokens: 150000 });
      expect(parseScanCreditInput('10 000 000')).toEqual({ ok: true, tokens: SCAN_CREDIT_MAX });
    });

    it('accepte les séparateurs de milliers dans une chaîne (« 60 000 »)', () => {
      expect(parseScanCreditInput('60 000')).toEqual({ ok: true, tokens: 60000 });
      expect(parseScanCreditInput('1 200 000')).toEqual({ ok: true, tokens: 1200000 });
    });

    it('accepte exactement 10 000 000 et refuse au-delà', () => {
      expect(SCAN_CREDIT_MAX).toBe(10_000_000);
      expect(parseScanCreditInput(SCAN_CREDIT_MAX)).toEqual({ ok: true, tokens: SCAN_CREDIT_MAX });
      expect(parseScanCreditInput(SCAN_CREDIT_MAX + 1)).toEqual({ ok: false, message: SCAN_CREDIT_INVALID_MESSAGE });
    });

    it('refuse les négatifs et ce qui n’est pas un nombre fini', () => {
      expect(parseScanCreditInput(-1)).toEqual({ ok: false, message: SCAN_CREDIT_INVALID_MESSAGE });
      expect(parseScanCreditInput('abc')).toEqual({ ok: false, message: SCAN_CREDIT_INVALID_MESSAGE });
      expect(parseScanCreditInput('Infinity')).toEqual({ ok: false, message: SCAN_CREDIT_INVALID_MESSAGE });
      expect(parseScanCreditInput(NaN)).toEqual({ ok: false, message: SCAN_CREDIT_INVALID_MESSAGE });
    });
  });
});

describe('équivalence en scans sous le champ', () => {
  it('vide = défaut 60 000 jetons ≈ 20 scans', () => {
    expect(SCAN_CREDIT_DEFAULT).toBe(60000);
    expect(equivalenceScans(null)).toBe('≈ 20 scans par mois (défaut : 60 000 jetons)');
  });

  it('un crédit réglé : scans moyens de 3 000 jetons, arrondis vers le bas', () => {
    expect(equivalenceScans(150000)).toBe('≈ 50 scans par mois');
    expect(equivalenceScans(4000)).toBe('≈ 1 scan par mois');
    expect(equivalenceScans(1000)).toBe("≈ moins d'un scan par mois");
  });

  it('0 = fonction désactivée', () => {
    expect(equivalenceScans(0)).toBe('IA désactivée pour cette société (scans, assistant, rapports IA)');
  });

  it('suit la saisie en cours, ou affiche le motif du refus', () => {
    expect(aideSaisieCredit(90000)).toBe('≈ 30 scans par mois');
    expect(aideSaisieCredit('')).toBe('≈ 20 scans par mois (défaut : 60 000 jetons)');
    expect(aideSaisieCredit(-5)).toBe(SCAN_CREDIT_INVALID_MESSAGE);
  });
});

describe('fiche société → barre et champ', () => {
  const fiche = {
    invoiceScanMonthlyTokens: null,
    invoiceScanMonthlyLimit: 50,
    invoiceScanBudgetTokens: 150000,
    invoiceScanUsedTokens: 45000,
    invoiceScanPercentUsed: 30,
    invoiceScanUsedThisMonth: 15,
    invoiceScanResetsAt: '2026-10-01T00:00:00Z'
  };

  it('crédit du mois pour la barre (budget effectif, consommation, recharge)', () => {
    expect(creditDepuisFiche(fiche)).toEqual({
      enabled: true, budgetTokens: 150000, usedTokens: 45000, remainingTokens: 105000,
      percentUsed: 30, scansThisMonth: 15, estimatedScansLeft: 35, resetsAt: '2026-10-01T00:00:00Z'
    });
  });

  it('budget nul : fonction fermée', () => {
    const c = creditDepuisFiche({ ...fiche, invoiceScanBudgetTokens: 0, invoiceScanUsedTokens: 0, invoiceScanPercentUsed: 100 })!;
    expect(c.enabled).toBe(false);
    expect(c.remainingTokens).toBe(0);
  });

  it('API sans ces champs : pas de barre plutôt qu’une barre fausse', () => {
    expect(creditDepuisFiche({ invoiceScanMonthlyLimit: 20, invoiceScanUsedThisMonth: 3 })).toBeNull();
    expect(creditDepuisFiche(null)).toBeNull();
  });

  it('champ initial : le crédit réglé en jetons', () => {
    expect(saisieInitialeCredit({ ...fiche, invoiceScanMonthlyTokens: 90000 })).toBe('90000');
    expect(saisieInitialeCredit({ ...fiche, invoiceScanMonthlyTokens: 0 })).toBe('0');
  });

  it('champ initial d’une société sur l’ancien quota : le budget converti, pour qu’Enregistrer ne le perde pas', () => {
    expect(saisieInitialeCredit(fiche)).toBe('150000');
  });

  it('champ initial sans aucun réglage : vide (défaut plateforme)', () => {
    expect(saisieInitialeCredit({ ...fiche, invoiceScanMonthlyLimit: null, invoiceScanBudgetTokens: 60000 })).toBe('');
    expect(saisieInitialeCredit(undefined)).toBe('');
  });
});
