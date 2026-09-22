import {
  CreditIa, creditBloque, creditDepuisReponse, creditEpuise, creditPerime, DELAI_MAX_MINUTERIE, delaiAvantRecharge, formatJetons,
  infobulleCredit, libelleRecharge, libelleScansRestants, lireCreditIa, lireRefusCreditIa, MARGE_APRES_RECHARGE_MS,
  messageCreditBloque, niveauCredit, ventilationCredit
} from './credit-ia.helpers';

/**
 * Crédit IA mensuel de la société (22/09/2026) : ce que la barre « Crédit IA » affiche,
 * avec quelle couleur et quelle infobulle — partagé par les cinq écrans du bouton
 * « Scanner une facture », l'assistant IA, le rapport IA flotte et la fiche société de
 * l'admin. Le crédit couvre TOUTE l'IA de la société (« le quota inclut l'utilisation de l'IA »).
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
    expect(infobulleCredit(epuise, apres)).toBe("Le crédit IA s'est rechargé le 1er octobre : vous pouvez de nouveau utiliser l'IA.");
    expect(infobulleCredit(credit(), apres)).toBe("Le crédit IA s'est rechargé le 1er octobre : vous pouvez de nouveau utiliser l'IA.");
    expect(infobulleCredit(epuise, apres, { scans: true })).toBe("Le crédit IA s'est rechargé le 1er octobre : vous pouvez de nouveau scanner.");
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
  it('texte demandé, générique (toute l’IA) : part utilisée, jetons, date de recharge', () => {
    expect(infobulleCredit(credit())).toBe(
      '42 % du crédit IA gratuit du mois utilisé (25 200 / 60 000 jetons). Se recharge le 1er octobre.'
    );
  });

  it('à côté du bouton de scan seulement : « environ N scans restants » en plus', () => {
    expect(infobulleCredit(credit(), undefined, { scans: true })).toBe(
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
      .toBe("Les fonctions d'IA ne sont pas activées pour votre société.");
  });
});

describe('toute l’IA : ventilation, réponses et refus du serveur', () => {
  it('lireCreditIa reprend la ventilation par fonction (entiers positifs)', () => {
    const c = lireCreditIa({ ...credit(), byFeature: { invoice_scan: 3000, assistant_chat: 22200.7, fleet_report: -4, bidon: 'x' } })!;
    expect(c.byFeature).toEqual({ invoice_scan: 3000, assistant_chat: 22200, fleet_report: 0 });
    expect(lireCreditIa(credit())!.byFeature).toBeUndefined();
  });

  it('creditDepuisReponse : « credit » des réponses d’IA, « quota » de l’ancien scan, sinon null', () => {
    expect(creditDepuisReponse({ answer: 'ok', credit: credit({ usedTokens: 30000 }) })!.usedTokens).toBe(30000);
    expect(creditDepuisReponse({ quota: credit({ usedTokens: 31000 }) })!.usedTokens).toBe(31000);
    expect(creditDepuisReponse({ answer: 'ok' })).toBeNull();
    expect(creditDepuisReponse(null)).toBeNull();
  });

  it('lireRefusCreditIa : 429 AI_CREDIT_EXHAUSTED avec le message du serveur et le crédit joint', () => {
    const message = "Crédit IA du mois épuisé (100 %). Il se recharge le 01/10/2026 ; votre administrateur peut l'augmenter.";
    const refus = lireRefusCreditIa({ status: 429, error: { code: 'AI_CREDIT_EXHAUSTED', message, credit: credit({ remainingTokens: 0, percentUsed: 100 }) } })!;

    expect(refus.code).toBe('AI_CREDIT_EXHAUSTED');
    expect(refus.message).toBe(message);
    expect(refus.credit!.percentUsed).toBe(100);
  });

  it('lireRefusCreditIa : 403 AI_CREDIT_DISABLED, message de repli si le serveur n’en donne pas', () => {
    const refus = lireRefusCreditIa({ status: 403, error: { code: 'AI_CREDIT_DISABLED' } })!;
    expect(refus.message).toBe("Les fonctions d'IA ne sont pas activées pour votre société.");
    expect(refus.credit).toBeNull();
  });

  it('lireRefusCreditIa : toute autre erreur n’est pas un refus de crédit', () => {
    expect(lireRefusCreditIa({ status: 503, error: { message: 'Groq indisponible' } })).toBeNull();
    expect(lireRefusCreditIa({ status: 403, error: { code: 'USER_PERMISSION_DENIED', message: 'Vous n\'avez pas accès à ce module' } })).toBeNull();
    expect(lireRefusCreditIa({ status: 0 })).toBeNull();
    expect(lireRefusCreditIa(null)).toBeNull();
  });

  it('messageCreditBloque : pourquoi un bouton d’IA est grisé, rien sinon', () => {
    const maintenant = Date.parse('2026-09-22T10:00:00Z');
    expect(messageCreditBloque(credit({ remainingTokens: 0, percentUsed: 100 }), maintenant))
      .toBe("Crédit IA du mois épuisé (100 %) — il se recharge le 1er octobre. Votre administrateur peut l'augmenter.");
    expect(messageCreditBloque(credit({ enabled: false, budgetTokens: 0, remainingTokens: 0 }), maintenant))
      .toBe("Les fonctions d'IA ne sont pas activées pour votre société.");
    expect(messageCreditBloque(credit(), maintenant)).toBe('');
    expect(messageCreditBloque(null, maintenant)).toBe('');
    // Passé la date de recharge, un crédit épuisé ne bloque plus (le serveur l'a remis à zéro).
    expect(messageCreditBloque(credit({ remainingTokens: 0, percentUsed: 100 }), Date.parse('2026-10-01T08:00:00Z'))).toBe('');
  });

  it('ventilationCredit : libellés français, plus gros consommateur d’abord, fonctions à 0 écartées', () => {
    const lignes = ventilationCredit(credit({
      usedTokens: 30000,
      byFeature: { invoice_scan: 6000, assistant_chat: 18000, fleet_report: 6000, consumption_explain: 0, fonction_future: 0 }
    }));

    expect(lignes).toEqual([
      { cle: 'assistant_chat', libelle: 'Assistant IA', jetons: 18000, part: 60 },
      { cle: 'invoice_scan', libelle: 'Scans de factures', jetons: 6000, part: 20 },
      { cle: 'fleet_report', libelle: 'Rapports IA flotte', jetons: 6000, part: 20 }
    ]);
    expect(ventilationCredit(credit())).toEqual([]);
    expect(ventilationCredit(null)).toEqual([]);
    expect(ventilationCredit(credit({ byFeature: { nouvelle_fonction: 500 } }))[0].libelle).toBe('nouvelle_fonction');
  });
});
