import { textesSinistres, TEXTES_SINISTRES_FRANCE, TEXTES_SINISTRES_TUNISIE } from './sinistres-textes-pays';

/**
 * Textes du module Sinistres selon le pays du client (23/09/2026).
 *
 * Le point qui compte : les clients hors euro doivent voir EXACTEMENT ce qu'ils
 * voyaient avant — les chaines tunisiennes sont figees ici au caractere pres,
 * copiees des gabarits d'origine, pour qu'une retouche ne les change pas sans
 * qu'un test le dise.
 */
describe('textesSinistres', () => {
  it('un compte en euros voit les textes francais choisis par Karim', () => {
    const t = textesSinistres('EUR');
    expect(t).toBe(TEXTES_SINISTRES_FRANCE);
    expect(t.division).toBe('Département');
    expect(t.localite).toBe('Ville');
    expect(t.exempleDivision).toBe('Rhône, Bouches-du-Rhône, ...');
    expect(t.exempleLocalite).toBe('Lyon, Marseille, ...');
    expect(t.exempleTelephone).toBe('+33 6 ...');
    expect(t.exemplePlaque).toBe('AB-123-CD');
    expect(t.exempleAssureurs).toBe('AXA, MAIF, Allianz...');
  });

  it('tout autre compte garde les textes d’origine, au caractere pres', () => {
    for (const devise of ['TND', 'MAD', 'DZD', 'USD', 'SAR', 'AED', '', null, undefined]) {
      expect(textesSinistres(devise)).toBe(TEXTES_SINISTRES_TUNISIE);
    }
    // Chaines copiees des gabarits d'avant la modification : ne pas « corriger ».
    expect(TEXTES_SINISTRES_TUNISIE).toEqual({
      division: 'Gouvernorat',
      localite: 'Commune',
      exempleDivision: 'Tunis, Sfax, ...',
      exempleLocalite: 'Le Bardo, ...',
      exempleTelephone: '+216 ...',
      exemplePlaque: '123 TU 4567',
      exempleAssureurs: 'STAR, COMAR, AMI...',
      synthAvantLocalite: ', sur la commune de ',
      synthApresLocalite: '',
      synthAvantDivision: ', dans le gouvernorat de ',
      synthApresDivision: ''
    });
  });

  it('la synthese se lit naturellement dans les deux pays', () => {
    const phrase = (t: ReturnType<typeof textesSinistres>, ville: string, division: string) =>
      'le 12/09/2026' + t.synthAvantLocalite + ville + t.synthApresLocalite + t.synthAvantDivision + division + t.synthApresDivision + '.';
    expect(phrase(textesSinistres('TND'), 'Le Bardo', 'Tunis')).toBe('le 12/09/2026, sur la commune de Le Bardo, dans le gouvernorat de Tunis.');
    // En francais, la parenthese evite « du / des / de la » qui varie selon le departement.
    expect(phrase(textesSinistres('EUR'), 'Marseille', 'Bouches-du-Rhône')).toBe('le 12/09/2026, à Marseille (Bouches-du-Rhône).');
  });
});
