/**
 * Belive GPA s'adresse à l'export : les champs préremplis des écrans clients ne doivent plus
 * proposer d'exemples tunisiens (indicatif +216, villes, patronymes, plaque locale, montants
 * en millimes). Garde de non-régression : elle relit le source des gabarits, seul endroit où
 * ces exemples vivent (attributs placeholder et jeux de démonstration en dur).
 *
 * Restent volontairement hors périmètre, car ce ne sont pas des exemples de saisie :
 * les listes (pays, indicatifs, fuseaux, devises) et les défauts par déploiement, le libellé
 * métier « CIN » (donnée réellement persistée), le gabarit de montant « 0.000 » (devise), les
 * opérateurs SIM, et les deux écrans de sinistres traités dans un second passage.
 * Hors d'atteinte d'un test de source : l'exemple de téléphone de l'inscription, lié à
 * environment.phonePlaceholder — il se règle dans la copie d'environment.ts du déploiement.
 */

// Fichier traité comme un module : sans cela les déclarations ci-dessous seraient globales
// et entreraient en conflit avec celles des autres gardes de même nature.
export {};

// tsconfig.spec.json limite les types à « jest » : on déclare l'accès fichier de Node ici
// plutôt que d'élargir la configuration partagée pour un seul test.
declare const require: (module: string) => any;
declare const __dirname: string;

const { readFileSync, readdirSync } = require('fs');
const { join } = require('path');

describe('exemples des écrans clients (marché français)', () => {
  /** Écrans de sinistres : exemples tunisiens connus, repris dans un second passage. */
  const reportes = ['accident-report.component.ts', 'accident-reports-list.component.ts'];

  const source = (fichier: string): string =>
    String(readFileSync(join(__dirname, fichier), 'utf8'));

  /** Certains exemples ont quitté les gabarits pour un jeu de textes par pays. */
  const sourceService = (fichier: string): string =>
    String(readFileSync(join(__dirname, '..', 'services', fichier), 'utf8'));

  /** Gabarits des écrans clients : la racine de components/ sans les fenêtres partagées. */
  const ecrans: string[] = readdirSync(__dirname)
    .filter((f: string) => /\.(ts|html)$/.test(f))
    .filter((f: string) => !f.endsWith('.spec.ts'))
    .filter((f: string) => !reportes.includes(f));

  const exemplesProposes = (fichier: string): string[] => {
    const trouves: string[] = [];
    const motif = /placeholder="([^"]*)"/g;
    let occurrence: RegExpExecArray | null;
    while ((occurrence = motif.exec(source(fichier))) !== null) {
      trouves.push(occurrence[1]);
    }
    return trouves;
  };

  const marqueursTunisiens =
    /\+216|Tunis|Sousse|Sfax|Bizerte|Hammamet|Monastir|Ben Salah|Sonia|@[\w.-]+\.tn\b|\d{3} TUN? \d{4}/;

  it('aucun écran client ne propose un exemple tunisien dans ses champs', () => {
    const fautifs = ecrans.flatMap((fichier) =>
      exemplesProposes(fichier)
        .filter((exemple) => marqueursTunisiens.test(exemple))
        .map((exemple) => `${fichier} : ${exemple}`)
    );
    expect(fautifs).toEqual([]);
  });

  it('les exemples remplaçants sont bien français', () => {
    expect(source('profile.component.ts')).toContain('placeholder="+33 6 12 34 56 78"');
    expect(source('profile.component.ts')).toContain('placeholder="Lyon"');
    expect(source('user-management.component.ts')).toContain('placeholder="+33 6 12 34 56 78"');
    expect(source('register.component.ts')).toContain('placeholder="Jean"');
    expect(source('register.component.ts')).toContain('placeholder="Dupont"');
    expect(source('register.component.ts')).toContain('placeholder="Transports Dupont"');
    // L'exemple d'assureur « (ex: AXA, Macif...) » a disparu avec le sous-formulaire
    // Assurance de l'écran Dépenses (Karim, 23/09/2026) : l'assurance se règle
    // depuis Échéances. Plus rien à vérifier ici pour cet écran.
    // L'exemple de nom de tournée n'est plus en dur dans le gabarit : il suit
    // désormais la devise du compte (Karim, 23/09/2026 — « Tunis - Sfax » hors
    // euro). L'exemple français vit dans le jeu de textes, et le gabarit s'y lie.
    expect(sourceService('tournees-textes-pays.ts')).toContain("exempleNom: 'Ex: Livraison Lyon - Marseille'");
    expect(source('tours.component.ts')).toContain('[placeholder]="textes.exempleNom"');
    expect(source('vehicle-loans.component.ts')).toContain('placeholder="Ex: Lyon"');
    expect(source('vehicles.component.ts')).toContain('chez Peugeot Lyon');
  });

  // Le plein est l'écran central de l'offre sans boîtier : un prix à 12,450 et un total à
  // 568,45 pour 45,5 L sont des millimes, invraisemblables en euros.
  it("l'écran carburant propose des montants à l'échelle européenne", () => {
    const carburant = source('carburant.component.ts');
    expect(carburant).toContain('placeholder="Ex: 1.750"');
    expect(carburant).toContain('placeholder="Ex: 79.63"');
    expect(carburant).not.toContain('12.450');
    expect(carburant).not.toContain('568.45');
  });

  // Ce repli s'affiche dans le sélecteur de véhicule quand l'API ne répond pas.
  it('le repli de l\'écran réparations montre des immatriculations françaises', () => {
    const reparations = source('repairs.component.ts');
    expect(reparations).toContain("plateNumber: 'AB-123-CD'");
    expect(/plateNumber: '\d{3} TUN \d{4}'/.test(reparations)).toBe(false);
  });

  it('les jeux de démonstration de la fiche véhicule ne citent plus de villes tunisiennes', () => {
    expect(source('vehicles.component.ts')).toContain("const cities = ['Lyon Part-Dieu'");
  });
});
