import { VISITES_ECRANS } from './help-content';

/**
 * Tutoriels des écrans (Karim, 24/09/2026) : garde-fous communs à TOUS les tutoriels,
 * présents et à venir. Un repère data-guide mal orthographié, un chemin qui n'existe
 * pas ou un tutoriel qui ne finit pas par l'enregistrement ne se voient qu'en testant
 * l'écran à la main : ici, ils cassent la compilation des tests.
 */
declare const require: (m: string) => any;
declare const __dirname: string;

describe('Tutoriels des écrans — cohérence du contenu', () => {
  const fs = require('fs');
  const path = require('path');
  const racine = path.join(__dirname, '..');

  /** Tout le code des composants, là où les repères data-guide sont posés. */
  const sources = (() => {
    const lire = (dossier: string): string[] => fs.readdirSync(dossier, { withFileTypes: true })
      .flatMap((e: any) => e.isDirectory()
        ? lire(path.join(dossier, e.name))
        : /\.(ts|html)$/.test(e.name) && !/\.spec\.ts$/.test(e.name) ? [fs.readFileSync(path.join(dossier, e.name), 'utf8')] : []);
    return lire(path.join(racine, 'components')).join('\n');
  })();
  const routes: string = fs.readFileSync(path.join(racine, 'app.routes.ts'), 'utf8');

  it('les identifiants de tutoriel et d\'étape sont uniques', () => {
    const ids = VISITES_ECRANS.flatMap(v => [v.id, ...v.etapes.map(e => e.id)]);
    expect(new Set(ids).size).toBe(ids.length);
  });

  for (const v of VISITES_ECRANS) {
    describe(v.titre, () => {
      it('commence par un geste (clic) et finit par l\'enregistrement (disparition)', () => {
        expect(v.etapes[0].action).toBe('clic');
        expect(v.etapes[v.etapes.length - 1].action).toBe('disparition');
        expect(v.etapes.every(e => !!e.action)).toBe(true);
      });

      it('chaque repère visé existe dans un écran', () => {
        const absents = v.etapes.map(e => e.cible)
          .filter(c => !sources.includes('"' + c + '"') && !sources.includes("'" + c + "'"));
        expect(absents).toEqual([]);
      });

      it('ses chemins existent dans les routes de l\'application', () => {
        for (const r of [v.route, ...(v.autresRoutes || [])]) {
          expect(routes).toContain("path: '" + r.replace(/^\//, '') + "'");
        }
      });

      it('aucune bulle n\'anticipe une erreur (règle de Karim)', () => {
        expect(v.etapes.some(e => /message d'erreur|erreur s'affiche/i.test(e.texte))).toBe(false);
      });
    });
  }
});
