-- 052 — Crédit IA mensuel du scan de factures, en JETONS (demande de Slim du 22/09/2026 :
-- « une barre de progression de contexte qui se remet à zéro chaque mois »).
--
-- CONSTAT — Le scan de factures IA est plafonné en NOMBRE de scans par mois
-- (societes.invoice_scan_monthly_limit : NULL = 20, 0 = désactivé), affiché « 12/20 ce
-- mois » à côté du bouton. Or un scan ne coûte pas toujours la même chose : mesuré sur TN
-- (13 scans), de 1 953 à 3 957 jetons Groq, 3 000 en moyenne (p90 3 631) — une photo floue
-- déclenche une passe corrective qui double la note. Le compteur ne disait rien de ce que
-- la société consomme réellement.
--
-- POURQUOI UNE NOUVELLE COLONNE — le budget devient un crédit de jetons par mois civil
-- (UTC) : invoice_scan_monthly_tokens, NULL = défaut plateforme 60 000 (≈ 20 scans, même
-- ordre que l'ancien défaut), 0 = fonction désactivée. La consommation est la somme de
-- invoice_scan_logs.tokens_used depuis le 1er du mois, déjà journalisée à chaque scan
-- réussi (index ix_invoice_scan_logs_company_id_created_at existant : rien à créer).
-- invoice_scan_monthly_limit n'est PAS réutilisée : ses valeurs sont des SCANS, les relire
-- comme des jetons ferait passer une société réglée à 50 scans à 50 jetons, donc à zéro
-- scan. Elle est conservée et CONVERTIE À LA LECTURE (limite × 3 000, bornée à 10 000 000,
-- 0 reste 0) tant que la nouvelle colonne est NULL. Chaque enregistrement depuis la fiche
-- admin écrit les jetons ET une OMBRE dans l'ancienne colonne, pour le retour arrière :
-- vide (défaut) → NULL ; 0 (désactivé) → 0 ; sinon l'équivalent en scans (jetons / 3 000,
-- au moins 1). Le nouveau code ne la relit plus dès que les jetons sont renseignés.
--
-- AUCUNE DONNÉE MODIFIÉE — ajout d'une colonne NULL, sans défaut ni UPDATE : chaque
-- société garde exactement son budget actuel (ancien quota converti, ou défaut).
--
-- ORDRE DE DÉPLOIEMENT — ce SQL se joue AVANT le pod API qui mappe
-- Societe.InvoiceScanMonthlyTokens (sinon 42703 sur toute lecture d'une société : login
-- cassé pour tout le monde). DZ puis TN. Retour arrière du pod API : la colonne peut
-- rester en place, l'ancien code l'ignore et relit invoice_scan_monthly_limit, que la
-- fiche tient à jour (ombre ci-dessus) — une société désactivée le reste, une société
-- réglée garde l'équivalent de son crédit en scans. Seul écart : un crédit qui n'est pas
-- un multiple de 3 000 jetons est arrondi au scan inférieur.

ALTER TABLE societes ADD COLUMN IF NOT EXISTS invoice_scan_monthly_tokens integer NULL;

COMMENT ON COLUMN societes.invoice_scan_monthly_tokens IS
    'Crédit IA mensuel du scan de factures, en jetons Groq (NULL = défaut plateforme 60 000, 0 = désactivé). Remis à zéro le 1er de chaque mois (UTC). Prioritaire sur invoice_scan_monthly_limit (ancien quota en scans, converti à 3 000 jetons par scan).';
