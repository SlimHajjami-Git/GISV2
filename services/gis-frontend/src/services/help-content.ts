import { HelpArticle, GuideEtape, VisiteEcran } from './help-content.model';

/**
 * Aide integree Calypso - TOUT le texte vu par le client est ici.
 *
 * Regles de redaction :
 *  - citer les libelles EXACTS de l'interface, entre guillemets francais ;
 *  - dire ce que le client voit, pas comment c'est code ;
 *  - signaler ce qui est reserve aux administrateurs, sinon l'utilisateur
 *    cherche un bouton qu'il n'a pas ;
 *  - `video.url` vide = pas de lecteur affiche, l'article reste lisible.
 *
 * Le champ `module` filtre l'aide selon l'abonnement : un client sans
 * geofences ne doit pas trouver d'article sur les zones.
 */

// Gabarit a recopier quand les capsules seront tournees :
//   video: { titre: 'Ajouter un vehicule', url: '', dureeSecondes: 95 }

export const ARTICLES_AIDE: HelpArticle[] = [

  // ------------------------------------------------------------------ Demarrage
  {
    id: 'se-connecter',
    titre: 'Se connecter à Calypso',
    module: 'general',
    motsCles: ['connexion', 'login', 'identifiant', 'mot de passe', 'oublie', 'compte', 'acces'],
    resume: "Comment ouvrir votre espace Calypso et que faire si le mot de passe ne passe plus.",
    etapes: [
      "Ouvrez votre navigateur sur l'adresse que votre administrateur vous a communiquée.",
      "Saisissez votre adresse e-mail professionnelle et votre mot de passe.",
      "Cochez « Se souvenir de moi » si vous êtes sur votre poste personnel.",
      "Cliquez sur « Se connecter »."
    ],
    paragraphes: [
      "Mot de passe oublié : cliquez sur « Mot de passe oublié ? » sous le formulaire. Vous recevrez un lien de réinitialisation sur votre adresse e-mail. Le lien a une durée de validité limitée : s'il a expiré, redemandez-en un.",
      "Si le message d'erreur persiste alors que vos identifiants sont bons, votre compte a peut-être été désactivé ou l'abonnement de votre société est suspendu. Dans ce cas, contactez l'administrateur de votre société."
    ],
    aRetenir: "Ne partagez jamais votre compte : chaque utilisateur doit avoir le sien, sinon les actions ne sont plus traçables.",
    video: { titre: 'Première connexion', url: '' }
  },
  {
    id: 'se-reperer',
    titre: "Se repérer dans l'application",
    module: 'general',
    motsCles: ['menu', 'navigation', 'accueil', 'tableau de bord', 'ecran', 'sombre', 'theme', 'notification', 'cloche'],
    resume: "À quoi servent le menu du haut, la cloche de notifications et le bouton d'aide.",
    paragraphes: [
      "Le menu en haut de l'écran donne accès aux grandes parties : le tableau de bord, l'exploitation de votre parc, la maintenance, les coûts et les rapports — plus le suivi cartographique si votre offre le comprend.",
      "Vous ne voyez que les rubriques comprises dans l'abonnement de votre société et autorisées par votre profil. Si une rubrique dont on vous parle n'apparaît pas chez vous, c'est normal : demandez à votre administrateur.",
      "La cloche affiche les événements de votre flotte : alertes de conduite, échéances et entretiens à prévoir, selon les modules de votre abonnement. Une pastille rouge signale les non lus.",
      "Le bouton en forme de lune ou de soleil bascule entre le mode clair et le mode sombre.",
      "La rubrique « Aide » du menu rouvre à tout moment ces tutoriels et la visite guidée."
    ]
  },

  // ------------------------------------------------------------------ Vehicules
  {
    id: 'ajouter-vehicule',
    titre: 'Ajouter un véhicule',
    module: 'vehicles',
    motsCles: ['ajouter', 'creer', 'nouveau', 'vehicule', 'camion', 'voiture', 'immatriculation', 'plaque', 'matricule', 'enregistrer'],
    resume: "Créer une fiche véhicule dans votre parc. Réservé aux administrateurs.",
    captures: [
      { fichier: 'ajouter-vehicule-bouton.png', legende: "Le bouton « Nouveau véhicule », en haut de la liste. Il n'apparaît que pour les administrateurs." },
      { fichier: 'ajouter-vehicule-formulaire.png', legende: "Le formulaire : les champs suivis d'une étoile sont obligatoires." },
    ],
    etapes: [
      "Allez dans le menu « Véhicules ».",
      "Cliquez sur « Nouveau véhicule » en haut de la liste.",
      "Renseignez les champs obligatoires, repérables à l'étoile : « Nom du véhicule », « Plaque », « Marque », « Modèle », « Année », « Type », « Statut », « Compteur » et « Type de carburant ».",
      "Complétez si vous le souhaitez la couleur, la capacité du réservoir et la date de mise en circulation.",
      "Cliquez sur « Ajouter »."
    ],
    paragraphes: [
      "Le bouton « Nouveau véhicule » n'apparaît que si vous êtes administrateur de votre société. Un utilisateur standard consulte la liste mais ne crée pas de véhicule.",
      "« Nom du véhicule » est le nom d'usage, celui que vos équipes emploient au quotidien (« Camion principal », « Fourgon atelier »). « Plaque » est l'immatriculation officielle.",
      "Le « Modèle » reste inactif tant que la « Marque » n'est pas choisie : c'est la marque qui détermine les modèles proposés.",
      "La section « Acquisition & financement » sert à suivre un achat ou un auto-financement. Si vous choisissez « Auto-financement », des champs supplémentaires apparaissent pour la traite mensuelle, la durée et le jour de prélèvement — et Calypso construit l'échéancier."
    ],
    aRetenir: "Une plaque ne peut exister qu'une fois dans votre société. Si le message « Le matricule … est déjà utilisé dans votre société. » s'affiche, le véhicule existe déjà : cherchez-le dans la liste plutôt que d'en créer un second.",
    video: { titre: 'Ajouter un véhicule', url: '' }
  },
  {
    id: 'trouver-vehicule',
    titre: 'Retrouver un véhicule dans la liste',
    module: 'vehicles',
    motsCles: ['rechercher', 'filtrer', 'liste', 'trouver', 'statut', 'disponible', 'service', 'maintenance', 'camion', 'voiture', 'fourgon'],
    resume: "Utiliser la recherche et les filtres de l'écran Véhicules.",
    captures: [
      { fichier: 'vehicules-filtres.png', legende: "La recherche et les deux filtres, au-dessus de la liste." },
    ],
    etapes: [
      "Dans « Véhicules », tapez dans le champ « Rechercher par nom, marque, plaque… ».",
      "Affinez avec la liste des statuts : « Disponible », « En service » ou « En maintenance ».",
      "Affinez avec la liste des types : « Camion », « Citadine », « SUV » ou « Utilitaire ».",
      "Cliquez sur une ligne pour ouvrir la fiche détaillée du véhicule."
    ],
    paragraphes: [
      "La recherche porte sur trois informations seulement : le nom, la marque et la plaque. Chercher un numéro de châssis ou un nom de chauffeur ne donnera rien ici.",
      "Le filtre par type ne propose pas « Autre » : un véhicule enregistré avec ce type se retrouve via la recherche ou en affichant « Tous les types ».",
      "Si la liste affiche « Aucun véhicule trouvé », commencez par remettre les filtres sur « Tous les statuts » et « Tous les types »."
    ]
  },
  {
    id: 'corriger-kilometrage',
    titre: 'Corriger un compteur erroné',
    module: 'vehicles',
    motsCles: ['kilometrage', 'compteur', 'km', 'corriger', 'erreur', 'saisie', 'motif', 'modifier', 'changer', 'odometre'],
    resume: "Deux façons de changer un compteur, et celle qu'il faut préférer.",
    etapes: [
      "Dans « Véhicules », cliquez sur la ligne du véhicule elle-même — pas sur le bouton « Modifier ». Le panneau de détail s'ouvre sur la droite.",
      "Dans ce panneau, repérez la ligne « Compteur » : un petit bouton « Corriger » est posé juste à côté de la valeur.",
      "Cliquez dessus. Saisissez le « Nouvelle valeur du compteur » puis le « Motif » — les deux sont obligatoires.",
      "Validez avec « Corriger »."
    ],
    paragraphes: [
      "Il existe un second chemin : le bouton « Modifier » ouvre le formulaire du véhicule, où figure aussi un champ « Compteur ». Il fonctionne, mais il ne demande aucun motif et ne laisse donc aucune trace de la raison du changement.",
      "Préférez le bouton « Corriger » dès qu'il s'agit de rattraper une erreur : la correction est horodatée et conservée avec son motif et le nom de son auteur. Ce journal n'est pas consultable depuis l'application — il sert en cas de litige ou de contrôle, et notre support peut le retrouver.",
      "Le bouton « Corriger » n'apparaît que pour les véhicules sans boîtier GPS. Quand un boîtier remonte le kilométrage, c'est lui qui fait foi, et la ligne affiche alors une petite antenne à la place du bouton.",
      "Le bouton « Modifier » refuse une valeur inférieure au compteur actuel, avec le message « Un compteur ne recule pas : vérifiez la valeur. » C'est une protection contre les fautes de frappe.",
      "« Corriger » est le seul moyen de faire baisser un compteur trop haut — après un import erroné, par exemple. Il exige un motif et garde l'ancienne et la nouvelle valeur."
    ],
    aRetenir: "Écrivez un motif utile — « faute de frappe à l'import du 03/09 » — plutôt que « erreur ». C'est ce texte qui expliquera l'écart si la question se pose plus tard."
  },

  // ----------------------------------------------------------------- Suivi GPS
  {
    id: 'suivre-en-direct',
    titre: 'Suivre vos véhicules en direct',
    module: 'monitoring',
    motsCles: ['carte', 'direct', 'temps reel', 'position', 'ou est', 'localiser', 'suivi', 'gps', 'camion', 'voiture', 'trouver'],
    resume: "Lire la carte du suivi en direct et comprendre l'état de chaque véhicule.",
    captures: [
      { fichier: 'suivi-carte.png', legende: "La liste des véhicules à gauche, la carte à droite, et la légende des quatre états." },
    ],
    etapes: [
      "Ouvrez « Suivi en direct » dans le menu.",
      "Le panneau de gauche liste vos véhicules ; la carte affiche leur dernière position connue.",
      "Cliquez sur un véhicule dans la liste pour déplier sa fiche, ou sur son marqueur sur la carte pour ouvrir sa bulle d'information.",
      "Utilisez « Centrer sur les véhicules » pour recadrer la carte sur l'ensemble de la flotte."
    ],
    paragraphes: [
      "Quatre états existent, avec un code couleur constant : vert pour un véhicule en mouvement, orange au ralenti (contact mis, à l'arrêt), rouge stationné (contact coupé) et gris hors ligne.",
      "Attention à un détail de vocabulaire : l'état vert est écrit « En mouvement » dans la légende, les filtres et la bulle de carte, mais « En marche » dans la fiche dépliante du véhicule. Il s'agit du même état.",
      "« Hors ligne » signifie qu'aucune donnée n'est arrivée depuis au moins trente minutes. Le véhicule roule peut-être : c'est la communication du boîtier qui manque (zone sans réseau, boîtier débranché, batterie).",
      "La fiche dépliante donne la vitesse actuelle, la vitesse maximale sur 24 h, le carburant, la température moteur, la batterie et le compteur. « N/A » signifie que le boîtier ne remonte pas cette information — tous les modèles ne mesurent pas tout."
    ],
    video: { titre: 'Lire la carte du suivi en direct', url: '' }
  },
  {
    id: 'partager-position',
    titre: "Partager la position d'un véhicule",
    module: 'monitoring',
    motsCles: ['partager', 'position', 'whatsapp', 'lien', 'maps', 'remorquage', 'depanneur'],
    resume: "Envoyer la position d'un véhicule à quelqu'un qui n'a pas Calypso.",
    etapes: [
      "Sur la carte du suivi en direct, cliquez sur le marqueur du véhicule.",
      "Dans la bulle, repérez « Partager la position ».",
      "Choisissez « Ouvrir dans Maps », « Envoyer via WhatsApp » ou « Copier le lien »."
    ],
    paragraphes: [
      "Le lien est fait pour un destinataire qui n'a pas l'application — un dépanneur, un remorqueur, un client. Le QR code, lui, ouvre l'application Calypso directement sur le véhicule."
    ]
  },
  {
    id: 'rejouer-trajet',
    titre: "Rejouer le trajet d'une journée",
    module: 'playback',
    motsCles: ['playback', 'tracer', 'historique', 'trajet', 'rejeu', 'parcours', 'hier', 'itineraire'],
    resume: "Revoir où un véhicule est passé, à quelle heure et à quelle vitesse.",
    captures: [
      { fichier: 'playback-trace.png', legende: "Le tracé, les quatre indicateurs et la chronologie des trajets et arrêts." },
    ],
    etapes: [
      "Ouvrez « Tracer Playback » dans le menu.",
      "Choisissez le véhicule dans la liste « -- Choisir un véhicule -- ».",
      "Indiquez la date et l'heure de début, puis celles de fin.",
      "Cliquez sur « Lancer le tracé ».",
      "Utilisez les commandes de lecture pour avancer, et les vitesses « 0.5x » à « 8x » pour accélérer le rejeu."
    ],
    paragraphes: [
      "Une fois le tracé chargé, quatre indicateurs résument la période : « Distance », « V. max », « Vitesse » moyenne et temps de « Conduite ».",
      "La chronologie « Historique » découpe la journée en « Trajet » et « Arrêt », avec la durée et la distance de chacun — c'est le moyen le plus rapide de repérer un arrêt anormalement long.",
      "Le rejeu emploie un état supplémentaire, « Arrêt trafic », qui correspond à un arrêt bref moteur tournant (feu rouge, bouchon)."
    ],
    aRetenir: "Si le tracé est vide, élargissez la plage horaire : le véhicule n'a peut-être pas roulé sur le créneau demandé.",
    video: { titre: 'Rejouer un trajet', url: '' }
  },

  // ------------------------------------------------------------------ Rapports
  {
    id: 'premier-rapport',
    titre: 'Générer votre premier rapport',
    module: 'reports',
    motsCles: ['rapport', 'generer', 'executer', 'export', 'excel', 'pdf', 'csv', 'editer', 'imprimer'],
    resume: "Choisir un rapport, le lancer, puis l'exporter.",
    captures: [
      { fichier: 'rapports-criteres.png', legende: "Le panneau de critères : type de rapport, véhicule et période." },
      { fichier: 'rapports-exports.png', legende: "Les boutons Excel, PDF et CSV, actifs une fois le rapport généré." },
    ],
    etapes: [
      "Ouvrez « Rapports » dans le menu.",
      // Exemple commun à TOUTES les offres (report_costs ouvert sur les cinq
      // plans) : « Rapport de trajets » n'existe pas sans boîtier GPS.
      "Dans « Type de rapport », choisissez par exemple « Réparations véhicules ».",
      "Sélectionnez le véhicule, ou cochez « Département » pour raisonner par service.",
      "Choisissez la période : « Aujourd'hui », « Semaine », « Mois » ou « Personnalisé » avec « Date début » et « Date fin ».",
      "Cliquez sur « Exécuter ».",
      "Une fois le résultat affiché, exportez-le avec « Excel », « PDF » ou « CSV »."
    ],
    paragraphes: [
      "La liste « Type de rapport » ne propose que les rapports compris dans votre abonnement et autorisés par votre profil.",
      "Les trois boutons d'export restent grisés tant que le rapport n'a pas été généré : c'est normal, lancez d'abord « Exécuter ».",
      "Si un avertissement « Sélectionnez un type de rapport » ou « Sélectionnez un véhicule » s'affiche, c'est qu'il manque un critère obligatoire.",
      "Le bouton « Effacer » remet tous les critères à zéro sans toucher aux données."
    ],
    aRetenir: "Un rapport reflète les données présentes en base. S'il paraît vide ou incomplet, vérifiez d'abord la période, puis que les saisies (pleins, dépenses) ont bien été faites.",
    video: { titre: 'Générer et exporter un rapport', url: '' }
  },
  {
    id: 'choisir-rapport',
    titre: 'Quel rapport choisir ?',
    module: 'reports',
    // Les mots « vitesse », « arrêt », « comportement » ne sont PAS des mots-clés :
    // ils ne valent que pour les offres avec boîtier, et le texte qui les porte
    // n'est rendu qu'à ceux qui ont ces rapports (paragraphesConditionnels).
    motsCles: ['quel rapport', 'liste', 'cout', 'rapports disponibles', 'choisir'],
    resume: "Repères pour s'orienter parmi les rapports disponibles.",
    // Un repère par rapport, montré seulement si CE rapport est ouvert à ce
    // client : l'offre GPA ouvre le module Rapports mais ferme les rapports GPS
    // un à un (relecture du 22/09/2026).
    paragraphesConditionnels: [
      { texte: "La liste « Type de rapport » ne montre que les rapports compris dans votre abonnement et autorisés par votre profil. Ceux qui vous sont ouverts :" },
      { rapports: ['trips'], texte: "« Rapport de trajets » : où un véhicule est passé, trajet par trajet." },
      { rapports: ['stops'], texte: "« Rapport des arrêts » : où et combien de temps un véhicule s'est arrêté." },
      { rapports: ['daily'], texte: "« Rapport journalier » : l'activité d'un véhicule, jour par jour." },
      { rapports: ['mileage'], texte: "« Rapport kilométrique » : les kilomètres parcourus, au jour le jour." },
      { rapports: ['mileage_period'], texte: "« Kilométrage par période » : les kilomètres agrégés par heure, par jour ou par mois." },
      { rapports: ['speed'], texte: "« Rapport de vitesse » : les vitesses relevées par le boîtier." },
      { rapports: ['speed_infraction'], texte: "« Infractions vitesse » : les dépassements de la limite que vous fixez." },
      { rapports: ['driving_behavior'], texte: "« Comportement conduite » : freinages et accélérations brusques." },
      { rapports: ['fuel'], texte: "« Consommation carburant » : l'analyse de la consommation de chaque véhicule." },
      { rapports: ['monthly_fuel'], texte: "« Consommation carburant mensuel » : les litres par département, mois par mois." },
      { rapports: ['maintenance'], texte: "« Coûts maintenance » : les entretiens réalisés et leur coût." },
      { rapports: ['costs'], texte: "« Réparations véhicules » : les réparations, avec la main-d'œuvre et les pièces." },
      { rapports: ['operating_cost'], texte: "« Coût d'exploitation réel » : toutes les dépenses cumulées, ramenées au kilomètre." },
      { rapports: ['cost_evolution'], texte: "« Évolution des coûts » : les dépenses d'un véhicule, mois par mois." },
      { rapports: ['cost_ranking'], texte: "« Véhicules les plus coûteux » : le palmarès du parc." }
    ],
    aRetenir: "« Coûts maintenance » ne contient que les entretiens. Les réparations sont dans « Réparations véhicules ». Voir l'article sur la différence entre les deux."
  },

  // --------------------------------------------------------------- Maintenance
  {
    id: 'entretien-ou-reparation',
    titre: 'Entretien ou réparation : ne pas confondre',
    module: 'maintenance',
    motsCles: ['entretien', 'reparation', 'maintenance', 'panne', 'vidange', 'difference', 'saisir'],
    resume: "Deux notions distinctes dans Calypso, deux écrans, deux rapports.",
    paragraphes: [
      "Un entretien est préventif et planifié : vidange, révision des 20 000 km, contrôle technique. Il se saisit comme une dépense d'entretien et alimente le rapport « Coûts maintenance ».",
      "Une réparation corrige une panne ou un dommage : freinage, électricité, carrosserie, pneumatique. Elle se saisit dans l'écran « Réparations » et alimente les rapports « Réparations véhicules » et « Fréquence des réparations ».",
      "Les deux ne se mélangent jamais. Saisir une réparation comme un entretien fausse le suivi de maintenance préventive, et la facture se retrouve comptée deux fois dans « Coût d'exploitation réel »."
    ],
    aRetenir: "Panne survenue = réparation. Opération prévue au calendrier ou au kilométrage = entretien."
  },
  {
    id: 'planifier-entretien',
    titre: 'Planifier les entretiens récurrents',
    module: 'maintenance',
    motsCles: ['planifier', 'entretien', 'programmable', 'vidange', 'periodicite', 'rappel', 'echeance', 'modele', 'intervalle'],
    resume: "Définir un entretien qui revient, et l'affecter aux véhicules concernés.",
    captures: [
      { fichier: 'entretien-modele.png', legende: "Le modèle d'entretien : intervalle en kilomètres, en mois, et seuils d'alerte." },
    ],
    etapes: [
      "Ouvrez « Maintenance » > « Entretiens » dans le menu.",
      "Cliquez sur « Nouveau modele ».",
      "Donnez un « Nom » (par exemple « Vidange moteur ») et une « Categorie » — les deux sont obligatoires.",
      "Renseignez l'« Intervalle (km) », l'« Intervalle (mois) », ou les deux.",
      "Ajustez les « Seuils d'alerte » si besoin, puis enregistrez.",
      "Revenez à la liste et cliquez sur « Affecter » pour appliquer ce modèle aux véhicules."
    ],
    paragraphes: [
      "Un modèle sans aucun intervalle ne peut pas être enregistré : c'est la périodicité qui fait tout l'intérêt de l'entretien préventif.",
      "Si vous saisissez les deux intervalles, c'est le premier atteint qui déclenche : 10 000 km ou 12 mois, selon ce qui arrive en premier.",
      "Les « Seuils d'alerte » décident du moment où vous êtes prévenu, en kilomètres restants et en jours restants, avec un niveau d'alerte et un niveau critique.",
      "Le tableau affiche ensuite pour chaque véhicule la progression, les kilomètres restants et un statut : « OK », « A prevoir », « Imminent », « Critique » ou « En retard »."
    ],
    aRetenir: "Un modèle déjà utilisé ne se supprime pas sans effacer l'historique des entretiens réalisés. Désactivez-le plutôt : il cesse d'être surveillé, l'historique et les dépenses restent.",
    video: { titre: 'Créer un entretien programmable', url: '' }
  },
  {
    id: 'marquer-entretien-fait',
    titre: 'Enregistrer un entretien réalisé',
    module: 'maintenance',
    motsCles: ['entretien', 'fait', 'realise', 'vidange', 'facture', 'garage', 'effectue', 'marquer'],
    resume: "Déclarer l'entretien effectué pour recaler la prochaine échéance.",
    etapes: [
      "Dans l'écran « Entretiens » (menu « Maintenance »), repérez la ligne du véhicule et cliquez sur « Marquer fait ».",
      "Saisissez la « Date » et le « Compteur » — les deux sont obligatoires.",
      "Choisissez le « Fournisseur / Garage » si vous le suivez.",
      "Détaillez la facture ligne par ligne, puis cliquez sur « Confirmer »."
    ],
    paragraphes: [
      "Le compteur est obligatoire parce qu'il sert à recalculer la prochaine échéance. Une valeur inférieure au compteur actuel est refusée : un compteur ne recule pas.",
      "Chaque ligne de facture doit être rattachée à un type d'entretien. Une ligne laissée en saisie libre n'est pas enregistrée — l'écran le signale.",
      "Si le véhicule bénéficie d'entretiens offerts par le concessionnaire, cochez « Appliquer le crédit gratuit » : le coût passe à zéro et le compteur d'entretiens offerts diminue.",
      "L'écran compare ensuite le coût estimé du modèle au coût réel de la facture, et affiche l'écart."
    ],
    aRetenir: "Cet écran n'a pas de bouton pour joindre une facture. Si vous scannez le document, son lien est conservé dans les « Notes », en bas du formulaire."
  },
  {
    id: 'saisir-reparation',
    titre: 'Enregistrer une réparation',
    module: 'maintenance',
    motsCles: ['reparation', 'panne', 'garage', 'piece', 'main oeuvre', 'facture', 'reparer', 'casse', 'annuler', 'annulee', 'statut'],
    resume: "Saisir une intervention curative, avec ses pièces et sa main-d'œuvre.",
    captures: [
      { fichier: 'reparation-formulaire.png', legende: "Les pièces détachées se saisissent ligne par ligne, sous la description." },
    ],
    etapes: [
      "Ouvrez « Réparations » dans le menu Maintenance.",
      "Cliquez sur « Nouvelle reparation ».",
      "Choisissez le véhicule et la « Date » — les deux seuls champs obligatoires.",
      "Précisez le « Type d'intervention », la description et le numéro de facture.",
      "Ajoutez les pièces une par une (désignation, référence, quantité, prix unitaire) et le coût de main-d'œuvre.",
      "Cliquez sur « Enregistrer »."
    ],
    paragraphes: [
      "Les types d'intervention sont « Électrique », « Mécanique », « Freinage », « Pneumatique », « Carrosserie » et « Autres ». Laissé sur « Non précisé », le type est déduit de la description.",
      "Une réparation est enregistrée « Terminée ». En modification, le « Statut » peut passer à « En attente », « En cours » ou « Annulée ». Une réparation annulée reste dans la liste, mais n'est plus comptée dans les coûts — ni tableau de bord, ni rapports, ni Dépenses : c'est la bonne façon d'écarter une facture non due sans perdre son historique.",
      "Le « Compteur » est facultatif et pré-rempli avec le compteur actuel du véhicule. S'il est saisi, il fait avancer le compteur du véhicule, et donc les échéances d'entretien au compteur : relisez-le, une faute de frappe ne se rattrape qu'avec « Corriger » dans « Véhicules ». Une réparation annulée ne fait pas avancer le compteur.",
      "Une réparation issue d'un dossier de sinistre porte le badge « Sinistre » et ne se supprime pas depuis cet écran : elle se retire depuis le dossier."
    ],
    aRetenir: "Comme pour les entretiens, il n'y a pas de champ pour joindre la facture : le lien du document scanné est rangé dans les « Notes ».",
    video: { titre: 'Enregistrer une réparation', url: '' }
  },

  // ------------------------------------------------------------------ Carburant
  {
    id: 'saisir-plein',
    titre: 'Saisir un plein de carburant',
    module: 'carburant',
    motsCles: ['carburant', 'plein', 'gasoil', 'essence', 'litres', 'ticket', 'station', 'facture', 'pompe'],
    resume: "Enregistrer un plein, à la main ou en scannant le ticket.",
    captures: [
      { fichier: 'carburant-saisie.png', legende: "L'onglet « Saisie Manuelle » et ses champs obligatoires." },
    ],
    etapes: [
      "Ouvrez « Maintenance » > « Carburants » dans le menu, onglet « Saisie Manuelle ».",
      "Choisissez le « Matricule Véhicule » et le « Type Carburant ».",
      "Indiquez la « Date Facture ».",
      "Saisissez soit le volume et le prix par litre, soit directement le « Montant Total ».",
      "Renseignez le « Compteur ».",
      "Cliquez sur « Enregistrer »."
    ],
    paragraphes: [
      "Deux façons de chiffrer le plein : volume plus prix au litre, ou montant total. L'une des deux suffit, et le total se calcule tout seul si vous donnez les deux premiers.",
      "Le bouton « Scanner un ticket » lit le document et pré-remplit les champs. Relisez toujours : quand la plaque ou le type de carburant n'a pas pu être lu avec certitude, l'écran vous le signale et vous demande de choisir.",
      "L'onglet « Import Excel » permet de charger des factures en lot, avec une correspondance de colonnes à régler une fois."
    ],
    aRetenir: "Le compteur est ce qui rend la consommation calculable. Sans boîtier GPS, ce sont vos saisies — pleins, entretiens, réparations, import — qui font avancer le compteur du véhicule.",
    video: { titre: 'Saisir un plein', url: '' }
  },
  {
    id: 'suivre-consommation',
    titre: 'Suivre la consommation réelle',
    module: 'carburant',
    motsCles: ['consommation', 'litres', 'moyenne', 'cout au km', 'surconsommation', 'l 100'],
    resume: "Lire les litres aux 100 km et repérer les saisies qui faussent le calcul.",
    paragraphes: [
      "L'onglet « Consommation » de l'écran « Carburants » donne, par véhicule, le nombre de pleins, la distance, les litres, les litres aux 100 km et le coût au kilomètre.",
      "Le calcul est simple et assumé : litres achetés divisés par les kilomètres relevés entre le premier et le dernier plein de la période. Avec peu de pleins, la lecture est donc légèrement majorée.",
      "Deux avertissements peuvent apparaître. « Relevés compteur incohérents ignorés » signale une faute de frappe probable sur un kilométrage : corrigez-la dans l'onglet « Historique ». « Pleins sans relevé compteur » signale des pleins saisis sans kilométrage, donc inexploitables."
    ],
    // Rapport bâti sur la jauge du boîtier (report_fuel) : fermé sans GPS.
    paragraphesConditionnels: [
      { rapports: ['fuel_comparison'], texte: "Le rapport « Carburant réel vs GPS » superpose vos pleins et la courbe de niveau remontée par le boîtier : c'est là que se voient les écarts anormaux." }
    ],
    aRetenir: "Un mois de saisie oublié rend la consommation de ce mois inutilisable. La régularité compte plus que la précision."
  },

  // ------------------------------------------------------------------- Dépenses
  {
    id: 'saisir-depense',
    titre: 'Enregistrer une dépense',
    module: 'costs',
    motsCles: ['depense', 'cout', 'facture', 'payer', 'montant', 'amende', 'peage', 'stationnement', 'carte grise', 'saisir'],
    resume: "Les dépenses d'un véhicule qui n'ont pas leur propre écran : péage, stationnement, amende, carte grise…",
    captures: [
      { fichier: 'depenses-formulaire.png', legende: "Le formulaire : véhicule, catégorie, date, description et montant." },
    ],
    etapes: [
      "Ouvrez « Dépenses » dans le menu Finances.",
      "Cliquez sur « Nouvelle dépense ».",
      "Choisissez le « Véhicule », la « Catégorie » et la « Date » — toujours obligatoires.",
      "Saisissez la « Description » et le « Montant ».",
      "Cliquez sur « Enregistrer »."
    ],
    paragraphes: [
      "Les catégories proposées sont celles qui n'ont pas d'écran à elles : carte grise, autorisation de transport, péage, stationnement, amende, avoir fournisseur, autre. Un plein se saisit dans « Carburant », un entretien dans « Entretiens », une réparation dans « Réparations », et l'assurance, la visite technique ou la vignette se règlent depuis « Échéances » avec « Renouveler » : chacun de ces écrans met à jour ce qui va avec — compteur, prochaine échéance, date de validité.",
      "Toutes ces dépenses, quel que soit l'écran de saisie, reviennent dans la liste de cet écran et dans ses totaux.",
      "Le bouton « Enregistrer » reste inactif tant que le montant n'est pas renseigné.",
      "La case « Afficher les échéances à venir » ajoute les mensualités non encore échues. Elles sont visibles mais exclues des totaux, pour ne pas gonfler vos coûts du mois.",
      "Le menu « … » des lignes d'échéance permet de « Marquer payée », de joindre une quittance, ou d'« Ignorer cette échéance » — une échéance ignorée sort des totaux et du tableau de bord."
    ],
    aRetenir: "C'est le seul écran où l'on joint réellement un justificatif à une dépense. Sur les réparations et les entretiens, le lien du document est conservé dans les notes."
  },
  {
    id: 'scanner-facture',
    titre: 'Scanner une facture',
    module: 'costs',
    motsCles: ['scanner', 'scan', 'facture', 'photo', 'ocr', 'lire', 'automatique', 'ia', 'saisir', 'ticket', 'credit', 'jetons', 'quota'],
    resume: "Laisser Calypso lire la facture, puis vérifier avant d'enregistrer.",
    etapes: [
      "Dans « Dépenses », cliquez sur « Scanner une facture ».",
      "Envoyez la photo ou le PDF du document.",
      "La fenêtre « Vérifier la facture » s'ouvre avec les champs pré-remplis.",
      "Contrôlez le véhicule, la catégorie, la date et le montant, corrigez si besoin.",
      "Cliquez sur « Enregistrer la dépense »."
    ],
    paragraphes: [
      "Le scan propose aussi le détail ligne par ligne de la facture. Si la somme des lignes ne correspond pas au total, l'écran le signale — c'est souvent une ligne oubliée ou une remise.",
      "La barre « Crédit IA » à côté du bouton montre la part du crédit IA du mois déjà utilisée par toute votre société : scans, assistant, rapports IA. À 100 %, le bouton de scan est grisé jusqu'à la recharge du 1er du mois ; survolez la barre pour connaître le nombre de scans restants.",
      "Une facture d'avoir n'est pas une dépense : enregistrez-la en catégorie « Avoir fournisseur », sinon elle s'ajoute à vos coûts au lieu de les diminuer."
    ],
    aRetenir: "Le scan fait gagner du temps, il ne dispense pas de relire. Vérifiez systématiquement le véhicule et le montant avant d'enregistrer."
  },
  {
    id: 'credit-ia',
    titre: 'Comprendre la barre « Crédit IA »',
    module: 'general',
    motsCles: ['credit', 'ia', 'jetons', 'quota', 'intelligence artificielle', 'assistant', 'scan', 'epuise', 'grise', 'recharge', 'pourcentage'],
    resume: "Ce que mesure la barre « Crédit IA », et pourquoi un bouton d'IA peut être grisé.",
    paragraphes: [
      "Les fonctions d'intelligence artificielle de Calypso — scan des factures, assistant, rapport IA de la flotte, explications de consommation — puisent toutes dans un même crédit mensuel, commun à toute votre société.",
      "La barre « Crédit IA », posée à côté des boutons qui appellent l'IA, montre en pourcentage la part de ce crédit déjà utilisée ce mois-ci. Elle est verte, passe à l'orange à 70 % et au rouge à 90 %.",
      "Survolez la barre pour le détail : le pourcentage utilisé, les jetons consommés sur le crédit du mois, la date de recharge et, à côté du bouton de scan, une estimation des scans restants.",
      "À 100 %, les boutons d'IA sont grisés jusqu'à la recharge, le 1er du mois suivant.",
      "« IA désactivée » à la place de la barre signifie que l'IA n'est pas ouverte pour votre société."
    ],
    aRetenir: "Le crédit est partagé : la barre ne compte pas seulement vos scans, mais tout ce que votre société a demandé à l'IA ce mois-ci. Quelques scans et beaucoup de questions à l'assistant peuvent suffire à l'épuiser."
  },

  // --------------------------------------------------------------- Fournisseurs
  {
    id: 'ajouter-fournisseur',
    titre: 'Ajouter un garage ou un fournisseur',
    module: 'suppliers',
    motsCles: ['fournisseur', 'garage', 'prestataire', 'assureur', 'atelier', 'contact', 'ajouter'],
    resume: "Constituer votre carnet d'adresses de prestataires.",
    etapes: [
      "Ouvrez « Fournisseurs » dans le menu Finances.",
      "Cliquez sur « Nouveau fournisseur ».",
      "Renseignez le « Nom », le « Type », l'« Adresse », la « Ville » et le « Téléphone » — tous obligatoires.",
      "Ajoutez éventuellement une note de 0 à 5 pour qualifier la prestation.",
      "Cliquez sur « Enregistrer »."
    ],
    paragraphes: [
      "Les types disponibles couvrent le garage, l'assurance, le vendeur, les pièces détachées, le carburant, les pneumatiques, le service et le général.",
      "Une fois créés, ces fournisseurs sont proposés dans les listes des entretiens, des réparations et des renouvellements de documents : vous ne ressaisissez plus leurs coordonnées.",
      "Un fournisseur que vous n'utilisez plus se passe en « Inactif » : il disparaît des listes sans effacer l'historique qui le cite."
    ]
  },

  // -------------------------------------------------------------------- Zones
  {
    id: 'creer-geofence',
    titre: 'Créer une zone (géofence)',
    module: 'geofences',
    motsCles: ['zone', 'geofence', 'geofencing', 'perimetre', 'alerte', 'entree', 'sortie', 'chantier', 'depot'],
    resume: "Délimiter un lieu et être alerté aux entrées et sorties.",
    captures: [
      { fichier: 'geofence-carte.png', legende: "Le tracé d'une zone sur la carte, avec sa marge autour du lieu réel." },
    ],
    etapes: [
      "Ouvrez « Exploitation » > « Géofencing » dans le menu.",
      "Créez une zone et dessinez-la sur la carte autour du lieu voulu.",
      "Nommez-la clairement (« Dépôt Tunis », « Chantier Sfax »).",
      "Choisissez les véhicules concernés et enregistrez."
    ],
    paragraphes: [
      "À chaque franchissement, un événement « Entrée dans la zone » ou « Sortie de la zone » est enregistré et apparaît dans la cloche de notifications.",
      "Une zone trop serrée déclenche des allers-retours d'alertes quand le véhicule stationne en bordure. Prévoyez une marge autour du lieu réel."
    ]
  },

  // ------------------------------------------------------------- Documents
  {
    id: 'echeances-documents',
    titre: 'Suivre les assurances et visites techniques',
    module: 'documents',
    motsCles: ['document', 'echeance', 'assurance', 'visite technique', 'expiration', 'rappel', 'vignette', 'expire'],
    resume: "L'écran qui regroupe toutes les dates de validité de votre parc.",
    captures: [
      { fichier: 'echeances-liste.png', legende: "Les trois compteurs cliquables et le délai affiché sur chaque ligne." },
    ],
    paragraphes: [
      "« Échéances » est un écran de surveillance : il rassemble les dates de validité de tous vos véhicules. On n'y crée pas un document — les dates se saisissent sur la fiche du véhicule.",
      "Trois compteurs cliquables filtrent la liste : « Expirés », « Expire < 30j » et « En règle ».",
      "Chaque ligne indique le délai en clair : « Expiré depuis … jour(s) », « Expire aujourd'hui », « Expire demain » ou « Dans … jours ».",
      "Le statut « Non renseignée » est à part : il signale un véhicule sans date saisie. Ces lignes ne sont pas comptées comme expirées, et pourtant ce sont les plus risquées — personne ne sera prévenu.",
      "Les types suivis sont l'assurance, la vignette, la visite technique, la carte grise, l'autorisation de transport et le permis du conducteur."
    ],
    aRetenir: "Le seuil d'alerte est de 30 jours par défaut. Il se règle document par document, dans la fenêtre de renouvellement, avec le champ « Rappel avant (jours) »."
  },
  {
    id: 'renouveler-document',
    titre: 'Renouveler une assurance ou une visite',
    module: 'documents',
    motsCles: ['renouveler', 'assurance', 'visite', 'vignette', 'payer', 'quittance', 'scanner', 'prolonger'],
    resume: "Enregistrer le renouvellement et la dépense correspondante, en une fois.",
    etapes: [
      "Dans « Échéances », repérez la ligne concernée et cliquez sur « Renouveler ».",
      "Renseignez la « Date de paiement » et la « Nouvelle date d'expiration » — les deux sont obligatoires.",
      "Indiquez le montant payé et le fournisseur si vous voulez suivre la dépense.",
      "Ajustez le « Rappel avant (jours) » si 30 jours ne vous convient pas.",
      "Cliquez sur « Enregistrer le renouvellement »."
    ],
    paragraphes: [
      "Les raccourcis « +1 an », « +6 mois » et « +2 ans » calculent la nouvelle échéance à votre place.",
      "Le bouton « Scanner la quittance » lit le montant, la date, le fournisseur et le numéro directement sur le document photographié. Relisez toujours ce qui a été pré-rempli : si rien n'a pu être lu, l'écran vous le dit et vous saisissez à la main.",
      "Le bouton « Historique » d'une ligne retrace tous les renouvellements passés, avec le total dépensé."
    ],
    aRetenir: "« Modifier l'échéance » et « Renouveler » ne font pas la même chose : le premier corrige une date mal saisie sans créer de dépense, le second enregistre un vrai renouvellement payé."
  },

  // ----------------------------------------------------------------- Sinistres
  {
    id: 'declarer-sinistre',
    titre: 'Déclarer un sinistre',
    module: 'accidents',
    motsCles: ['accident', 'sinistre', 'declarer', 'choc', 'degats', 'constat', 'assurance'],
    resume: "Créer un dossier pour un accident que le système n'a pas détecté seul.",
    captures: [
      { fichier: 'sinistre-manuel.png', legende: "La fenêtre « Ajouter un sinistre manuel » : véhicule et date suffisent." },
    ],
    etapes: [
      "Ouvrez « Rapports d'accident » dans le menu Incidents.",
      "Cliquez sur « Ajouter un sinistre manuel ».",
      "Choisissez le « Véhicule » et la « Date / heure » — les deux seuls champs obligatoires.",
      "Complétez la sévérité, le lieu, la description et le coût estimé si vous les connaissez.",
      "Cliquez sur « Créer le sinistre »."
    ],
    paragraphes: [
      "Les accidents détectés automatiquement par le boîtier apparaissent déjà dans la liste. La déclaration manuelle sert aux cas que la détection a manqués : panne du boîtier, véhicule hors couverture, ou véhicule sans GPS.",
      "Un dossier créé à la main porte le badge « Manuel ».",
      "Vous pouvez joindre le PDF de l'expert dès la création, ou plus tard depuis la fiche du sinistre."
    ],
    video: { titre: 'Déclarer un sinistre', url: '' }
  },
  {
    id: 'suivre-sinistre',
    titre: 'Suivre un dossier de sinistre',
    module: 'accidents',
    motsCles: ['sinistre', 'expertise', 'devis', 'reparation', 'assurance', 'indemnisation', 'tiers', 'suivi'],
    resume: "De la confirmation de l'accident au remboursement de l'assurance.",
    paragraphes: [
      "Un dossier détecté automatiquement attend une décision : « Confirmer l'accident » ou « Fausse alerte ». Cette décision est réservée aux administrateurs de la société.",
      "Une fois le dossier confirmé, la section « Suivi de l'accident » déroule les phases : dégâts visibles, expertise de l'assurance, devis du garage, réparation, suivi assurance, et tiers impliqués.",
      "Chaque phase se remplit indépendamment, au fil de l'eau. Rien n'est obligatoire : vous complétez ce que vous savez, quand vous le savez.",
      "Deux reports sont automatiques : la réparation enregistrée apparaît dans « Réparations » et dans « Dépenses » ; le montant approuvé par l'assurance revient en remboursement dans « Dépenses ».",
      "Le bloc « Tiers impliqués » recueille les coordonnées et l'assurance des autres véhicules — indispensable pour le constat."
    ],
    aRetenir: "Ne confondez pas les deux PDF : le « Rapport PDF » est produit par Calypso et se régénère, le « Rapport d'expertise » est votre document, conservé en pièce jointe. Joindre un constat exige un dossier confirmé et un compte administrateur."
  },
  {
    id: 'remorquages',
    titre: 'Repérer un véhicule remorqué ou volé',
    module: 'monitoring',
    motsCles: ['remorquage', 'vol', 'depanneur', 'fourriere', 'deplace', 'moteur coupe', 'enleve'],
    resume: "La détection d'un véhicule déplacé alors que son moteur est coupé.",
    paragraphes: [
      "Calypso surveille un cas précis : un véhicule qui se déplace à plus de 15 km/h alors que son moteur est coupé. C'est la signature d'un remorquage — dépanneuse, fourrière, ou vol.",
      "L'écran se trouve à l'adresse /remorquages. Il n'a pas d'entrée dans le menu : tapez l'adresse ou mettez-la en favori.",
      "Chaque détection indique le véhicule, l'heure de début, la vitesse maximale, la distance et le lieu de départ. Le bouton « Marquer examiné » sert à signaler que vous avez traité le cas.",
      "Il n'y a rien à saisir : la page ne fait que constater."
    ]
  },

  // --------------------------------------------------------------- Chauffeurs
  {
    id: 'ajouter-chauffeur',
    titre: 'Ajouter un chauffeur',
    module: 'employees',
    motsCles: ['chauffeur', 'conducteur', 'employe', 'salarie', 'ajouter', 'creer', 'permis', 'affecter', 'compte', 'application'],
    resume: "Créer la fiche d'un chauffeur et l'affecter à un véhicule.",
    captures: [
      { fichier: 'chauffeur-formulaire.png', legende: "Seuls le prénom et le nom sont obligatoires ; la date d'expiration du permis déclenche les alertes." },
    ],
    etapes: [
      "Ouvrez « Chauffeurs » dans le menu Exploitation.",
      "Cliquez sur « Nouveau chauffeur ».",
      "Renseignez le « Prénom » et le « Nom » — ce sont les deux seuls champs obligatoires.",
      "Complétez si besoin l'e-mail, le téléphone, la pièce d'identité et la date d'embauche.",
      "Dans « Permis de conduire », saisissez le numéro, la catégorie et surtout la « Date d'expiration ».",
      "Choisissez éventuellement le « Véhicule » affecté, puis cliquez sur « Créer le chauffeur »."
    ],
    paragraphes: [
      "La rubrique s'appelle « Chauffeurs », pas « Conducteurs » ni « Employés ».",
      "La catégorie de permis propose « B - Véhicule léger », « C - Poids lourd », « D - Transport en commun », « CE - Super poids lourd » et « DE - Transport + remorque ».",
      "Le champ « Statut » (« Actif » ou « Inactif ») n'apparaît qu'en modification : un chauffeur créé est actif d'office.",
      "La fiche chauffeur ne donne pas accès à l'application mobile. Pour qu'il reçoive ses tournées sur son téléphone, cliquez sur « Créer son compte » dans sa ligne : le formulaire Utilisateurs s'ouvre pré-rempli, case « 🚚 Chauffeur (application mobile) » cochée. La ligne affiche ensuite « Application : active »."
    ],
    aRetenir: "La recherche de cet écran ne porte que sur le nom et l'e-mail. Chercher une plaque ou un numéro de permis ne donnera rien.",
    video: { titre: 'Ajouter un chauffeur', url: '' }
  },
  {
    id: 'permis-expiration',
    titre: 'Être prévenu qu\'un permis expire',
    module: 'employees',
    motsCles: ['permis', 'expiration', 'echeance', 'rappel', 'renouveler', 'alerte', 'validite'],
    resume: "Le suivi des dates de permis et le compteur des renouvellements à venir.",
    paragraphes: [
      "Dès qu'une « Date d'expiration » est saisie, la colonne « Échéance » affiche l'état du permis : « Permis valide », « Permis expire dans … j », « Permis expire aujourd'hui » ou « Permis expiré ».",
      "L'alerte devient orange à trente jours et rouge à quinze jours de l'échéance.",
      "En haut de l'écran, le compteur « Permis à renouveler » regroupe tous les chauffeurs dont le permis expire dans les trente jours. Cliquez dessus pour filtrer la liste.",
      "Le champ « Rappel avant (jours) » de la fiche permet d'avancer ou de reculer ce préavis, chauffeur par chauffeur."
    ],
    aRetenir: "Un permis sans date saisie ne déclenche aucune alerte. Le jour où il expire, personne ne sera prévenu."
  },

  // ----------------------------------------------------------------- Tournées
  {
    id: 'planifier-tournee',
    titre: 'Planifier une tournée',
    module: 'tours',
    motsCles: ['tournee', 'livraison', 'itineraire', 'planifier', 'trajet prevu', 'etape', 'arret', 'circuit', 'envoyer', 'chauffeur'],
    resume: "Préparer un itinéraire avec ses arrêts, puis le comparer au trajet réellement effectué.",
    captures: [
      { fichier: 'tournees-liste.png', legende: "L'écran « Tournees » : les compteurs par statut, et le bouton « Nouvelle tournee »." },
      { fichier: 'tournee-informations.png', legende: "Étape 1 « Informations » : le nom, le véhicule — le chauffeur rattaché est proposé — et la récurrence." },
      { fichier: 'tournee-itineraire.png', legende: "Étape 2 « Itineraire » : le départ, la destination, et « + Arret » pour les étapes intermédiaires. L'étape 3 « Estimation » apparaît dès que deux points sont placés." },
    ],
    etapes: [
      "Ouvrez « Exploitation » > « Tournées » dans le menu.",
      "Cliquez sur « Nouvelle tournee ».",
      "Étape « Informations » : donnez un « Nom » et choisissez le « Vehicule ». Le chauffeur rattaché au véhicule est proposé automatiquement dans « Chauffeur » ; vous pouvez en choisir un autre.",
      "Étape « Itineraire » : saisissez l'adresse de départ, puis la destination. Ajoutez des arrêts intermédiaires avec « + Arret », ou cliquez directement sur la carte.",
      "Étape « Estimation » : cliquez sur « Calculer l'itineraire » pour obtenir la distance, la durée et le carburant prévus.",
      "Cliquez sur « Creer la tournee » — ou sur « 📱 Enregistrer et envoyer » pour l'envoyer aussitôt sur le téléphone du chauffeur."
    ],
    paragraphes: [
      "Les libellés de cet écran sont écrits sans accents (« Tournees », « Vehicule », « Duree ») : c'est normal, ce n'est pas un défaut d'affichage.",
      "Une tournée exige au minimum deux points de passage, un départ et une destination. Avec un seul point, le bouton de création reste sans effet et aucun message ne l'explique : ajoutez la destination.",
      "« 📱 Enregistrer et envoyer » n'apparaît que si le chauffeur choisi a un compte de l'application : sous « Chauffeur », « 📱 Ce chauffeur a l'application » le confirme, « Pas de compte application — créez-le dans Utilisateurs » dit ce qui manque. Voir l'article « Envoyer une tournée au chauffeur ».",
      "Pour une tournée qui revient chaque semaine, choisissez la récurrence « Hebdomadaire » et cochez les jours concernés.",
      "Chaque arrêt accepte une durée de pause et une marge de retard tolérée, qui servent à juger si la tournée est en avance ou en retard."
    ],
    aRetenir: "Seule une tournée au statut « Planifiee » peut être modifiée. Une fois démarrée, elle ne peut plus qu'être annulée ou terminée.",
    video: { titre: 'Planifier une tournée', url: '' }
  },
  {
    id: 'suivre-tournee',
    titre: 'Suivre une tournée et comparer au prévu',
    module: 'tours',
    motsCles: ['tournee', 'suivi', 'retard', 'replay', 'rapport', 'ecart', 'reel', 'estime', 'telephone', 'boitier'],
    resume: "Voir où en est une tournée, puis mesurer l'écart entre le prévu et le réalisé.",
    paragraphes: [
      "Les compteurs du haut filtrent la liste : « Total », « Planifiees », « En cours », « Terminees ».",
      "La colonne « Envoi » dit où en est la tournée sur le téléphone du chauffeur : « Non envoyée », « Envoyée », « Ouverte », puis « Partie ».",
      "Sur une tournée en cours, la fiche affiche le suivi en direct et le prochain point à atteindre. Le bandeau de suivi dit d'où viennent les positions : « Suivi par boîtier » tant que le boîtier du véhicule émet, « Suivi par téléphone » quand le téléphone du chauffeur prend le relais, « Suivi interrompu depuis … » quand aucun des deux n'émet.",
      "Sur une tournée terminée, le bouton « ▶ Replay » rejoue le trajet sur la carte, et « 📄 Rapport » produit un PDF comparant le réel à l'estimé.",
      "Le tableau « Comparaison Estime vs Reel » donne trois colonnes : « Estime », « Reel » et « Ecart ». Sur la carte, le tracé « Calculé » est l'itinéraire théorique, le tracé « Réel » la trace GPS."
    ],
    aRetenir: "S'il manque des positions GPS sur la période, le replay affiche « Pas assez de positions GPS sur la fenêtre de cette tournée » : le boîtier n'a pas communiqué, la tournée a bien eu lieu."
  },
  {
    id: 'envoyer-tournee-chauffeur',
    titre: 'Envoyer une tournée au chauffeur',
    module: 'tours',
    motsCles: ['envoyer', 'renvoyer', 'telephone', 'application', 'mobile', 'chauffeur', 'tournee', 'smartphone', 'je pars', 'notification'],
    resume: "Faire arriver la tournée sur le téléphone du chauffeur, puis la suivre par le boîtier ou par son téléphone.",
    etapes: [
      "Vérifiez que le chauffeur a un compte de l'application : dans « Chauffeurs », sa ligne affiche « Application : active ». Sinon, créez-le d'abord (article « Créer le compte application d'un chauffeur »).",
      "À la création de la tournée, choisissez ce chauffeur dans « Chauffeur », puis cliquez sur « 📱 Enregistrer et envoyer ».",
      "Pour une tournée déjà créée, ouvrez-la et cliquez sur « 📱 Envoyer au chauffeur » — « Renvoyer au chauffeur » si elle a déjà été envoyée.",
      "Le message « Tournée envoyée » confirme l'envoi."
    ],
    paragraphes: [
      "Le bouton d'envoi n'existe que pour une tournée « Planifiee » ou « En cours ». Il reste grisé tant qu'aucun chauffeur n'est choisi, ou si le chauffeur n'a pas de compte application actif : son infobulle en donne la raison.",
      "« Envoyée, mais le chauffeur n'a pas encore ouvert l'application sur son téléphone » : la tournée est bien enregistrée, il la verra en ouvrant l'application.",
      "La colonne « Envoi » de la liste suit la tournée : « Non envoyée », « Envoyée », « Ouverte » (le chauffeur l'a ouverte dans l'application), puis « Partie ». Le détail de la tournée donne les heures : « Envoyée au chauffeur », « Ouverte sur le téléphone », « Départ signalé par le chauffeur ».",
      "Le chauffeur démarre la tournée depuis l'application avec « Je pars ».",
      "Pendant la tournée, le bandeau de suivi indique la source des positions : « Suivi par boîtier » tant que le boîtier du véhicule émet, « 📱 Suivi par téléphone » quand le téléphone du chauffeur prend le relais, avec la batterie du téléphone.",
      "Le chauffeur se connecte à l'application Calypso version 1.2 ou plus récente, avec l'e-mail et le mot de passe de son compte."
    ],
    aRetenir: "Un compte chauffeur ne donne aucun accès au site : le chauffeur ne voit que ses tournées, dans l'application."
  },

  // ---------------------------------------------------------- Gestion de flotte
  {
    id: 'departements',
    titre: 'Organiser le parc en départements',
    module: 'fleet_management',
    motsCles: ['departement', 'service', 'groupe', 'organiser', 'affecter', 'agence', 'flotte'],
    resume: "Regrouper les véhicules par service pour filtrer et comparer les coûts.",
    etapes: [
      "Ouvrez « Gestion de Flotte », onglet « Départements ».",
      "Cliquez sur « Ajouter », saisissez le « Nom du département » et enregistrez.",
      "Sur la ligne du département, utilisez « Affecter véhicules » pour y rattacher les véhicules."
    ],
    paragraphes: [
      "Une fois les départements en place, plusieurs rapports de coûts peuvent être produits par département plutôt que véhicule par véhicule.",
      "Un département qui contient des véhicules ne peut pas être supprimé : le bouton reste inactif. Retirez d'abord les véhicules."
    ]
  },
  {
    id: 'prix-carburant-limites',
    titre: 'Prix du carburant et limites de vitesse',
    module: 'fleet_management',
    motsCles: ['prix', 'carburant', 'litre', 'limite', 'vitesse', 'parametrer', 'estimation'],
    resume: "Les réglages qui alimentent les estimations de coût et les alertes de vitesse.",
    paragraphes: [
      "Onglet « Carburant » : « Ajouter un prix » enregistre un prix par litre pour un type de carburant. C'est ce prix qui sert au rapport « Estimation coûts carburant ».",
      "Onglet « Limites vitesse » : chaque véhicule peut avoir sa limite propre, et « Limite par défaut » s'applique aux nouveaux véhicules. « Appliquer à tous les véhicules » impose la même valeur à tout le parc.",
      "Les onglets « Limites vitesse », « Prix pièces » et « Contrôle à distance » n'apparaissent que pour les abonnements incluant le suivi GPS."
    ],
    aRetenir: "Sans prix par litre renseigné, les rapports d'estimation de coût carburant restent vides."
  },
  {
    id: 'emprunter-vehicule',
    titre: 'Prêter un véhicule à un collaborateur',
    module: 'fleet_management',
    // L'écran /emprunts n'existe que chez les loueurs (LocationCompanyGuard) :
    // une société de transport ne doit pas trouver cet article.
    typeSociete: 'location',
    motsCles: ['emprunt', 'preter', 'location', 'louer', 'rendre', 'retour', 'mise a disposition'],
    resume: "Enregistrer la sortie d'un véhicule et son retour, avec le kilométrage parcouru.",
    etapes: [
      "Cliquez sur votre nom en haut à droite, puis sur « Emprunts ». L'écran s'intitule « Emprunts Véhicules ».",
      "Cliquez sur « Nouvel Emprunt ».",
      "Choisissez le « Véhicule » — seul champ obligatoire — puis l'employé, le motif et la destination.",
      "Validez avec « Confirmer » : l'écran bascule sur l'onglet « En cours ».",
      "Au retour, cliquez sur « Retourner » dans l'onglet « En cours »."
    ],
    paragraphes: [
      "Le kilométrage de départ et celui d'arrivée sont relevés automatiquement depuis le GPS : vous n'avez rien à saisir.",
      "La liste ne propose que les véhicules ni loués ni déjà empruntés. Un véhicule absent de la liste est donc déjà sorti.",
      "L'onglet « Historique » conserve les emprunts terminés ou annulés, avec les kilomètres parcourus."
    ],
    aRetenir: "Si rien ne se passe au moment de confirmer, l'opération a échoué sans message d'erreur. Rechargez la page et vérifiez l'onglet « En cours » avant de recommencer."
  },

  // ------------------------------------------------------------- Utilisateurs
  {
    id: 'ajouter-utilisateur',
    titre: 'Donner un accès à un collègue',
    module: 'users',
    motsCles: ['utilisateur', 'collegue', 'acces', 'droit', 'permission', 'compte', 'inviter', 'role', 'administrateur'],
    resume: "Créer un compte pour quelqu'un de votre société et choisir précisément ce qu'il voit.",
    captures: [
      { fichier: 'utilisateur-permissions.png', legende: "L'étape « Permissions » : un module non coché reste inaccessible." },
    ],
    etapes: [
      "Ouvrez « Gestion des Utilisateurs », onglet « Comptes utilisateurs ».",
      "Cliquez sur « Nouvel Utilisateur ».",
      "Étape « 1 Général » : saisissez « Prénom », « Nom », « Email » et « Mot de passe » — tous obligatoires.",
      "Étape « 2 Permissions » : cochez les modules auxquels il a droit.",
      "Étape « 3 Véhicules » : cochez les véhicules qu'il pourra superviser.",
      "Cliquez sur « Créer »."
    ],
    paragraphes: [
      "Pour un chauffeur qui n'utilisera que l'application mobile, cochez « 🚚 Chauffeur (application mobile) » à l'étape « 1 Général » : les étapes « Permissions » et « Véhicules » disparaissent. Voir l'article « Créer le compte application d'un chauffeur ».",
      "La case « 👑 Administrateur de la société » donne tout : tous les véhicules et toutes les fonctionnalités. À réserver au responsable.",
      "Le tableau de bord reste toujours accessible, même sans aucune case cochée.",
      "Si vous cochez « 📊 Rapports », un sous-bloc « Types de rapports autorisés » apparaît : vous choisissez rapport par rapport ce que la personne peut éditer.",
      "En bas de l'étape « Permissions », le bloc « Alertes par email » définit les alertes d'échéance envoyées à son adresse : assurance, taxe de circulation, visite technique, entretien.",
      "Les droits se cumulent avec l'abonnement de la société : vous ne pouvez pas accorder un module qui n'y figure pas."
    ],
    aRetenir: "Un départ dans l'équipe ? Passez son statut à « Inactif » le jour même. C'est plus sûr et ça conserve l'historique de ses actions.",
    video: { titre: 'Créer un utilisateur et ses droits', url: '' }
  },
  {
    id: 'compte-chauffeur',
    titre: "Créer le compte application d'un chauffeur",
    module: 'users',
    motsCles: ['chauffeur', 'compte', 'application', 'mobile', 'telephone', 'tournee', 'smartphone', 'connexion', 'acces'],
    resume: "Donner à un chauffeur l'accès à l'application mobile, pour qu'il reçoive ses tournées sur son téléphone.",
    etapes: [
      "Depuis « Chauffeurs », cliquez sur « Créer son compte » dans la ligne du chauffeur : le formulaire Utilisateurs s'ouvre pré-rempli, case « 🚚 Chauffeur (application mobile) » déjà cochée.",
      "Ou, depuis « Gestion des Utilisateurs », cliquez sur « Nouvel Utilisateur » et cochez vous-même « 🚚 Chauffeur (application mobile) » à l'étape « 1 Général ».",
      "Renseignez « Prénom », « Nom », « Email » et « Mot de passe », et vérifiez la « Fiche chauffeur à relier ».",
      "Cliquez sur « Créer », puis communiquez au chauffeur son e-mail et son mot de passe."
    ],
    paragraphes: [
      "Un compte chauffeur n'a que l'étape « Général » : ni permissions ni véhicules à régler. Il n'a aucun accès au site et ne compte pas dans le quota d'utilisateurs de votre abonnement.",
      "La fiche reliée est celle que vous choisissez dans « Fiche chauffeur à relier » ; à défaut, celle qui porte le même e-mail, sinon une fiche neuve. Reliez la bonne : c'est elle qui porte le véhicule, le permis et les tournées.",
      "Le chauffeur se connecte avec l'application Calypso version 1.2 ou plus récente, avec l'e-mail et le mot de passe de ce compte. Il n'y voit que ses tournées.",
      "Dans « Chauffeurs », sa ligne affiche ensuite « Application : active »."
    ],
    aRetenir: "La case n'est pas proposée sur votre propre compte : devenu compte chauffeur, il perdrait aussitôt l'accès au site."
  },
  {
    id: 'alertes-email',
    titre: 'Envoyer les alertes à une adresse e-mail',
    module: 'users',
    motsCles: ['alerte', 'email', 'destinataire', 'assurance', 'visite', 'entretien', 'copie', 'notification'],
    resume: "Ajouter une adresse qui reçoit les alertes d'échéance, même sans compte Calypso.",
    etapes: [
      "Ouvrez « Gestion des Utilisateurs », onglet « Alertes par email ».",
      "Cliquez sur « Ajouter une adresse ».",
      "Saisissez l'« Adresse email » et choisissez le « Type d'alerte ».",
      "Enregistrez, puis utilisez « Tester » pour vérifier la réception."
    ],
    paragraphes: [
      "Les types disponibles sont « Assurance », « Taxe Circulation », « Visite Technique », « Entretien », « Permis » et, pour les flottes équipées GPS, « Accident ».",
      "Utile pour un comptable ou un assureur externe : l'adresse reçoit les alertes sans avoir de compte dans l'application.",
      "Sans aucune adresse configurée, les alertes partent par défaut aux administrateurs de la société."
    ]
  },

  // ------------------------------------------------------------ Tableau de bord
  {
    id: 'lire-tableau-de-bord',
    titre: 'Lire le tableau de bord',
    module: 'dashboard',
    motsCles: ['tableau de bord', 'accueil', 'indicateur', 'kpi', 'graphique', 'synthese', 'periode'],
    resume: "Les chiffres de la page d'accueil et comment changer la période analysée.",
    paragraphes: [
      "Le filtre en haut commande tout l'écran : « Aujourd'hui », « Hier », « Semaine », « Mois », « Année », ou « Personnalisé » avec « Du » et « Au ». Tous les chiffres suivent cette période.",
      "Le tableau de bord existe en deux versions selon votre offre. Avec le suivi par boîtier, il montre la flotte en direct, l'état des véhicules, les trajets et les scores de conduite.",
      "Sans suivi GPS (gestion de parc seule), il montre les coûts : « Coût total », « Coût d'achats », « Reste à payer leasing », « Interventions » et « Entretiens à venir », complétés par la répartition des coûts, l'évolution sur douze mois et le « Top 5 des véhicules par coût ».",
      "Si une tuile affiche « accès non autorisé », c'est que votre profil n'a pas le droit correspondant : le chiffre existe, il ne vous est pas ouvert."
    ],
    aRetenir: "« Coût total » est calculé hors achats de véhicules. Les achats ont leur propre tuile, pour ne pas écraser les coûts d'exploitation courants."
  },

  // ------------------------------------------------------------- Préférences
  {
    id: 'devise-unites',
    titre: 'Changer la devise, les unités ou la langue',
    module: 'general',
    motsCles: ['devise', 'monnaie', 'euro', 'dinar', 'unite', 'kilometre', 'langue', 'fuseau', 'date', 'litre'],
    resume: "Ces réglages sont dans « Mon profil », pas dans « Paramètres ».",
    etapes: [
      "Cliquez sur votre nom en haut à droite, puis « Mon profil ».",
      "Descendez à « Paramètres régionaux » pour la langue, le fuseau horaire, la monnaie et le format de date.",
      "Descendez à « Unités de mesure » pour la distance, la vitesse, le volume et la température.",
      "Cliquez sur « Enregistrer »."
    ],
    paragraphes: [
      "C'est le piège le plus fréquent : on cherche la devise dans « Paramètres », elle est dans « Mon profil ».",
      "Ces deux sections n'apparaissent que pour les offres incluant le suivi GPS.",
      "Le nom de l'entreprise n'est pas modifiable ici : il faut passer par votre interlocuteur Calypso."
    ]
  },
  {
    id: 'changer-mot-de-passe',
    titre: 'Changer son mot de passe',
    module: 'general',
    motsCles: ['mot de passe', 'changer', 'securite', 'modifier', 'password'],
    resume: "Depuis le profil ou depuis les paramètres, au choix.",
    etapes: [
      "Ouvrez « Mon profil », section « Mot de passe » (ou « Paramètres », onglet « Sécurité »).",
      "Saisissez le « Mot de passe actuel », puis le « Nouveau mot de passe » et sa confirmation.",
      "Cliquez sur « Changer le mot de passe »."
    ],
    paragraphes: [
      "Six caractères minimum. Ce formulaire est indépendant du bouton « Enregistrer » du profil : il se valide tout seul.",
      "L'onglet « Sécurité » des paramètres permet aussi de déconnecter toutes les sessions ouvertes — utile si vous vous êtes connecté sur un poste que vous ne maîtrisez plus."
    ]
  },
  {
    id: 'regler-alertes',
    titre: 'Choisir les alertes que vous recevez',
    module: 'settings',
    motsCles: ['alerte', 'notification', 'parametre', 'push', 'email', 'sms', 'silencieux', 'reglage'],
    resume: "Activer ou couper chaque type d'alerte, et définir des heures silencieuses.",
    paragraphes: [
      "Dans « Paramètres », onglet « Notifications », le groupe « Alertes en temps réel » réunit les alertes comprises dans votre offre : les rappels de « Maintenance » pour tout le monde, et les alertes de conduite et de franchissement pour les flottes équipées d'un boîtier.",
      "Le groupe « Canaux de notification » choisit par quel moyen elles arrivent : « Notifications push », « Notifications email », « Notifications SMS ».",
      "« Heures silencieuses » suspend les notifications sur une plage horaire — la nuit, par exemple — sans désactiver les alertes elles-mêmes.",
      "N'oubliez pas « Enregistrer les paramètres » en bas : les réglages ne s'appliquent pas tant que vous n'avez pas enregistré.",
      "Les alertes issues du boîtier n'apparaissent que pour les offres qui le comprennent."
    ]
  },
  {
    id: 'consulter-notifications',
    titre: 'Consulter et trier les notifications',
    module: 'general',
    motsCles: ['notification', 'cloche', 'alerte', 'lu', 'historique', 'filtrer'],
    resume: "Retrouver les alertes passées et faire le ménage.",
    paragraphes: [
      "La cloche donne les dernières alertes ; l'écran « Notifications » donne tout l'historique.",
      "Des boutons filtrent l'historique par famille d'alerte, et « Toutes » les réunit. Ils sont les mêmes pour toutes les offres : une famille qui ne concerne pas la vôtre reste simplement vide — sans boîtier GPS, par exemple, aucune alerte issue du boîtier n'arrive.",
      "La case « Non lues uniquement » isole ce que vous n'avez pas encore traité, et « Tout marquer lu » remet le compteur à zéro.",
      "Chaque ligne peut être marquée comme lue ou supprimée individuellement."
    ]
  },
  {
    id: 'exporter-importer-donnees',
    titre: 'Exporter ou importer vos données',
    module: 'settings',
    motsCles: ['export', 'import', 'excel', 'modele', 'sauvegarde', 'donnees', 'migrer'],
    resume: "Récupérer tout votre parc dans un classeur Excel, ou injecter des données en masse.",
    etapes: [
      "Ouvrez « Paramètres », onglet « Données ».",
      "« Exporter mes données (Excel) » produit un classeur de cinq feuilles : véhicules, entretiens, réparations, carburant, dépenses.",
      "Pour un import, cliquez d'abord sur « Télécharger le modèle », remplissez-le, puis « Importer un fichier Excel »."
    ],
    paragraphes: [
      "Cet onglet est réservé aux administrateurs de la société.",
      "Respectez l'ordre des colonnes du modèle sans en insérer ni en déplacer : l'import lit les colonnes par leur position, pas par leur titre. Une colonne décalée produit des lignes fausses sans message d'erreur.",
      "Le modèle a une feuille par nature de donnée : « Véhicules », « Entretiens », « Réparations », « Carburant », « Dépenses ». Une réparation va dans « Réparations », jamais dans « Entretiens » : saisie au mauvais endroit, elle serait comptée comme un entretien préventif et fausserait vos coûts."
    ],
    aRetenir: "Faites un export avant un import massif. C'est votre seul filet si l'import ne donne pas ce que vous attendiez."
  },
  {
    id: 'abonnement',
    titre: 'Consulter votre abonnement',
    module: 'general',
    motsCles: ['abonnement', 'offre', 'facture', 'echeance', 'renouveler', 'formule', 'quota', 'essai'],
    resume: "Voir votre offre, son échéance et vos quotas, ou commander une autre formule.",
    paragraphes: [
      "L'écran « Abonnement » affiche l'offre en cours, la date d'échéance et le montant du prochain règlement.",
      "La section « Utilisation » compare votre consommation à vos quotas : nombre de véhicules, nombre d'utilisateurs, et les quotas propres aux options que vous avez souscrites. Un quota atteint empêche d'ajouter davantage.",
      "« Choisir cette formule » enregistre une commande. Elle reste « en attente de validation » jusqu'à réception du règlement — le paiement se fait hors ligne, par virement.",
      "Les offres comprenant du matériel GPS ne se commandent pas en ligne : le bouton invite à nous contacter."
    ],
    aRetenir: "Cet écran n'existe que si votre société gère elle-même son abonnement. Sinon, tout passe par votre interlocuteur Calypso."
  }
];

