# Paiement par carte des abonnements — ClicToPay

Les commandes d'abonnement (écran **Abonnement**, offre en libre-service) peuvent être réglées
par carte bancaire via ClicToPay (Monétique Tunisie), d'après le manuel d'intégration
« Basic 1.0 » (`register.do` et `getOrderStatusExtended.do`). Le règlement par virement reste
possible.

## Tester depuis la console d'administration

Page **Admin > Paiement ClicToPay** (`/admin/paiement-test`, administrateur système complet) :

- l'état de la configuration du serveur (identifiants posés, environnement TEST ou PRODUCTION,
  bouton client affiché ou non), sans jamais montrer d'identifiant ;
- « Payer sur ClicToPay » : un paiement de **test** d'un montant libre (0,1 à 1000, TND, EUR ou
  USD), sans commande ni abonnement touchés (`online_payments.kind = 'admin_test'`) ;
- ClicToPay revient sur cette page (`?ref=CLYT-…`), qui fait vérifier le résultat par le serveur ;
- la liste des dernières tentatives (tests et clients), avec « Vérifier » sur celles en attente.

La saisie sur la page ClicToPay se fait au clavier (champ de carte à masque, listes mois/année).

## Parcours

1. Le client choisit une formule : la **commande** est créée, son montant figé par le serveur.
2. « Payer par carte » : l'API appelle `register.do` avec ce montant (en millimes pour le dinar,
   en centimes pour l'euro) et renvoie la page de paiement ClicToPay ; l'écran y envoie le client.
3. Le client paie chez ClicToPay (3-D Secure). Ses données de carte ne passent jamais par Calypso.
4. ClicToPay le renvoie sur `/paiement/retour?ref=CLY-…`. **Ce retour ne prouve rien** : l'écran
   demande au serveur de vérifier par `getOrderStatusExtended.do`.
5. Le serveur ne valide la commande que si `orderStatus = 2` **et** que le montant, la devise et le
   numéro de commande renvoyés sont ceux de la tentative. La validation passe par le même chemin
   que la validation manuelle : l'abonnement est prolongé une seule fois.
6. Si le client ferme l'onglet après avoir payé, une tâche de fond revérifie toutes les 3 minutes
   les tentatives en attente ; une tentative jamais payée est close au bout de 48 h.

Chaque tentative est enregistrée dans `online_payments` (migration 067), sans rien de la carte
hormis le numéro déjà masqué par ClicToPay et le code d'autorisation.

| Statut | Sens |
|---|---|
| `pending` | Page de paiement ouverte, pas encore confirmée par la banque. |
| `paid` | Payée et vérifiée. La commande est validée, sauf si elle avait été annulée ou déjà validée : la note de la tentative dit alors « à rembourser ». |
| `failed` | Refusée, annulée ou remboursée chez ClicToPay. |
| `error` | `register.do` a échoué : aucune page n'a été ouverte. |
| `expired` | Jamais payée dans les 48 h. |
| `anomaly` | Payée, mais montant, devise ou numéro différent. **La commande n'est pas validée** : à vérifier à la main. |

Une tentative payée sur une commande annulée, ou payée en double, se retrouve par :

```sql
SELECT id, company_id, subscription_order_id, order_number, amount_minor, currency, status, note
FROM online_payments WHERE note IS NOT NULL OR status = 'anomaly' ORDER BY id DESC;
```

## Réglages (par serveur, jamais dans le dépôt)

Variables d'environnement du déploiement `gis-api` :

| Variable | Valeur |
|---|---|
| `ClicToPay__Enabled` | `true` : ClicToPay utilisable (page de test de la console admin) |
| `ClicToPay__ClientCheckoutEnabled` | `false` (défaut) : bouton « Payer par carte » **masqué aux clients**. Passer à `true` avec les identifiants de production. |
| `ClicToPay__UserName` | identifiant marchand fourni par ClicToPay |
| `ClicToPay__Password` | mot de passe marchand (sensible à la casse) |
| `ClicToPay__BaseUrl` | `https://test.clictopay.com/payment/rest/` (défaut, TEST) ou `https://ipay.clictopay.com/payment/rest/` (PRODUCTION) |
| `ClicToPay__ReturnBaseUrl` | facultatif ; défaut `App__PublicBaseUrl` (`https://www.belive-calypso.com` sur TN) |
| `ClicToPay__PilotCompanyIds` | facultatif : sociétés autorisées, séparées par des virgules (`1` = Belive). Vide = toutes. **À poser pendant la recette** : les identifiants de TEST sur TN sans que les clients voient le bouton. |
| `ClicToPay__MobilePageView` | `false` (défaut). Ne passer à `true` que si ClicToPay confirme le gabarit mobile du compte (voir ci-dessous). |

