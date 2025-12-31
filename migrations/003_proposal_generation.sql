-- =============================================================================
-- Migration: Proposal Generation Feature
-- Created: 2025-01-01
-- Description: Adds ProposalTemplates and Proposals tables for document generation
-- =============================================================================

-- ProposalTemplates: Stores Word (.docx) templates uploaded by agencies
CREATE TABLE IF NOT EXISTS proposal_templates (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    name VARCHAR(255) NOT NULL,
    description VARCHAR(1000),
    storage_path VARCHAR(500) NOT NULL,
    original_file_name VARCHAR(255) NOT NULL,
    file_size_bytes BIGINT,
    placeholders JSONB DEFAULT '[]',
    is_active BOOLEAN DEFAULT TRUE,
    is_default BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_proposal_templates_tenant ON proposal_templates(tenant_id);
CREATE INDEX IF NOT EXISTS idx_proposal_templates_tenant_active ON proposal_templates(tenant_id, is_active);

-- Proposals: Generated proposal documents
CREATE TABLE IF NOT EXISTS proposals (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    template_id UUID NOT NULL REFERENCES proposal_templates(id) ON DELETE CASCADE,
    client_name VARCHAR(255) NOT NULL,
    policy_ids JSONB NOT NULL DEFAULT '[]',
    output_storage_path VARCHAR(500),
    status VARCHAR(50) DEFAULT 'pending',
    error_message VARCHAR(1000),
    created_at TIMESTAMPTZ DEFAULT NOW(),
    generated_at TIMESTAMPTZ,
    created_by_user_id UUID REFERENCES users(id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_proposals_tenant ON proposals(tenant_id);
CREATE INDEX IF NOT EXISTS idx_proposals_template ON proposals(template_id);
CREATE INDEX IF NOT EXISTS idx_proposals_created_by ON proposals(created_by_user_id);

-- =============================================================================
-- End of migration
-- =============================================================================
