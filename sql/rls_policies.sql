-- Row Level Security (RLS) Policies for Multi-Tenant Isolation
-- This provides database-level security in addition to application-level filters.
-- Even if someone bypasses the API, they cannot access other tenants' data.

-- ============================================================================
-- HELPER FUNCTION: Get current user's tenant_id from JWT
-- ============================================================================

-- Function to get the current user's tenant_id by looking up their Supabase ID
CREATE OR REPLACE FUNCTION public.get_current_tenant_id()
RETURNS UUID
LANGUAGE plpgsql
SECURITY DEFINER
STABLE
AS $$
DECLARE
    current_tenant_id UUID;
BEGIN
    -- Get tenant_id from users table based on the authenticated user's Supabase ID
    SELECT tenant_id INTO current_tenant_id
    FROM public.users
    WHERE supabase_user_id = auth.uid()::text
    LIMIT 1;

    RETURN current_tenant_id;
END;
$$;

-- Grant execute to authenticated users
GRANT EXECUTE ON FUNCTION public.get_current_tenant_id() TO authenticated;

-- ============================================================================
-- ENABLE RLS ON ALL TABLES
-- ============================================================================

ALTER TABLE public.tenants ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.users ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.documents ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.document_chunks ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.policies ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.coverages ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.compliance_checks ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.contract_requirements ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.conversations ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.messages ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.submission_groups ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.webhook_deliveries ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.industry_benchmarks ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.webhooks ENABLE ROW LEVEL SECURITY;

-- ============================================================================
-- RLS POLICIES: TENANTS
-- Users can only see their own tenant
-- ============================================================================

CREATE POLICY "Users can view their own tenant"
    ON public.tenants
    FOR SELECT
    TO authenticated
    USING (id = public.get_current_tenant_id());

CREATE POLICY "Users can update their own tenant"
    ON public.tenants
    FOR UPDATE
    TO authenticated
    USING (id = public.get_current_tenant_id());

-- ============================================================================
-- RLS POLICIES: USERS
-- Users can only see users in their tenant
-- ============================================================================

CREATE POLICY "Users can view users in their tenant"
    ON public.users
    FOR SELECT
    TO authenticated
    USING (tenant_id = public.get_current_tenant_id());

CREATE POLICY "Users can update users in their tenant"
    ON public.users
    FOR UPDATE
    TO authenticated
    USING (tenant_id = public.get_current_tenant_id());

CREATE POLICY "Users can insert users in their tenant"
    ON public.users
    FOR INSERT
    TO authenticated
    WITH CHECK (tenant_id = public.get_current_tenant_id());

-- ============================================================================
-- RLS POLICIES: DOCUMENTS
-- ============================================================================

CREATE POLICY "Tenant isolation for documents"
    ON public.documents
    FOR ALL
    TO authenticated
    USING (tenant_id = public.get_current_tenant_id())
    WITH CHECK (tenant_id = public.get_current_tenant_id());

-- ============================================================================
-- RLS POLICIES: DOCUMENT_CHUNKS
-- ============================================================================

CREATE POLICY "Tenant isolation for document_chunks"
    ON public.document_chunks
    FOR ALL
    TO authenticated
    USING (tenant_id = public.get_current_tenant_id())
    WITH CHECK (tenant_id = public.get_current_tenant_id());

-- ============================================================================
-- RLS POLICIES: POLICIES (insurance policies)
-- ============================================================================

CREATE POLICY "Tenant isolation for policies"
    ON public.policies
    FOR ALL
    TO authenticated
    USING (tenant_id = public.get_current_tenant_id())
    WITH CHECK (tenant_id = public.get_current_tenant_id());

-- ============================================================================
-- RLS POLICIES: COVERAGES
-- ============================================================================

CREATE POLICY "Tenant isolation for coverages"
    ON public.coverages
    FOR ALL
    TO authenticated
    USING (tenant_id = public.get_current_tenant_id())
    WITH CHECK (tenant_id = public.get_current_tenant_id());

-- ============================================================================
-- RLS POLICIES: COMPLIANCE_CHECKS
-- ============================================================================

