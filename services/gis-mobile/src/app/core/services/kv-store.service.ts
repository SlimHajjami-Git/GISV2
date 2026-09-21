import { Injectable } from '@angular/core';
import { Preferences } from '@capacitor/preferences';

/**
 * Magasin clé-valeur JSON au-dessus de @capacitor/preferences.
 *
 * Existe pour être REMPLAÇABLE dans les tests : le mandataire d'un plugin Capacitor
 * (registerPlugin) intercepte toute lecture de propriété, si bien que
 * `spyOn(Preferences, 'get')` n'a aucun effet — le mandataire renvoie toujours son
 * propre wrapper. Les services qui persistent quelque chose (files hors ligne, état
 * du suivi) passent par ce service, et les specs fournissent une version en mémoire.
 */
@Injectable({ providedIn: 'root' })
export class KvStore {
  async get<T>(key: string): Promise<T | null> {
    try {
      const { value } = await Preferences.get({ key });
      return value ? (JSON.parse(value) as T) : null;
    } catch (e) {
      console.warn(`[KvStore] lecture impossible (${key})`, e);
      return null;
    }
  }

  async set<T>(key: string, value: T): Promise<void> {
    await Preferences.set({ key, value: JSON.stringify(value) });
  }

  async remove(key: string): Promise<void> {
    await Preferences.remove({ key });
  }
}
