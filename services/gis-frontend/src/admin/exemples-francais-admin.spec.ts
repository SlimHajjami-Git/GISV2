/**
 * Belive GPA s'adresse à l'export : les champs préremplis de l'espace d'administration
 * ne doivent plus proposer d'exemples tunisiens (indicatif +216, patronymes, courriels .tn,
 * adresse ou identifiant fiscal tunisiens).
 * Garde de non-régression : elle relit le source des gabarits, seul endroit où ces exemples
 * vivent (placeholders et valeurs de démonstration en dur dans les templates en ligne).
 * Restent volontairement hors périmètre, car ce ne sont pas des exemples de saisie client :
 * le fuseau « Africa/Tunis » et le libellé de la devise « TND - Tunisian Dinar », tous deux
 * verrouillés en positif plus bas ; le libellé métier « CIN », champ réellement persisté.
 */

// tsconfig.spec.json limite les types à « jest » : on déclare l'accès fichier de Node ici
// plutôt que d'élargir la configuration partagée pour un seul test.
declare const require: (module: string) => any;
declare const __dirname: string;

const { readFileSync } = require('fs');
const { join } = require('path');

describe("exemples de l'espace d'administration (marché français)", () => {
  const ecrans = [
    'pages/admin-clients.component.ts',
    'pages/admin-company-details.component.ts',
    'pages/admin-estimates.component.ts',
    'pages/admin-login.component.ts',
    'pages/admin-settings.component.ts',
    'pages/admin-users.component.ts'
  ];

  const lignes = (fichier: string): string[] =>
    String(readFileSync(join(__dirname, fichier), 'utf8')).split('\n');

  const source = (fichier: string): string => lignes(fichier).join('\n');

  // Le fuseau et le libellé de la devise sont les deux seules mentions tunisiennes légitimes :
  // on les neutralise avant de chercher une nouvelle occurrence.
  const horsExclusions = (ligne: string): string =>
    ligne.replace(/Africa\/Tunis/g, '').replace(/TND - Tunisian Dinar/g, '');

  const interdits: { quoi: string; motif: (ligne: string) => boolean }[] = [
    { quoi: "d'indicatif +216 ni +212", motif: (l) => /\+21[26]/.test(l) },
    { quoi: 'de courriel en .tn', motif: (l) => /@[A-Za-z0-9._-]*\.tn\b/.test(l) },
    { quoi: 'de ville ou de pays tunisiens', motif: (l) => /tunis/i.test(horsExclusions(l)) },
    { quoi: "d'identifiant fiscal tunisien", motif: (l) => /['"]TN\d/.test(l) }
  ];

  for (const fichier of ecrans) {
    for (const { quoi, motif } of interdits) {
      it(`${fichier} ne propose plus ${quoi}`, () => {
        const fautive = lignes(fichier).find(motif);
        expect(fautive ?? null).toBeNull();
      });
    }
  }

  it('les exemples remplaçants sont bien français', () => {
    const clients = source('pages/admin-clients.component.ts');
    expect(clients).toContain('placeholder="contact@societe.fr"');
    expect(clients).toContain('placeholder="admin@societe.fr"');
    expect(clients).toContain('placeholder="+33 1 23 45 67 89"');
    expect(clients).toContain('placeholder="+33 6 12 34 56 78"');

    const societe = source('pages/admin-company-details.component.ts');
    expect(societe).toContain('placeholder="Jean"');
    expect(societe).toContain('placeholder="Dupont"');
    expect(societe).toContain('placeholder="+33 6 12 34 56 78"');

    const devis = source('pages/admin-estimates.component.ts');
    expect(devis).toContain('placeholder="contact@client.fr"');
    expect(devis).toContain('placeholder="+33 1 23 45 67 89"');

    const utilisateurs = source('pages/admin-users.component.ts');
    expect(utilisateurs).toContain('placeholder="+33 6 12 34 56 78"');

    const connexion = source('pages/admin-login.component.ts');
    expect(connexion).toContain('placeholder="admin@exemple.fr"');

    const reglages = source('pages/admin-settings.component.ts');
    expect(reglages).toContain("companyAddress: '12 rue de la République\\n69003 Lyon, France'");
    expect(reglages).toContain("companyPhone: '+33 1 23 45 67 89'");
    expect(reglages).toContain("taxId: 'FR12345678901'");
    expect(reglages).toContain("supportEmail: 'support@exemple.fr'");
    expect(reglages).toContain("alertEmails: 'admin@exemple.fr\\nsupport@exemple.fr'");
  });

  it('le fuseau et la devise du déploiement restent intacts', () => {
    const reglages = source('pages/admin-settings.component.ts');
    expect(reglages).toContain("defaultTimezone: 'Africa/Tunis'");
    expect(reglages).toContain('<option value="TND">');
    expect(reglages).toContain('TND - Tunisian Dinar');
  });

  it('le libellé métier CIN, champ persisté, est laissé en place', () => {
    const societe = source('pages/admin-company-details.component.ts');
    expect(societe).toContain('[(ngModel)]="userForm.cin"');
  });
});
