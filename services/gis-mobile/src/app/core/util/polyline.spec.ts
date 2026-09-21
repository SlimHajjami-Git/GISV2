import { decodePolyline6, haversineM } from './polyline';

/** Encodeur de référence (algorithme Google, précision 6 comme Valhalla) pour les allers-retours. */
function encode6(points: [number, number][]): string {
  let out = '', pLat = 0, pLng = 0;
  const encNum = (v: number) => {
    let n = v < 0 ? ~(v << 1) : v << 1;
    let s = '';
    while (n >= 0x20) { s += String.fromCharCode((0x20 | (n & 0x1f)) + 63); n >>= 5; }
    return s + String.fromCharCode(n + 63);
  };
  for (const [lat, lng] of points) {
    const iLat = Math.round(lat * 1e6), iLng = Math.round(lng * 1e6);
    out += encNum(iLat - pLat) + encNum(iLng - pLng);
    pLat = iLat; pLng = iLng;
  }
  return out;
}

describe('decodePolyline6', () => {
  it('décode le vecteur de référence Google avec le diviseur 1e6 (précision Valhalla)', () => {
    // « _p~iF~ps|U_ulLnnqC_mqNvxq`@ » = (38.5,-120.2) (40.7,-120.95) (43.252,-126.453) en précision 5 ;
    // lu en précision 6, chaque coordonnée est dix fois plus petite.
    const pts = decodePolyline6('_p~iF~ps|U_ulLnnqC_mqNvxq`@');
    expect(pts.length).toBe(3);
    expect(pts[0][0]).toBeCloseTo(3.85, 6);
    expect(pts[0][1]).toBeCloseTo(-12.02, 6);
    expect(pts[1][0]).toBeCloseTo(4.07, 6);
    expect(pts[1][1]).toBeCloseTo(-12.095, 6);
    expect(pts[2][0]).toBeCloseTo(4.3252, 6);
    expect(pts[2][1]).toBeCloseTo(-12.6453, 6);
  });

  it('aller-retour sur un itinéraire tunisien (Tunis → Sousse)', () => {
    const route: [number, number][] = [[36.806495, 10.181532], [36.4, 10.3], [35.825603, 10.63699]];
    const pts = decodePolyline6(encode6(route));
    expect(pts.length).toBe(3);
    pts.forEach((p, i) => {
      expect(p[0]).toBeCloseTo(route[i][0], 6);
      expect(p[1]).toBeCloseTo(route[i][1], 6);
    });
  });

  it('rend une liste vide sans polyline (vieille tournée → liaison droite)', () => {
    expect(decodePolyline6(null)).toEqual([]);
    expect(decodePolyline6(undefined)).toEqual([]);
    expect(decodePolyline6('')).toEqual([]);
  });
});

describe('haversineM', () => {
  it('0,001° de latitude ≈ 111 m', () => {
    expect(haversineM(36.8, 10.18, 36.801, 10.18)).toBeCloseTo(111.2, 0);
  });

  it('distance nulle sur place', () => {
    expect(haversineM(36.8, 10.18, 36.8, 10.18)).toBe(0);
  });
});
