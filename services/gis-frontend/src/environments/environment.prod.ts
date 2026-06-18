// ⚠️ NOTE: this file is currently DEAD CODE. angular.json has no
// `fileReplacements`, so `ng build --configuration=production` does NOT swap
// environment.ts for this file — environment.ts is always the one bundled.
// Per-deployment overrides (brandName, defaultCurrency, …) must be made in
// environment.ts. Kept in sync only so it doesn't drift if fileReplacements is
// ever re-enabled.
export const environment = {
  production: true,
  apiUrl: '/api',
  brandName: 'Calypso',
  mapCenter: { lat: 36.8065, lng: 10.1815, zoom: 8 },
  defaultCurrency: 'TND'
};
