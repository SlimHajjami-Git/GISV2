export const environment = {
  production: true,
  apiUrl: '/api',
  // Branding + default map view. Default = Tunisie / Calypso.
  // Per-deployment override (e.g. Algeria / Bougeo) edits ONLY these two
  // lines before building that deployment's frontend image.
  brandName: 'Calypso',
  mapCenter: { lat: 36.8065, lng: 10.1815, zoom: 8 }
};
