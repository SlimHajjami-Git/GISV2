export const environment = {
  production: true,
  apiUrl: '/api',
  // Branding + default map view + default currency. Default = Tunisie / Calypso / TND.
  // Per-deployment override (e.g. Algeria / Bougeo) edits ONLY these
  // lines before building that deployment's frontend image
  // (e.g. brandName: 'Bougeo', defaultCurrency: 'DZD').
  brandName: 'Calypso',
  mapCenter: { lat: 36.8065, lng: 10.1815, zoom: 8 },
  defaultCurrency: 'TND'
};
