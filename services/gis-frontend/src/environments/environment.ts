export const environment = {
  production: false,
  apiUrl: '/api',
  // Branding + default map view + default currency — overridable per deployment
  // (Tunisie/Calypso/TND by default; the Algeria/Bougeo build overrides these in
  // environment.prod.ts, e.g. defaultCurrency: 'DZD').
  brandName: 'Calypso',
  mapCenter: { lat: 36.8065, lng: 10.1815, zoom: 8 },
  // ISO code used as the fallback display currency for operators who have not
  // picked one in /settings. Must be one of the codes in UserPreferences.Currency.
  defaultCurrency: 'TND'
};
