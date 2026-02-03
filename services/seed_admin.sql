-- Seed admin role
INSERT INTO roles (name, description, societe_id, is_company_admin, is_system_role, permissions, created_at, updated_at)
VALUES ('Administrateur', 'Administrateur système avec tous les droits', 1, true, true, '{"all": true}', NOW(), NOW())
ON CONFLICT DO NOTHING;

-- Seed admin user
INSERT INTO users (first_name, last_name, email, password_hash, status, company_id, role_id, created_at, updated_at)
VALUES ('Admin', 'Belive', 'admin@belive.tn', '$2a$11$rKN/VcPCwD8MOqXqdJqHs.qDL9LVV.JZR9lLmPnVPBVrNNJJVJ7Hy', 'active', 1, 1, NOW(), NOW())
ON CONFLICT (email) DO NOTHING;
