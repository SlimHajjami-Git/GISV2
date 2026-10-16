import { DashboardComponent } from './dashboard.component';

/**
 * Tableau de bord GPA d'un utilisateur limité (campagne GPA du 06/10/2026, finitions) : la
 * tuile « Entretiens à venir » affichait « accès non autorisé ». Une tuile que l'utilisateur
 * n'a pas le droit de voir est masquée, et la rangée se règle sur les tuiles restantes.
 */
describe('Tableau de bord GPA — les tuiles non permises sont masquées', () => {
  const avec = (gpa: any): DashboardComponent => {
    const d = Object.create(DashboardComponent.prototype) as DashboardComponent;
    (d as any).gpa = gpa;
    return d;
  };
  const toutes = { costs: {}, acquisition: {}, leasingRemaining: {}, interventions: {}, upcomingMaintenance: {} };

  it('pendant le chargement : les cinq tuiles (avec « — »)', () => {
    expect(avec(null).nbTuilesGpa).toBe(5);
  });

  it('administrateur : les cinq tuiles', () => {
    expect(avec(toutes).nbTuilesGpa).toBe(5);
  });

  it('utilisateur limité : seulement les tuiles permises', () => {
    expect(avec({ ...toutes, upcomingMaintenance: null, interventions: null }).nbTuilesGpa).toBe(3);
    expect(avec({ ...toutes, upcomingMaintenance: null }).nbTuilesGpa).toBe(4);
  });

  it('aucune section permise : aucune tuile (la rangée disparaît)', () => {
    expect(avec({ costs: null, acquisition: null, leasingRemaining: null, interventions: null, upcomingMaintenance: null }).nbTuilesGpa).toBe(0);
  });
});
