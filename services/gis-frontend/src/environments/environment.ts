export const environment = {
  production: false,
  apiUrl: '/api',
  // Branding + default map view — overridable per deployment (Tunisie/Calypso
  // by default; the Algeria/Bougeo build overrides these in environment.prod.ts).
  brandName: 'Calypso',
  mapCenter: { lat: 36.8065, lng: 10.1815, zoom: 8 }
};
