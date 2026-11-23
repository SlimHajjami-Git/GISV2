import {
  activeKeyCount, apiKeyExample, apiKeyStatusLabel, apiTokenErrorMessage, apiTokenStatusLabel,
  curlExample, formatCountdown, isoDay, secondsUntil
} from './api-token.helpers';

/**
 * Section « Accès API » du profil (jeton d'API à usage unique, migration 064).
 * Fonctions pures, hors TestBed.
 */
describe('api-token.helpers', () => {
  it('traduit les trois statuts et laisse passer un statut inconnu', () => {
    expect(apiTokenStatusLabel('pending')).toBe('En attente');
    expect(apiTokenStatusLabel('used')).toBe('Utilisé');
    expect(apiTokenStatusLabel('expired')).toBe('Expiré');
    expect(apiTokenStatusLabel('autre')).toBe('autre');
  });

  it('compte les secondes restantes sans jamais descendre sous zéro', () => {
    const now = Date.parse('2026-10-31T10:00:00Z');
    expect(secondsUntil('2026-10-31T10:15:00Z', now)).toBe(900);
    expect(secondsUntil('2026-10-31T10:00:30.900Z', now)).toBe(30);
    expect(secondsUntil('2026-10-31T09:59:00Z', now)).toBe(0);
    expect(secondsUntil('pas une date', now)).toBe(0);
  });

  it('formate le compte à rebours', () => {
    expect(formatCountdown(905)).toBe('15 min 05 s');
    expect(formatCountdown(60)).toBe('1 min 00 s');
    expect(formatCountdown(42)).toBe('42 s');
    expect(formatCountdown(0)).toBe('expiré');
    expect(formatCountdown(-5)).toBe('expiré');
  });

  it('écrit les dates au jour près sur deux chiffres', () => {
    expect(isoDay(new Date(2026, 0, 5))).toBe('2026-01-05');
    expect(isoDay(new Date(2026, 9, 31))).toBe('2026-10-31');
  });

  it('prépare un exemple curl sur 7 jours avec l’adresse du site et le jeton', () => {
    const exemple = curlExample('cly_abc', 'https://belive-calypso.com/', new Date(2026, 9, 31));

    expect(exemple).toContain('https://belive-calypso.com/api/reports/stops?startDate=2026-10-24&endDate=2026-10-31');
    expect(exemple).toContain('-H "X-Api-Token: cly_abc"');
    expect(exemple).not.toContain('com//api');
  });

  it('montre le message du serveur, avec un repli lisible', () => {
    expect(apiTokenErrorMessage({ status: 400, error: { message: 'E-mail ou mot de passe incorrect.' } }))
      .toBe('E-mail ou mot de passe incorrect.');
    expect(apiTokenErrorMessage({ status: 429, error: { message: 'Trop de tentatives de génération de jeton.' } }))
      .toBe('Trop de tentatives de génération de jeton.');
    expect(apiTokenErrorMessage({ status: 429 })).toContain('Patientez');
    expect(apiTokenErrorMessage({ status: 500 })).toBe('Impossible de générer le jeton.');
    expect(apiTokenErrorMessage(null)).toBe('Impossible de générer le jeton.');
    expect(apiTokenErrorMessage({ status: 500 }, 'Impossible de créer la clé.')).toBe('Impossible de créer la clé.');
  });
});

/** Clés d'API (migration 069) : elles s'échangent contre des jetons à usage unique. */
describe('api-token.helpers — clés d’API', () => {
  it('traduit les statuts d’une clé', () => {
    expect(apiKeyStatusLabel('active')).toBe('Active');
    expect(apiKeyStatusLabel('revoked')).toBe('Révoquée');
    expect(apiKeyStatusLabel('expired')).toBe('Expirée');
    expect(apiKeyStatusLabel('autre')).toBe('autre');
  });

  it('ne compte que les clés actives pour le plafond de la société', () => {
    expect(activeKeyCount([{ status: 'active' }, { status: 'revoked' }, { status: 'expired' }, { status: 'active' }])).toBe(2);
    expect(activeKeyCount([])).toBe(0);
  });

  it('montre l’appel direct des arrêts avec la clé dans l’en-tête X-Api-Key', () => {
    const exemple = apiKeyExample('clyk_abc', 'https://belive-calypso.com/');

    // Adresse sans barre finale, période à compléter, clé envoyée telle quelle.
    expect(exemple).toContain('curl "https://belive-calypso.com/api/reports/stops?startDate=AAAA-MM-JJ&endDate=AAAA-MM-JJ"');
    expect(exemple).toContain('-H "X-Api-Key: clyk_abc"');
    // Plus d'étape d'échange, et la clé n'est jamais présentée comme un jeton.
    expect(exemple).not.toContain('echange');
    expect(exemple).not.toContain('X-Api-Token');
    // Rien qui évoque une tâche planifiée.
    expect(exemple).not.toMatch(/cron|planifi|#!\/bin\/sh|yesterday/i);
  });
});
