export const environment = {
  production: true,
  // HTTPS sur le nom de domaine : le certificat GlobalSign (secret belive-calypso-tls,
  // k8s/04-ingress.yaml) couvre www.belive-calypso.com et l'apex. Plus d'adresse IP en clair.
  apiUrl: 'https://www.belive-calypso.com/api',
  signalrUrl: 'https://www.belive-calypso.com/hubs/gps'
};
