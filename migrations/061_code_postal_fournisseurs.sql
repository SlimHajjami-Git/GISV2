-- 061 — Code postal des fournisseurs (campagne de tests GPA du 06/10/2026, erreur n°11).
--
-- Constat : la fiche fournisseur propose un champ « Code postal », l'API le reçoit
-- (CreateSupplierCommand / UpdateSupplierCommand) mais la table suppliers n'avait pas de
-- colonne : SupplierConfiguration l'ignorait (Ignore(PostalCode)) et la saisie disparaissait
-- sans message. Même constat sur TN et en local le 15/10/2026 (12 fournisseurs sur TN).
--
-- Modèle : colonne facultative, même convention que les autres colonnes de suppliers
-- (PascalCase entre guillemets). Les fournisseurs existants restent sans code postal.
--
-- À jouer AVANT le pod API (qui lit et écrit la colonne : sinon 42703 sur tout l'écran
-- Fournisseurs et sur les listes qui chargent les fournisseurs).

ALTER TABLE suppliers ADD COLUMN IF NOT EXISTS "PostalCode" varchar(20);
