export const environment = {
  production: false,
  apiUrl: '/api',
  // Branding + default map view + default currency. THIS is the file every build
  // bundles — angular.json has no fileReplacements, so environment.prod.ts is NOT
  // used. Each deployment edits these values locally before building its image
  // (Tunisie/Calypso/TND here by default; the Algeria/Bougeo build sets
  // brandName: 'Bougeo' and defaultCurrency: 'DZD' in its own copy of this file).
  brandName: 'Calypso',
  mapCenter: { lat: 36.8065, lng: 10.1815, zoom: 8 },
  // ISO code used as the fallback display currency for operators who have not
  // picked one in /settings. Must be one of the codes in UserPreferences.Currency.
  defaultCurrency: 'TND',
  // Pre-login AI automobile assistant as the landing page. PER-DEPLOYMENT:
  // true on Calypso/TN; leave absent (or false) on deployments that must keep
  // the classic marketing landing (e.g. Bougeo/DZ).
  aiAssistantLanding: true
};
