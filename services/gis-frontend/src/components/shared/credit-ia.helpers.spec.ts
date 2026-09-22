import {
  CreditIa, creditBloque, creditEpuise, creditPerime, DELAI_MAX_MINUTERIE, delaiAvantRecharge, formatJetons,
  infobulleCredit, libelleRecharge, libelleScansRestants, lireCreditIa, MARGE_APRES_RECHARGE_MS, niveauCredit
} from './credit-ia.helpers';

/**
 * Crédit IA mensuel du scan de factures (22/09/2026) : ce que la barre « Crédit IA »
 * affiche, avec quelle couleur et quelle infobulle — partagé par les cinq écrans du
 * bouton « Scanner une facture » et par la fiche société de l'admin.
 */
const credit = (patch: Partial<CreditIa> = {}): CreditIa => ({
  enabled: true, budgetTokens: 60000, usedTokens: 25200, remainingTokens: 34800,
  percentUsed: 42, scansThisMonth: 9, estimatedScansLeft: 11, resetsAt: '2026-10-01T00:00:00Z',
  ...patch
});

describe('lireCreditIa — réponse du serveur', () => {
  it('reprend le contrat de l’API tel quel', () => {
    expect(lireCreditIa(credit())).toEqual(credit());
  });

  it('ancienne réponse { used, limit, remaining } (API pas redéployée) : null, pas de barre fausse', () => {
    expect(lireCreditIa({ used: 3, limit: 20, remaining: 17, resetsAt: '2026-10-01T00:00:00Z' })).toBeNull();
  });

  it('réponse absente ou illisible : null', () => {
    expect(lireCreditIa(null)).toBeNull();
    expect(lireCreditIa(undefined)).toBeNull();
    expect(lireCreditIa('42')).toBeNull();
    expect(lireCreditIa({ budgetTokens: 'beaucoup', usedTokens: 3 })).toBeNull();
  });

  it('pourcentage borné entre 0 et 100', () => {
    expect(lireCreditIa(credit({ percentUsed: 140 }))!.percentUsed).toBe(100);
    expect(lireCreditIa(credit({ percentUsed: -3 }))!.percentUsed).toBe(0);
  });

  it('champs dérivés absents : recalculés comme le serveur (arrondi bas)', () => {
    const c = lireCreditIa({ budgetTokens: 60000, usedTokens: 59999 })!;
    expect(c.enabled).toBe(true);
    expect(c.remainingTokens).toBe(1);
    expect(c.percentUsed).toBe(99);
    expect(c.estimatedScansLeft).toBe(0);
    expect(c.scansThisMonth).toBe(0);
    expect(c.resetsAt).toBeUndefined();
  });
});

describe('niveauCredit — couleur de la barre', () => {
  it('vert sous 70 %', () => {
    expect(niveauCredit(0)).toBe('ok');
    expect(niveauCredit(69)).toBe('ok');
  });

  it('orange de 70 à 89 %', () => {
    expect(niveauCredit(70)).toBe('alerte');
    expect(niveauCredit(89)).toBe('alerte');
  });

  it('rouge à partir de 90 %', () => {
    expect(niveauCredit(90)).toBe('critique');
    expect(niveauCredit(100)).toBe('critique');
  });
});

describe('creditEpuise — bouton grisé', () => {
  it('épuisé quand il ne reste plus de jeton', () => {
    expect(creditEpuise(credit({ remainingTokens: 0, percentUsed: 100 }))).toBe(true);
  });

  it('fonction désactivée : grisé aussi', () => {
    expect(creditEpuise(credit({ enabled: false, budgetTokens: 0, remainingTokens: 0 }))).toBe(true);
  });

  it('il reste un jeton : pas épuisé (le serveur accepte encore un scan)', () => {
    expect(creditEpuise(credit({ remainingTokens: 1, percentUsed: 99 }))).toBe(false);
  });
});

