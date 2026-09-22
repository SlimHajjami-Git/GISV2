import {
  MOVING_SPEED_KPH, NO_DEVICE_STYLE, ONLINE_WINDOW_MS, STATE_TEXT_ON_COLOR, VEHICLE_STATES, VEHICLE_STATE_ORDER,
  countByState, frameState, isFresh, motionState, stateMarkerHtml, stateStyle
} from './vehicle-state.util';

/** Rapport de contraste WCAG 2.x entre deux couleurs #rrggbb. */
function contrast(a: string, b: string): number {
  const lum = (hex: string) => {
    const [r, g, b2] = [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16) / 255)
      .map(c => (c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4)));
    return 0.2126 * r + 0.7152 * g + 0.0722 * b2;
  };
  const [hi, lo] = [lum(a), lum(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

describe('vehicle-state.util (source unique du code couleur des véhicules)', () => {
  const NOW = Date.parse('2026-09-22T12:00:00Z');
  const fresh = new Date(NOW - 5 * 60 * 1000).toISOString();      // il y a 5 min
  const stale = new Date(NOW - 11 * 24 * 3600 * 1000).toISOString(); // il y a 11 jours

  it('le code demandé : rouge à l\'arrêt, orange au ralenti, vert en route, gris déconnecté', () => {
    expect(VEHICLE_STATES.parked.color).toBe('#ef4444');
    expect(VEHICLE_STATES.idling.color).toBe('#f59e0b');
    expect(VEHICLE_STATES.moving.color).toBe('#10b981');
    expect(VEHICLE_STATES.offline.color).toBe('#9ca3af');
    expect(VEHICLE_STATE_ORDER.map(s => VEHICLE_STATES[s].label))
      .toEqual(['En route', 'Au ralenti', 'À l\'arrêt', 'Déconnecté']);
  });

  it('chaque état a sa propre icône et sa propre forme de marqueur (la couleur n\'est jamais le seul indice)', () => {
    const icons = VEHICLE_STATE_ORDER.map(s => VEHICLE_STATES[s].icon);
    const glyphs = VEHICLE_STATE_ORDER.map(s => VEHICLE_STATES[s].glyph);
    expect(new Set(icons).size).toBe(4);
    expect(new Set(glyphs).size).toBe(4);
    expect(icons).toEqual(['navigate', 'pause-circle', 'stop-circle', 'cloud-offline']);
  });

  it('le texte des pastilles reste lisible sur les quatre couleurs (contraste >= 4,5:1)', () => {
    for (const s of VEHICLE_STATE_ORDER) {
      expect(contrast(STATE_TEXT_ON_COLOR, VEHICLE_STATES[s].color))
        .withContext(s).toBeGreaterThanOrEqual(4.5);
    }
  });

  it('« Sans boîtier » n\'est pas un état : aucune des quatre couleurs ni des quatre icônes', () => {
    expect(NO_DEVICE_STYLE.label).toBe('Sans boîtier');
    for (const s of VEHICLE_STATE_ORDER) {
      expect(NO_DEVICE_STYLE.color).withContext(s).not.toContain(VEHICLE_STATES[s].color);
      expect(NO_DEVICE_STYLE.icon).withContext(s).not.toBe(VEHICLE_STATES[s].icon);
    }
    expect(VEHICLE_STATE_ORDER as readonly string[]).not.toContain(NO_DEVICE_STYLE.state);
  });

  it('stateStyle retombe sur « Déconnecté » pour une valeur inconnue', () => {
    expect(stateStyle('moving').label).toBe('En route');
    expect(stateStyle('bogus' as any).state).toBe('offline');
  });

  describe('motionState — la fraîcheur passe avant la vitesse', () => {
    it('trame fraîche, vitesse > 3 km/h : en route', () => {
      expect(motionState({ speedKph: 45, ignitionOn: true, recordedAt: fresh }, NOW)).toBe('moving');
    });

    it('trame fraîche, contact mis, à l\'arrêt : au ralenti', () => {
      expect(motionState({ speedKph: 0, ignitionOn: true, recordedAt: fresh }, NOW)).toBe('idling');
    });

    it('trame fraîche, contact coupé : à l\'arrêt', () => {
      expect(motionState({ speedKph: 0, ignitionOn: false, recordedAt: fresh }, NOW)).toBe('parked');
      expect(motionState({ speedKph: 0, ignitionOn: null, recordedAt: fresh }, NOW)).toBe('parked');
    });

    it('exactement 3 km/h ne roule pas encore (même seuil que le rapport d\'activité)', () => {
      expect(MOVING_SPEED_KPH).toBe(3);
      expect(motionState({ speedKph: 3, ignitionOn: true, recordedAt: fresh }, NOW)).toBe('idling');
      expect(motionState({ speedKph: 3.1, ignitionOn: false, recordedAt: fresh }, NOW)).toBe('moving');
    });

    it('boîtier muet depuis 11 jours avec une vitesse figée : déconnecté, pas « en route »', () => {
      expect(motionState({ speedKph: 8, ignitionOn: true, recordedAt: stale }, NOW)).toBe('offline');
    });

    it('sans horodatage exploitable : déconnecté', () => {
      expect(motionState({ speedKph: 50, ignitionOn: true }, NOW)).toBe('offline');
      expect(motionState({ speedKph: 50, ignitionOn: true, recordedAt: 'pas une date' }, NOW)).toBe('offline');
    });

    it('la fenêtre de 30 min est exclusive', () => {
      const edge = new Date(NOW - ONLINE_WINDOW_MS).toISOString();
      const justBefore = new Date(NOW - ONLINE_WINDOW_MS + 1000).toISOString();
      expect(isFresh(edge, NOW)).toBeFalse();
      expect(isFresh(justBefore, NOW)).toBeTrue();
    });
  });

  it('frameState classe une trame d\'historique sans juger de sa fraîcheur (replay)', () => {
    expect(frameState({ speedKph: 60, ignitionOn: true })).toBe('moving');
    expect(frameState({ speedKph: 0, ignitionOn: true })).toBe('idling');
    expect(frameState({ speedKph: 0, ignitionOn: false })).toBe('parked');
    expect(frameState({})).toBe('parked');
  });

  it('countByState rend toujours les quatre compteurs', () => {
    expect(countByState(['moving', 'parked', 'parked', 'offline']))
      .toEqual({ moving: 1, idling: 0, parked: 2, offline: 1 });
    expect(countByState([])).toEqual({ moving: 0, idling: 0, parked: 0, offline: 0 });
  });

  describe('stateMarkerHtml (marqueurs de carte et légende)', () => {
    it('peint le fond à la couleur de l\'état et porte le glyphe de sa forme', () => {
      for (const s of VEHICLE_STATE_ORDER) {
        const html = stateMarkerHtml(s);
        expect(html).toContain(`background:${VEHICLE_STATES[s].color}`);
        expect(html).toContain(VEHICLE_STATES[s].glyph);
        expect(html).toContain(`vs-${s}`);
      }
    });

    it('seule la flèche « en route » suit le cap', () => {
      expect(stateMarkerHtml('moving', { rotateDeg: 90 })).toContain('rotate(90deg)');
      expect(stateMarkerHtml('idling', { rotateDeg: 90 })).not.toContain('rotate(');
      expect(stateMarkerHtml('parked', { rotateDeg: 90 })).not.toContain('rotate(');
    });

    it('respecte la taille demandée', () => {
      expect(stateMarkerHtml('offline', { size: 20 })).toContain('width:20px;height:20px');
    });
  });
});
