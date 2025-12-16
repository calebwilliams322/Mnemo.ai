-- Audit Events Table for Security Logging
-- Tracks all security-related events for compliance and debugging

CREATE TABLE IF NOT EXISTS public.audit_events (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID REFERENCES public.tenants(id) ON DELETE SET NULL,
    user_id UUID REFERENCES public.users(id) ON DELETE SET NULL,
    event_type VARCHAR(50) NOT NULL,
    event_status VARCHAR(20) NOT NULL,
    ip_address VARCHAR(50),
    user_agent VARCHAR(500),
    details JSONB,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

-- Indexes for common query patterns
CREATE INDEX idx_audit_events_tenant_id ON public.audit_events(tenant_id);
CREATE INDEX idx_audit_events_user_id ON public.audit_events(user_id);
CREATE INDEX idx_audit_events_event_type ON public.audit_events(event_type);
CREATE INDEX idx_audit_events_created_at ON public.audit_events(created_at);
CREATE INDEX idx_audit_events_tenant_type_created ON public.audit_events(tenant_id, event_type, created_at DESC);

-- Enable RLS
ALTER TABLE public.audit_events ENABLE ROW LEVEL SECURITY;

-- RLS Policy: Users can only view audit events for their tenant
CREATE POLICY "Tenant isolation for audit_events"
    ON public.audit_events
    FOR SELECT
    TO authenticated
    USING (tenant_id = public.get_current_tenant_id());

-- Note: INSERT is done by the API service role which bypasses RLS
-- Audit events should not be updated or deleted by users

-- Revoke access from anon role
REVOKE ALL ON public.audit_events FROM anon;

-- Grant select to authenticated users (limited by RLS)
GRANT SELECT ON public.audit_events TO authenticated;