/**
 * Visite guidee de premiere connexion.
 *
 * Chaque etape vise un element portant data-guide="...". Si le module n'est pas
 * souscrit ou si l'element n'est pas trouve, l'etape est sautee : la visite doit
 * s'adapter a l'abonnement du client, pas montrer des ecrans qu'il n'a pas.
 */
/*
 * Parcours defini par Karim le 23/09/2026 pour l'offre GPA — l'ordre dans
 * lequel un nouveau client doit s'y prendre pour commencer a travailler :
 * son premier vehicule (ou l'import Excel de tout son parc), ses chauffeurs,
 * les echeances de ses documents, puis un programme d'entretien et son
 * affectation aux vehicules. Le rapport n'en fait plus partie (« le reste on
 * verra si c'est necessaire »). La carte reste reservee aux offres GPS et se
 * place apres les chauffeurs ; ce qu'on ajoute pour le GPS se decidera ensuite.
 */
export const ETAPES_GUIDE: GuideEtape[] = [
  {
    id: 'bienvenue',
    titre: 'Bienvenue dans Calypso',
    // Étape jouée pour TOUTES les offres : elle ne cite que ce que tout client
    // a — pas de nombre d'étapes non plus, il change avec l'abonnement.
    texte: "Quelques étapes pour démarrer, dans l'ordre : votre premier véhicule, vos chauffeurs, les échéances de vos documents, un programme d'entretien, puis les adresses qui reçoivent vos alertes par e-mail. Votre parc se trouve dans le menu « Exploitation ». Vous pouvez arrêter à tout moment et reprendre depuis la rubrique « Aide » du menu.",
    cible: 'menu-flotte',
    sauf: 'monitoring',
    route: '/dashboard'
  },
  // Offre GPS (Karim, 23/09/2026) : « presque la meme chose que GPA », sans
  // l'ajout de vehicule — les vehicules sont crees par l'equipe Belive avec
  // leurs boitiers — plus la carte et les zones. Le module `monitoring` signe
  // l'offre GPS ; toutes les offres GPS ont aussi geofencing et tournees.
  {
    id: 'bienvenue-gps',
    titre: 'Bienvenue dans Calypso',
    texte: "Quelques étapes pour démarrer, dans l'ordre : vos véhicules, vos chauffeurs, la carte en direct, les échéances de vos documents, un programme d'entretien, les alertes par e-mail, puis votre premier rapport. Vous pouvez arrêter à tout moment et reprendre depuis la rubrique « Aide » du menu.",
    cible: 'menu-flotte',
    module: 'monitoring',
    route: '/dashboard'
  },
  {
    id: 'vehicules-en-place',
    titre: 'Vos véhicules sont déjà en place',
    texte: "Ils ont été ajoutés par notre équipe, avec leurs boîtiers. Vérifiez la liste : cliquez sur une ligne pour ouvrir la fiche d'un véhicule, ou sur « Modifier » pour compléter ses informations — chauffeur, couleur, capacité du réservoir.",
    cible: 'vehicules-liste',
    module: 'monitoring',
    route: '/vehicles'
  },
  {
    id: 'ajouter-vehicule',
    titre: 'Ajoutez votre premier véhicule',
    sauf: 'monitoring',
    // « Nouveau vehicule » est reserve aux administrateurs : sans ce drapeau, un
    // non-administrateur restait ~3 s devant un ecran assombri, puis l'etape sautait.
    adminSeulement: true,
    // Le modele d'import a cinq feuilles (verifie dans DataPortController le
    // 23/09/2026) : Vehicules, Entretiens, Reparations, Carburant, Depenses —
    // toutes lues et creees a l'import. Karim tient a ce que la visite le dise.
    texte: "« Nouveau véhicule » ouvre la fiche à remplir ; seuls les champs marqués d'une étoile sont obligatoires. Vous avez déjà vos données dans un fichier ? Importez tout d'un coup — véhicules, entretiens, réparations, pleins de carburant et dépenses : menu Paramètres, onglet « Données », « Télécharger le modèle » puis « Importer un fichier Excel ».",
    cible: 'vehicules-nouveau',
    module: 'vehicles',
    route: '/vehicles'
  },
  {
    id: 'ajouter-chauffeurs',
    titre: 'Ajoutez vos chauffeurs',
    texte: "« Nouveau chauffeur » crée la fiche : le prénom et le nom suffisent. Renseignez la date d'expiration du permis pour être prévenu avant, et rattachez le chauffeur à son véhicule.",
    cible: 'chauffeurs-nouveau',
    module: 'employees',
    route: '/drivers'
  },
  {
    id: 'voir-la-carte',
    titre: 'Voyez votre flotte en direct',
    texte: "Chaque véhicule apparaît avec sa couleur d'état : vert en mouvement, orange au ralenti, rouge stationné, gris hors ligne. Cliquez sur un véhicule pour ouvrir sa fiche.",
    cible: 'suivi-liste',
    module: 'monitoring',
    route: '/monitoring'
  },
  {
    id: 'echeances',
    titre: 'Renseignez vos échéances',
    texte: "Chaque véhicule a ici ses lignes assurance, vignette et visite technique. Tant qu'une date n'est pas saisie, la ligne dit « Non renseignée » et personne ne sera prévenu : cliquez sur « Modifier l'échéance » pour saisir la date, ou sur « Renouveler » quand c'est fait.",
    cible: 'echeances-compteurs',
    module: 'documents',
    route: '/echeances'
  },
  {
    id: 'entretien-modele',
    titre: "Créez un programme d'entretien",
    texte: "« Nouveau modele » définit un entretien qui revient — vidange, révision — avec son intervalle en kilomètres ou en mois. Calypso vous préviendra à l'approche de l'échéance.",
    cible: 'entretiens-nouveau-modele',
    module: 'maintenance',
    route: '/entretien-programmable'
  },
  {
    id: 'entretien-affecter',
    titre: 'Affectez-le à vos véhicules',
    texte: "« Affecter » applique le programme aux véhicules concernés. À partir de là, chaque véhicule a sa prochaine échéance d'entretien, et « Marquer fait » la recale quand l'entretien est réalisé.",
    cible: 'entretiens-affecter',
    module: 'maintenance',
    route: '/entretien-programmable'
  },
  // Etape ajoutee par Karim le 23/09/2026 pour les deux offres : « une etape
  // tres importante qu'on a oubliee, l'alerte par mail ». Avant-derniere en
  // GPS (le rapport suit), derniere en GPA. Le module `users` est dans tous
  // les plans ; si l'utilisateur n'a pas ce droit, la cible est absente et
  // l'etape est sautee.
  {
    id: 'alertes-email',
    titre: 'Recevez vos alertes par e-mail',
    // Types proposes par l'ecran (ALERT_TYPES d'alert-emails.component.ts) :
    // Assurance, Taxe Circulation, Visite Technique, Entretien, Permis, Accident.
    texte: "Calypso envoie une copie de ses alertes par e-mail — assurance, visite technique, entretien, permis — mais seulement aux adresses inscrites ici. Ouvrez l'onglet « Alertes par email », puis « Ajouter une adresse » : une adresse et un type d'alerte. Sans adresse, personne n'est prévenu par mail.",
    cible: 'alertes-email-onglet',
    module: 'users',
    route: '/users',
    // Derniere etape du parcours GPA : « Terminer » depose le client sur
    // Vehicules, pour qu'il ajoute les siens (Karim, 23/09/2026). En GPS ce
    // n'est pas la derniere etape, la valeur n'y sert pas.
    routeApresFin: '/vehicles'
  },
  // Offre GPS seulement (Karim, 23/09/2026 : « ajoute rapport » pour le GPS,
  // retire pour la GPA). Toutes les offres GPS ont le module Rapports ; si un
  // utilisateur n'a pas ce droit, la cible est absente et l'etape est sautee.
  {
    id: 'premier-rapport',
    titre: 'Générez votre premier rapport',
    texte: "Choisissez un type de rapport, un véhicule et une période, puis cliquez sur « Exécuter ». L'export Excel, PDF ou CSV se débloque une fois le rapport affiché.",
    cible: 'rapports-type',
    module: 'monitoring',
    route: '/reports',
    // Derniere etape du parcours GPS : « Terminer » depose le client sur
    // « Suivi en direct », pour qu'il voie ses vehicules (Karim, 23/09/2026).
    routeApresFin: '/monitoring'
  }
];