Sans identifiant, le bouton « Payer par carte » n'apparaît pas et l'API refuse. Sur
l'environnement de test, l'écran affiche « Environnement de test — aucun débit réel ».

Le mot de passe marchand va dans un secret Kubernetes, pas en clair dans le yaml :

```bash
ssh belive-tn "kubectl create secret generic clictopay -n gisv2 --from-literal=username='<identifiant>' --from-literal=password='<mot de passe>'"
ssh belive-tn "kubectl set env deployment/gis-api -n gisv2 --from=secret/clictopay --prefix=ClicToPay__ && kubectl set env deployment/gis-api -n gisv2 ClicToPay__Enabled=true"
```

`--prefix` donne `ClicToPay__USERNAME` / `ClicToPay__PASSWORD` : la configuration .NET ignore la
casse, ces noms sont lus comme `UserName` et `Password`.

Devises acceptées : TND (788), EUR (978), USD (840), selon le contrat marchand. Une société en DZD
ne peut pas payer par carte (message clair, règlement par virement).

## Essai réel sur l'environnement de test (06/11/2026)

Avec les accès de test du compte marchand et les deux cartes de test fournies par ClicToPay
(acceptée, refusée), le client .NET de Calypso a été passé contre `test.clictopay.com` :

- `register.do` : identifiant de commande et page `https://test.clictopay.com/epg/merchants/CLICTOPAY/payment.html?mdOrder=…`
  (36 000 envoyés → « 36.000 TND » affiché : millimes confirmés).
- Carte acceptée : retour sur `returnUrl` (ClicToPay ajoute `&orderId=…` après notre `ref`) ;
  statut `orderStatus 2`, `paymentState DEPOSITED`, montant, devise `"788"` et numéro conformes.
- Carte refusée : retour sur `failUrl` ; `orderStatus 6`, `actionCode 76`, `paymentState DECLINED`.
- Commande inconnue : `errorCode "6"` ; numéro déjà utilisé : `errorCode 1`.

Écarts avec le manuel, pris en compte dans le code :

- le code d'autorisation arrive dans `cardAuthInfo.authorizationResponseId`, pas `approvalCode` ;
- `pageView=MOBILE` renvoie une page `mobile_payment.html` **en 404** sur ce compte : Calypso
  envoie DESKTOP (la page s'affiche bien sur téléphone) tant que `MobilePageView` n'est pas activé ;
- les accents de `description` reviennent abîmés : Calypso l'envoie en ASCII ;
- le motif de refus de la banque est en anglais technique : il reste en base
  (`action_description`), le client lit un message en français.

## Déploiement

1. Jouer `migrations/067_paiements_en_ligne.sql` **avant** le pod API.
2. Déployer API puis frontend.
3. Poser les variables ci-dessus (identifiants de TEST d'abord).

## Procédure ClicToPay (manuel, section 2)

1. Recevoir les accès de TEST (identifiant, mot de passe) de l'équipe ClicToPay.
2. Intégration : faite (ce document).
3. Paramètres : `returnUrl`/`failUrl` = `<adresse publique>/paiement/retour?ref=…`, devise selon
   la société, langue `fr`, gabarit DESKTOP ou MOBILE selon l'écran.
4. Charger sur un serveur de test accessible depuis Internet.
5. Dérouler le cahier de recettes fourni par ClicToPay et le leur renvoyer.
6. Formation au portail marchand (suivi des transactions : `https://test.clictopay.com/epg_gui/`).
7. Passage en production : `ClicToPay__BaseUrl` sur `ipay.clictopay.com`, identifiants de
   production, puis un vrai paiement de test.

Ne jamais passer en production sans avoir transmis le cahier de recettes à ClicToPay.
