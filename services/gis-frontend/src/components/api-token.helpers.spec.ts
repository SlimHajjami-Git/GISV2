import {
  CRON_LINE_EXAMPLE, activeKeyCount, apiKeyStatusLabel, apiTokenErrorMessage, apiTokenStatusLabel,
  cronScriptExample, curlExample, formatCountdown, isoDay, secondsUntil
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

/** Clés d'automatisation de l'API (migration 069), pour une tâche planifiée. */
describe('api-token.helpers — clés d’automatisation', () => {
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

  it('prépare un script qui échange la clé puis lit les arrêts de la veille avec le jeton', () => {
    const script = cronScriptExample('clyk_abc', 'https://belive-calypso.com/');
    const lignes = script.split('\n');

    expect(lignes[0]).toBe('#!/bin/sh');
    expect(script).toContain('CLE="clyk_abc"');
    expect(script).toContain('URL="https://belive-calypso.com"');
    // 1. échange de la clé (en-tête X-Api-Key, sans session) …
    expect(script).toContain('-X POST "$URL/api/api-tokens/echange" -H "X-Api-Key: $CLE"');
    // … dont on extrait le jeton sans dépendance (sed, pas jq) …
    expect(script).toContain(`sed -E 's/.*"token":"([^"]+)".*/\\1/'`);
    // 2. … qui lit les arrêts de la veille, un seul appel.
    expect(script).toContain('"$URL/api/reports/stops?startDate=$JOUR&endDate=$JOUR"');
    expect(script).toContain('-H "X-Api-Token: $JETON"');
    expect(script).toContain('JOUR=$(date -d yesterday +%F)');
    // La clé n'est jamais envoyée en jeton.
    expect(script).not.toContain('X-Api-Token: $CLE');
  });

  it('propose une ligne de crontab quotidienne', () => {
    expect(CRON_LINE_EXAMPLE).toMatch(/^\d+ \d+ \* \* \* \S+$/);
  });
});