describe('changement de mois sur un écran resté ouvert', () => {
  const avant = Date.parse('2026-09-30T18:00:00Z');
  const apres = Date.parse('2026-10-01T08:00:00Z');
  const epuise = credit({ usedTokens: 60000, remainingTokens: 0, percentUsed: 100, estimatedScansLeft: 0 });

  it('creditPerime : vrai dès la date de recharge, faux avant ou sans date lisible', () => {
    expect(creditPerime(epuise, avant)).toBe(false);
    expect(creditPerime(epuise, Date.parse('2026-10-01T00:00:00Z'))).toBe(true);
    expect(creditPerime(epuise, apres)).toBe(true);
    expect(creditPerime(credit({ resetsAt: undefined }), apres)).toBe(false);
    expect(creditPerime(credit({ resetsAt: 'pas une date' }), apres)).toBe(false);
  });

  it('creditBloque : un crédit épuisé ne grise plus le bouton passé la date de recharge', () => {
    expect(creditBloque(epuise, avant)).toBe(true);
    expect(creditBloque(epuise, apres)).toBe(false);
    expect(creditBloque(credit(), avant)).toBe(false);
  });

  it('creditBloque : une fonction désactivée reste grisée, même au changement de mois', () => {
    const ferme = credit({ enabled: false, budgetTokens: 0, usedTokens: 0, remainingTokens: 0, percentUsed: 100 });
    expect(creditBloque(ferme, avant)).toBe(true);
    expect(creditBloque(ferme, apres)).toBe(true);
  });

  it('delaiAvantRecharge : jusqu’à minuit UTC plus la marge, rien si la date est passée ou la fonction fermée', () => {
    const a2350 = Date.parse('2026-09-30T23:50:00Z');
    expect(delaiAvantRecharge(epuise, a2350)).toBe(10 * 60_000 + MARGE_APRES_RECHARGE_MS);
    expect(delaiAvantRecharge(epuise, apres)).toBeNull();
    expect(delaiAvantRecharge(credit({ enabled: false }), a2350)).toBeNull();
    expect(delaiAvantRecharge(credit({ resetsAt: undefined }), a2350)).toBeNull();
    expect(delaiAvantRecharge(null, a2350)).toBeNull();
  });

  it('delaiAvantRecharge : borné à 2^31-1 ms (au-delà, setTimeout se déclencherait aussitôt)', () => {
    expect(DELAI_MAX_MINUTERIE).toBe(2 ** 31 - 1);
    expect(delaiAvantRecharge(epuise, Date.parse('2026-09-02T00:00:00Z'))).toBe(DELAI_MAX_MINUTERIE);
  });

  it('infobulle : ne promet plus une recharge « le 1er octobre » le 1er octobre après minuit UTC', () => {
    expect(infobulleCredit(epuise, avant)).toBe(
      "Crédit IA du mois épuisé (100 %) — il se recharge le 1er octobre. Votre administrateur peut l'augmenter."
    );
    expect(infobulleCredit(epuise, apres)).toBe("Le crédit IA s'est rechargé le 1er octobre : vous pouvez de nouveau scanner.");
    expect(infobulleCredit(credit(), apres)).toBe("Le crédit IA s'est rechargé le 1er octobre : vous pouvez de nouveau scanner.");
  });
});

describe('libellés', () => {
  it('jetons groupés par milliers, quelle que soit la locale du navigateur', () => {
    expect(formatJetons(25200)).toBe('25 200');
    expect(formatJetons(60000)).toBe('60 000');
    expect(formatJetons(1234567)).toBe('1 234 567');
    expect(formatJetons(999)).toBe('999');
    expect(formatJetons(0)).toBe('0');
    expect(formatJetons(NaN)).toBe('0');
  });

  it('date de recharge lue en UTC : « le 1er octobre », « le 15 octobre », repli sans date', () => {
    expect(libelleRecharge('2026-10-01T00:00:00Z')).toBe('le 1er octobre');
    expect(libelleRecharge('2026-10-15T00:00:00Z')).toBe('le 15 octobre');
    expect(libelleRecharge(undefined)).toBe('le 1er du mois prochain');
    expect(libelleRecharge('pas une date')).toBe('le 1er du mois prochain');
  });

  it('scans restants : pluriel, singulier, moins d’un scan, aucun', () => {
    expect(libelleScansRestants(credit())).toBe('environ 11 scans restants');
    expect(libelleScansRestants(credit({ estimatedScansLeft: 1, remainingTokens: 3500 }))).toBe('environ 1 scan restant');
    expect(libelleScansRestants(credit({ estimatedScansLeft: 0, remainingTokens: 800 }))).toBe("moins d'un scan restant");
    expect(libelleScansRestants(credit({ estimatedScansLeft: 0, remainingTokens: 0 }))).toBe('aucun scan restant');
  });
});

describe('infobulleCredit', () => {
  it('texte demandé : part utilisée, jetons, scans restants, date de recharge', () => {
    expect(infobulleCredit(credit())).toBe(
      '42 % du crédit IA gratuit du mois utilisé (25 200 / 60 000 jetons, environ 11 scans restants). Se recharge le 1er octobre.'
    );
  });

  it('crédit épuisé : le dit et donne la date de recharge', () => {
    expect(infobulleCredit(credit({ usedTokens: 60000, remainingTokens: 0, percentUsed: 100, estimatedScansLeft: 0 }))).toBe(
      "Crédit IA du mois épuisé (100 %) — il se recharge le 1er octobre. Votre administrateur peut l'augmenter."
    );
  });

  it('fonction désactivée : le dit, sans parler d’épuisement', () => {
    expect(infobulleCredit(credit({ enabled: false, budgetTokens: 0, usedTokens: 0, remainingTokens: 0, percentUsed: 100 })))
      .toBe("Le scan de factures IA n'est pas activé pour votre société.");
  });
});