CREATE POLICY "Tenant isolation for compliance_checks"
    ON public.compliance_checks
    FOR ALL
    TO authenticated
    USING (tenant_id = public.get_current_tenant_id())
    WITH CHECK (tenant_id = public.get_current_tenant_id());

-- ============================================================================
-- RLS POLICIES: CONTRACT_REQUIREMENTS
-- ============================================================================

CREATE POLICY "Tenant isolation for contract_requirements"
    ON public.contract_requirements
    FOR ALL
    TO authenticated
    USING (tenant_id = public.get_current_tenant_id())
    WITH CHECK (tenant_id = public.get_current_tenant_id());

-- ============================================================================
-- RLS POLICIES: CONVERSATIONS
-- ============================================================================

CREATE POLICY "Tenant isolation for conversations"
    ON public.conversations
    FOR ALL
    TO authenticated
    USING (tenant_id = public.get_current_tenant_id())
    WITH CHECK (tenant_id = public.get_current_tenant_id());

-- ============================================================================
-- RLS POLICIES: MESSAGES
-- ============================================================================

CREATE POLICY "Tenant isolation for messages"
    ON public.messages
    FOR ALL
    TO authenticated
    USING (tenant_id = public.get_current_tenant_id())
    WITH CHECK (tenant_id = public.get_current_tenant_id());

-- ============================================================================
-- RLS POLICIES: SUBMISSION_GROUPS
-- ============================================================================

CREATE POLICY "Tenant isolation for submission_groups"
    ON public.submission_groups
    FOR ALL
    TO authenticated
    USING (tenant_id = public.get_current_tenant_id())
    WITH CHECK (tenant_id = public.get_current_tenant_id());

-- ============================================================================
-- RLS POLICIES: WEBHOOKS
-- ============================================================================

CREATE POLICY "Tenant isolation for webhooks"
    ON public.webhooks
    FOR ALL
    TO authenticated
    USING (tenant_id = public.get_current_tenant_id())
    WITH CHECK (tenant_id = public.get_current_tenant_id());

-- ============================================================================
-- RLS POLICIES: WEBHOOK_DELIVERIES
-- Linked to webhooks which are tenant-scoped
-- ============================================================================

CREATE POLICY "Tenant isolation for webhook_deliveries"
    ON public.webhook_deliveries
    FOR ALL
    TO authenticated
    USING (
        webhook_id IN (
            SELECT id FROM public.webhooks WHERE tenant_id = public.get_current_tenant_id()
        )
    );

-- ============================================================================
-- RLS POLICIES: INDUSTRY_BENCHMARKS
-- Read-only for all authenticated users (shared reference data)
-- ============================================================================

CREATE POLICY "All authenticated users can read benchmarks"
    ON public.industry_benchmarks
    FOR SELECT
    TO authenticated
    USING (true);

-- ============================================================================
-- SERVICE ROLE BYPASS
-- The service role (used by our API) bypasses RLS
-- This is important because our API uses the service role key
-- ============================================================================

-- Note: By default, the service_role in Supabase bypasses RLS.
-- Our API uses the service role key, so it can access all data.
-- RLS protects against direct database access from clients.

-- ============================================================================
-- ANON ROLE: No access
-- Anonymous users should not be able to access any data
-- ============================================================================

-- Revoke all access from anon role on these tables
REVOKE ALL ON public.tenants FROM anon;
REVOKE ALL ON public.users FROM anon;
REVOKE ALL ON public.documents FROM anon;
REVOKE ALL ON public.document_chunks FROM anon;
REVOKE ALL ON public.policies FROM anon;
REVOKE ALL ON public.coverages FROM anon;
REVOKE ALL ON public.compliance_checks FROM anon;
REVOKE ALL ON public.contract_requirements FROM anon;
REVOKE ALL ON public.conversations FROM anon;
REVOKE ALL ON public.messages FROM anon;
REVOKE ALL ON public.submission_groups FROM anon;
REVOKE ALL ON public.webhook_deliveries FROM anon;
REVOKE ALL ON public.industry_benchmarks FROM anon;
REVOKE ALL ON public.webhooks FROM anon;
