/**
 * Décode une polyline encodée en précision 6 (format Valhalla, champ
 * `estimatedRoutePolyline` des tournées) en liste de [lat, lng] pour Leaflet.
 * Même algorithme que gis-frontend/src/components/tours.component.ts (decodePolyline6).
 * Polyline corrompue → liste vide (l'appelant se rabat sur la liaison droite).
 */
export function decodePolyline6(encoded: string | null | undefined): [number, number][] {
  if (!encoded) return [];
  const pts: [number, number][] = [];
  let index = 0, lat = 0, lng = 0;
  try {
    while (index < encoded.length) {
      let b: number, shift = 0, result = 0;
      do { b = encoded.charCodeAt(index++) - 63; result |= (b & 0x1f) << shift; shift += 5; } while (b >= 0x20);
      lat += (result & 1) ? ~(result >> 1) : (result >> 1);
      shift = 0; result = 0;
      do { b = encoded.charCodeAt(index++) - 63; result |= (b & 0x1f) << shift; shift += 5; } while (b >= 0x20);
      lng += (result & 1) ? ~(result >> 1) : (result >> 1);
      pts.push([lat / 1e6, lng / 1e6]);
    }
  } catch {
    return [];
  }
  return pts;
}

/** Distance en mètres entre deux points (Haversine). */
export function haversineM(lat1: number, lon1: number, lat2: number, lon2: number): number {
  const R = 6371000;
  const dLat = (lat2 - lat1) * Math.PI / 180;
  const dLon = (lon2 - lon1) * Math.PI / 180;
  const a = Math.sin(dLat / 2) ** 2 +
    Math.cos(lat1 * Math.PI / 180) * Math.cos(lat2 * Math.PI / 180) *
    Math.sin(dLon / 2) ** 2;
  return R * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
}
