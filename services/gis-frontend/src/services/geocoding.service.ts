import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { map, catchError, shareReplay } from 'rxjs/operators';

export interface NominatimResponse {
  place_id: number;
  display_name: string;
  address?: {
    road?: string;
    house_number?: string;
    suburb?: string;
    city?: string;
    town?: string;
    village?: string;
    state?: string;
    country?: string;
  };
}

@Injectable({
  providedIn: 'root'
})
export class GeocodingService {
  private cache = new Map<string, Observable<string>>();
  private readonly CACHE_SIZE = 1000;

  constructor(private http: HttpClient) {}

  /**
   * Reverse geocode coordinates to address
   * Uses local Nominatim instance via nginx proxy
   */
  reverseGeocode(lat: number, lon: number): Observable<string> {
    const cacheKey = `${lat.toFixed(4)},${lon.toFixed(4)}`;
    
    // Check cache first
    if (this.cache.has(cacheKey)) {
      return this.cache.get(cacheKey)!;
    }

    // Limit cache size
    if (this.cache.size >= this.CACHE_SIZE) {
      const firstKey = this.cache.keys().next().value;
      if (firstKey) this.cache.delete(firstKey);
    }

    const request$ = this.http.get<{latitude: number; longitude: number; address: string | null}>(
      `/api/gps/geocode/reverse?lat=${lat}&lon=${lon}`
    ).pipe(
      map(response => response.address || `${lat.toFixed(4)}°, ${lon.toFixed(4)}°`),
      catchError(() => of(`${lat.toFixed(4)}°, ${lon.toFixed(4)}°`)),
      shareReplay(1)
    );

    this.cache.set(cacheKey, request$);
    return request$;
  }

  /**
   * Batch reverse geocode multiple coordinates
   * Returns a map of "lat,lon" -> address
   */
  async batchReverseGeocode(coordinates: { lat: number; lon: number }[]): Promise<Map<string, string>> {
    const results = new Map<string, string>();
    
    // Deduplicate coordinates (round to 4 decimals)
    const uniqueCoords = new Map<string, { lat: number; lon: number }>();
    for (const coord of coordinates) {
      const key = `${coord.lat.toFixed(4)},${coord.lon.toFixed(4)}`;
      if (!uniqueCoords.has(key)) {
        uniqueCoords.set(key, coord);
      }
    }

    // Process in batches of 10 to avoid overwhelming the server
    const entries = Array.from(uniqueCoords.entries());
    const batchSize = 10;
    
    for (let i = 0; i < entries.length; i += batchSize) {
      const batch = entries.slice(i, i + batchSize);
      const promises = batch.map(async ([key, coord]) => {
        try {
          const address = await this.reverseGeocode(coord.lat, coord.lon).toPromise();
          results.set(key, address || key);
        } catch {
          results.set(key, `${coord.lat.toFixed(4)}°, ${coord.lon.toFixed(4)}°`);
        }
      });
      await Promise.all(promises);
      
      // Small delay between batches to be nice to the server
      if (i + batchSize < entries.length) {
        await new Promise(resolve => setTimeout(resolve, 100));
      }
    }

    return results;
  }

  /**
   * Format Nominatim response to a short readable address
   */
  private formatAddress(response: NominatimResponse): string {
    if (!response.address) {
      // Fallback to display_name but truncate it
      const parts = response.display_name?.split(',').slice(0, 3) || [];
      return parts.join(', ').trim() || 'Adresse inconnue';
    }

    const addr = response.address;
    const parts: string[] = [];

    // Street with number
    if (addr.road) {
      if (addr.house_number) {
        parts.push(`${addr.house_number} ${addr.road}`);
      } else {
        parts.push(addr.road);
      }
    }

    // City/Town/Village
    const locality = addr.city || addr.town || addr.village || addr.suburb;
    if (locality) {
      parts.push(locality);
    }

    return parts.length > 0 ? parts.join(', ') : 'Adresse inconnue';
  }

  /**
   * Clear the cache
   */
  clearCache(): void {
    this.cache.clear();
  }
}
