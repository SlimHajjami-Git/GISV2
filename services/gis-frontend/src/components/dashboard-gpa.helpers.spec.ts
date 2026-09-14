import {
  estRefusDeDroit, estSocieteSansGps, libelleMoisIncomplet, libellePerimetreParc, sousTitreAchats
} from './dashboard-gpa.helpers';

/**
 * Relecture de l'apport « tableau de bord GPA » et des rapports (14/09/2026).
 */
describe('dashboard-gpa.helpers', () => {
  describe('estSocieteSansGps (règle SOCIÉTÉ, jamais le droit de l’utilisateur)', () => {
    it('société dont l’abonnement exclut le suivi GPS : tableau de bord GPA', () => {
      expect(estSocieteSansGps({ moduleMonitoring: false })).toBe(true);
    });

    it('société équipée : tableau de bord GPS, même si l’employé n’a pas le droit Monitoring', () => {
      // Le droit par utilisateur n'entre pas dans la décision : seule la société compte.
      expect(estSocieteSansGps({ moduleMonitoring: true })).toBe(false);
    });

    it('abonnement inconnu (administrateur système, société sans plan) : pas GPA', () => {
      expect(estSocieteSansGps(null)).toBe(false);
      expect(estSocieteSansGps(undefined)).toBe(false);
      expect(estSocieteSansGps({})).toBe(false);
    });
  });

  describe('libellePerimetreParc', () => {
    it('un administrateur voit tout le parc', () => {
      expect(libellePerimetreParc(true, 12)).toBe('Tout le parc');
    });

    it('un non-admin ne voit que ses véhicules affectés : le libellé le dit', () => {
      expect(libellePerimetreParc(false, 1)).toBe('Mes véhicules (1)');
    });
  });

  describe('libelleMoisIncomplet', () => {
    const aujourdhui = new Date(2026, 8, 14);   // 14/09/2026

    it('mois calendaire courant : « en cours »', () => {
      expect(libelleMoisIncomplet(2026, 9, aujourdhui)).toBe('en cours');
    });

    it('premier mois d’une période qui commence le 15 : « incomplet », pas « en cours »', () => {
      expect(libelleMoisIncomplet(2026, 3, aujourdhui)).toBe('incomplet');
      expect(libelleMoisIncomplet(2025, 9, aujourdhui)).toBe('incomplet');
    });
  });

  describe('sousTitreAchats', () => {
    const fmt = (v: number) => `${v} TND`;

    it('donne la part de la période du coût d’achats (livraison du 11/09/2026)', () => {
      expect(sousTitreAchats({ periodCost: 50400 }, fmt)).toBe('dont 50400 TND sur la période');
      expect(sousTitreAchats({ periodCost: 0 }, fmt)).toBe('dont 0 TND sur la période');
    });

    it('bloc non autorisé ou pas encore chargé : rien', () => {
      expect(sousTitreAchats(null, fmt)).toBe('');
    });
  });

  describe('estRefusDeDroit', () => {
    it('un 403 est un refus, pas « aucun plein saisi »', () => {
      expect(estRefusDeDroit({ status: 403 })).toBe(true);
      expect(estRefusDeDroit({ status: 500 })).toBe(false);
      expect(estRefusDeDroit(null)).toBe(false);
    });
  });
});
