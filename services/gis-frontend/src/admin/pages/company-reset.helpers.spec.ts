import {
  driverAccountsPreviewText, driverAccountsResultText, driverAccountsToClose, keptUsersText
} from './company-reset.helpers';

/**
 * Remise à zéro d'une société (relecture du 21/09/2026, R7c) : l'écran promettait
 * « Conservé : … ses utilisateurs » alors que les comptes chauffeurs perdent leur fiche
 * et sont désactivés. Ces textes disent combien, avant comme après.
 */
describe('company-reset.helpers', () => {
  it('aperçu : N comptes chauffeurs seront désactivés, leurs fiches partent avec les données', () => {
    const apercu = { totalRows: 120, driverAccountsClosed: 3 };
    expect(driverAccountsToClose(apercu)).toBe(3);
    expect(driverAccountsPreviewText(apercu))
      .toBe("3 comptes chauffeurs seront désactivés : leurs fiches partent avec les données, ils ne pourront plus se connecter à l'application.");
    expect(driverAccountsPreviewText({ totalRows: 5, driverAccountsClosed: 1 }))
      .toBe("1 compte chauffeur sera désactivé : sa fiche part avec les données, il ne pourra plus se connecter à l'application.");
  });

  it('« Conservé : ses utilisateurs » ne ment plus quand N > 0', () => {
    expect(keptUsersText({ totalRows: 120, driverAccountsClosed: 3 }))
      .toBe('ses utilisateurs et leurs rôles, sauf 3 comptes chauffeurs qui seront désactivés');
    expect(keptUsersText({ totalRows: 120, driverAccountsClosed: 1 }))
      .toBe('ses utilisateurs et leurs rôles, sauf 1 compte chauffeur qui sera désactivé');
    expect(keptUsersText({ totalRows: 120, driverAccountsClosed: 0 })).toBe('ses utilisateurs et leurs rôles');
    // Aperçu pas encore arrivé (ou en échec) : on ne promet pas de garder les chauffeurs.
    expect(keptUsersText(null)).toContain('hors comptes chauffeurs');
  });

  it('aucun compte chauffeur, ou serveur antérieur sans le champ : rien à annoncer', () => {
    expect(driverAccountsPreviewText({ totalRows: 120, driverAccountsClosed: 0 })).toBe('');
    expect(driverAccountsPreviewText({ totalRows: 120 })).toBe('');
    expect(driverAccountsPreviewText(null)).toBe('');
    expect(driverAccountsToClose({ totalRows: 120, driverAccountsClosed: -2 })).toBe(0);
  });

  it('aperçu sans ligne à supprimer : le bouton est verrouillé, rien ne sera désactivé', () => {
    const vide = { totalRows: 0, driverAccountsClosed: 2 };
    expect(driverAccountsToClose(vide)).toBe(0);
    expect(driverAccountsPreviewText(vide)).toBe('');
    expect(keptUsersText(vide)).toBe('ses utilisateurs et leurs rôles');
  });

  it('résultat : le nombre rendu fait foi', () => {
    expect(driverAccountsResultText({ totalRows: 118, driverAccountsClosed: 3 }))
      .toBe('3 comptes chauffeurs désactivés : leurs fiches sont parties avec les données.');
    expect(driverAccountsResultText({ totalRows: 4, driverAccountsClosed: 1 }))
      .toBe('1 compte chauffeur désactivé : sa fiche est partie avec les données.');
    // Données parties entre l'aperçu et l'exécution : les comptes ont quand même été désactivés.
    expect(driverAccountsResultText({ totalRows: 0, driverAccountsClosed: 2 }))
      .toBe('2 comptes chauffeurs désactivés : leurs fiches sont parties avec les données.');
    expect(driverAccountsResultText({ totalRows: 118, driverAccountsClosed: 0 })).toBe('');
  });
});
