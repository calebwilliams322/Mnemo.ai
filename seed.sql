-- Seed data for testing
-- Run this in Supabase SQL Editor

-- Create a test tenant
INSERT INTO tenants (id, name, plan, is_active, created_at)
VALUES (
    '00000000-0000-0000-0000-000000000001',
    'Test Brokerage',
    'Starter',
    true,
    NOW()
) ON CONFLICT (id) DO NOTHING;

-- Create a test user
INSERT INTO users (id, tenant_id, email, name, role, is_active, created_at)
VALUES (
    '00000000-0000-0000-0000-000000000001',
    '00000000-0000-0000-0000-000000000001',
    'test@example.com',
    'Test User',
    'Admin',
    true,
    NOW()
) ON CONFLICT (id) DO NOTHING;

-- Verify
SELECT 'Tenant created:' as status, id, name FROM tenants WHERE id = '00000000-0000-0000-0000-000000000001';
SELECT 'User created:' as status, id, email FROM users WHERE id = '00000000-0000-0000-0000-000000000001';
