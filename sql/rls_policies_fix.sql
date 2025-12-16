-- Fix RLS policies for tables without direct tenant_id
-- These tables inherit tenant isolation through foreign keys

-- Drop the failed policies first (they may not exist, so ignore errors)
DROP POLICY IF EXISTS "Tenant isolation for coverages" ON public.coverages;
DROP POLICY IF EXISTS "Tenant isolation for document_chunks" ON public.document_chunks;
DROP POLICY IF EXISTS "Tenant isolation for messages" ON public.messages;

-- ============================================================================
-- RLS POLICIES: COVERAGES
-- Linked through policies table
-- ============================================================================

CREATE POLICY "Tenant isolation for coverages"
    ON public.coverages
    FOR ALL
    TO authenticated
    USING (
        policy_id IN (
            SELECT id FROM public.policies
            WHERE tenant_id = public.get_current_tenant_id()
        )
    )
    WITH CHECK (
        policy_id IN (
            SELECT id FROM public.policies
            WHERE tenant_id = public.get_current_tenant_id()
        )
    );

-- ============================================================================
-- RLS POLICIES: DOCUMENT_CHUNKS
-- Linked through documents table
-- ============================================================================

CREATE POLICY "Tenant isolation for document_chunks"
    ON public.document_chunks
    FOR ALL
    TO authenticated
    USING (
        document_id IN (
            SELECT id FROM public.documents
            WHERE tenant_id = public.get_current_tenant_id()
        )
    )
    WITH CHECK (
        document_id IN (
            SELECT id FROM public.documents
            WHERE tenant_id = public.get_current_tenant_id()
        )
    );

-- ============================================================================
-- RLS POLICIES: MESSAGES
-- Linked through conversations table
-- ============================================================================

CREATE POLICY "Tenant isolation for messages"
    ON public.messages
    FOR ALL
    TO authenticated
    USING (
        conversation_id IN (
            SELECT id FROM public.conversations
            WHERE tenant_id = public.get_current_tenant_id()
        )
    )
    WITH CHECK (
        conversation_id IN (
            SELECT id FROM public.conversations
            WHERE tenant_id = public.get_current_tenant_id()
        )
    );