/**
 * Tutoriels des ecrans (Karim, 24/09/2026) : pour un NOUVEL utilisateur — celui
 * dont c'est la premiere connexion — chaque ecran ouvert propose un tutoriel pas a
 * pas, a chaque acces, tant qu'il ne l'a ni passe ni termine. Le client fait
 * lui-meme chaque geste ; la bulle lui dit lequel.
 *
 * Pilote : l'ecran « Vehicules » de l'offre GPA, valide par Karim le 24/09/2026.
 * Parcours voulu : « Nouveau vehicule », puis le nom, la plaque, la marque et le
 * modele, sans insister sur le reste (il pourra revenir le remplir), puis
 * « Ajouter ». Les autres ecrans GPA suivent le meme modele, un par un, chacun
 * valide avec Karim : Chauffeurs d'abord. Libelles cites TELS QU'A L'ECRAN, meme
 * sans accents : corriger les ecrans (partages avec le GPS) attend la fin de la
 * validation GPA (« il faut qu'on valide ensemble GPA et on passe apres »).
 */
export const VISITES_ECRANS: VisiteEcran[] = [
  {
    id: 'tuto-vehicules-gpa',
    titre: 'Écran Véhicules',
    route: '/vehicles',
    module: 'vehicles',
    // GPA seulement pour le pilote : en GPS, les vehicules sont crees par
    // l'equipe Belive avec leurs boitiers.
    sauf: 'monitoring',
    // « Nouveau vehicule » est reserve aux administrateurs (*ngIf="isAdmin").
    adminSeulement: true,
    etapes: [
      {
        id: 'tuto-vehicule-nouveau',
        titre: 'Ajoutez votre premier véhicule',
        texte: "Cliquez sur « Nouveau véhicule » : la fiche à remplir s'ouvre.",
        cible: 'vehicules-nouveau',
        action: 'clic'
      },
      {
        id: 'tuto-vehicule-nom',
        titre: 'Le nom du véhicule',
        texte: 'Donnez-lui un nom qui vous parle, par exemple « Camion principal » ou « Clio du commercial ». Puis cliquez sur « Suivant ».',
        cible: 'vehicule-nom',
        action: 'valeur'
      },
      {
        id: 'tuto-vehicule-plaque',
        titre: 'La plaque',
        texte: "Saisissez son immatriculation, telle qu'elle figure sur la carte grise.",
        cible: 'vehicule-plaque',
        action: 'valeur'
      },
      {
        id: 'tuto-vehicule-marque',
        titre: 'La marque',
        // Facultatif : la marque du client peut manquer au catalogue (pas de saisie libre).
        texte: "Choisissez la marque dans la liste. Elle n'y est pas ? Cliquez sur « Suivant ».",
        cible: 'vehicule-marque',
        action: 'valeur',
        facultatif: true
      },
      {
        id: 'tuto-vehicule-modele',
        titre: 'Le modèle',
        // Facultatif : 8 des 27 marques de production n'ont aucun modele actif (liste
        // vide), et le modele du client peut manquer. Karim, 24/09/2026 : « fais-le passer ».
        texte: "Choisissez maintenant le modèle : la liste suit la marque choisie. Il n'y est pas, ou la liste est vide ? Cliquez sur « Suivant ».",
        cible: 'vehicule-modele',
        action: 'valeur',
        facultatif: true
      },
      {
        id: 'tuto-vehicule-ajouter',
        titre: 'Enregistrez le véhicule',
        // Les autres champs obligatoires ont une valeur par defaut dans la fiche
        // (vehicle-popup resetForm : annee en cours, citadine, disponible,
        // compteur 0, diesel) : les quatre champs ci-dessus suffisent.
        // Le « type » n'est pas cite : choisir un modele le remplace par le type du
        // catalogue (hatchback, van…), absent de la liste Type, qui s'affiche alors
        // vide (defaut de la fiche signale a Karim le 24/09/2026).
        // Pas de phrase sur un eventuel refus du serveur : Karim ne veut pas de
        // message « Un message d'erreur s'affiche ? » dans les bulles (24/09/2026).
        texte: "Le reste est facultatif ou déjà prérempli (année, compteur…) : vous pourrez le compléter plus tard avec « Modifier ». Cliquez sur « Ajouter ».",
        cible: 'vehicule-ajouter',
        action: 'disparition'
      }
    ]
  },
  {
    id: 'tuto-chauffeurs-gpa',
    titre: 'Écran Chauffeurs',
    route: '/drivers',
    module: 'employees',
    // GPA seulement pendant la validation avec Karim (le GPS viendra ensuite).
    sauf: 'monitoring',
    // Pas reserve a l'administrateur : « Nouveau chauffeur » est ouvert a tout
    // utilisateur qui a l'ecran Chauffeurs (employees.component.html).
    // Fiche : employee-popup.component.ts. Prenom et Nom suffisent au serveur
    // (CreateDriverCommand) ; rien n'est prerempli d'utile.
    etapes: [
      {
        id: 'tuto-chauffeur-nouveau',
        titre: 'Ajoutez votre premier chauffeur',
        texte: "Cliquez sur « Nouveau chauffeur » : la fiche à remplir s'ouvre.",
        cible: 'chauffeurs-nouveau',
        action: 'clic'
      },
      {
        id: 'tuto-chauffeur-prenom',
        titre: 'Le prénom',
        texte: "Saisissez le prénom du chauffeur. Puis cliquez sur « Suivant ».",
        cible: 'chauffeur-prenom',
        action: 'valeur'
      },
      {
        id: 'tuto-chauffeur-nom',
        titre: 'Le nom',
        texte: 'Saisissez maintenant son nom de famille.',
        cible: 'chauffeur-nom',
        action: 'valeur'
      },
      {
        id: 'tuto-chauffeur-permis',
        titre: "L'expiration du permis",
        // Alimente les echeances et les alertes « Permis » (DriverPermitExpiries).
        texte: "Indiquez la « Date d'expiration » de son permis : vous serez prévenu avant l'échéance. Vous ne l'avez pas sous la main ? Cliquez sur « Suivant ».",
        cible: 'chauffeur-permis-expiration',
        action: 'valeur',
        facultatif: true
      },
      {
        id: 'tuto-chauffeur-vehicule',
        titre: 'Son véhicule',
        texte: "Choisissez le « Véhicule » qu'il conduit. Il n'y est pas ? Cliquez sur « Suivant ».",
        cible: 'chauffeur-vehicule',
        action: 'valeur',
        facultatif: true
      },
      {
        id: 'tuto-chauffeur-creer',
        titre: 'Enregistrez le chauffeur',
        // « Modifier » n'est pas un libelle visible : c'est le crayon de la ligne.
        texte: "Le reste est facultatif : vous pourrez le compléter plus tard avec le crayon de sa ligne. Cliquez sur « Créer le chauffeur ».",
        cible: 'chauffeur-creer',
        action: 'disparition'
      }
    ]
  }
];
