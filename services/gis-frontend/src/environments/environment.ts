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
  aiAssistantLanding: true,
  // Inscription libre (route /inscription + lien depuis /login). PER-DEPLOYMENT:
  // its absence means disabled. The screen is only half the switch — the API
  // stays closed until Registration__SelfSignupEnabled=true is set server-side.
  selfSignup: true,
  // Exemple de numéro affiché dans le formulaire d'inscription (indicatif TN).
  phonePlaceholder: '+216 20 000 000',
  // Domaines qui servent la vitrine commerciale France a la racine. PER-DEPLOYMENT.
  // Le NOM DE DOMAINE est le seul signal fiable : le fuseau horaire du visiteur
  // ne dit pas ou il est, il dit comment sa machine a ete reglee — en Tunisie
  // (UTC+1) Windows propose « Bruxelles, Copenhague, Madrid, Paris » en tete de
  // liste, si bien que des postes tunisiens se declarent Europe/Paris. Tant
  // qu aucun domaine .fr ne pointe ici, la liste reste vide et la vitrine
  // France n est accessible que par son adresse /fr.
  europeanHostnames: [] as string[],
  // Duree d essai ANNONCEE sur les ecrans publics. Le document maitre impose
  // 7 jours et interdit explicitement d afficher 14.
  //
  // Depuis la recette client du 01/09/2026, le serveur accorde 7 jours
  // (AppRegistration.TrialDays / Registration__TrialDays) — ce que le site
  // promet. Les deux valeurs ci-dessous sont donc alignees.
  selfSignupTrialDays: 7,

  // Duree REELLEMENT accordee par le serveur (AppRegistration.TrialDays).
  // A GARDER ALIGNEE sur le serveur — sinon on promet plus que l on donne.
  actualTrialDays: 7,
  // Pays preselectionne dans le formulaire d inscription (ISO 3166-1 alpha-2).
  // PER-DEPLOYMENT : TN ici, FR sur un futur deploiement francais, DZ pour Bougeo.
  // Son absence vaut TN, le pays historiquement servi.
  // Vide : aucun pays n est presuppose. Un deploiement qui veut preselectionner
  // le sien pose son code ici.
  defaultCountry: '',

  // Mobile/SIM operators shown when configuring a GPS device. PER-DEPLOYMENT:
  // Tunisian carriers here by default; the Algeria/Bougeo build replaces this
  // list with Mobilis / Djezzy / Ooredoo Algérie in its own copy of this file.
  simOperators: [
    { value: 'ooredoo', label: 'Ooredoo' },
    { value: 'orange_tunisie', label: 'Orange Tunisie' },
    { value: 'tunisie_telecom', label: 'Tunisie Telecom' }
  ]
};
