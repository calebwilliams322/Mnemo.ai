CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE TABLE industry_benchmarks (
        id uuid NOT NULL,
        industry_class character varying(200) NOT NULL,
        naics_code character varying(10),
        sic_code character varying(10),
        recommended_coverages jsonb NOT NULL,
        source character varying(200),
        notes text,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT p_k_industry_benchmarks PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE TABLE tenants (
        id uuid NOT NULL,
        name character varying(200) NOT NULL,
        address_line1 character varying(200),
        address_line2 character varying(200),
        city character varying(100),
        state character varying(2),
        zip_code character varying(20),
        phone character varying(20),
        email character varying(200),
        plan character varying(50) NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT p_k_tenants PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE TABLE users (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        supabase_user_id character varying(100),
        email character varying(200) NOT NULL,
        name character varying(200),
        role character varying(50) NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT p_k_users PRIMARY KEY (id),
        CONSTRAINT f_k_users_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE TABLE webhooks (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        url character varying(1000) NOT NULL,
        secret character varying(500),
        events jsonb NOT NULL,
        is_active boolean NOT NULL,
        consecutive_failures integer NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT p_k_webhooks PRIMARY KEY (id),
        CONSTRAINT f_k_webhooks_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE TABLE conversations (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        user_id uuid NOT NULL,
        title character varying(200),
        policy_ids jsonb NOT NULL,
        document_ids jsonb NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT p_k_conversations PRIMARY KEY (id),
        CONSTRAINT f_k_conversations__tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE,
        CONSTRAINT f_k_conversations__users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE TABLE documents (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        file_name character varying(500) NOT NULL,
        storage_path character varying(1000) NOT NULL,
        file_size_bytes bigint,
        content_type character varying(100) NOT NULL,
        page_count integer,
        document_type character varying(50),
        processing_status character varying(50) NOT NULL,
        processing_error text,
        processed_at timestamp with time zone,
        uploaded_by_user_id uuid,
        uploaded_at timestamp with time zone NOT NULL,
        submission_group_id uuid,
        CONSTRAINT p_k_documents PRIMARY KEY (id),
        CONSTRAINT f_k_documents__tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE,
        CONSTRAINT f_k_documents__users_uploaded_by_user_id FOREIGN KEY (uploaded_by_user_id) REFERENCES users (id) ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE TABLE webhook_deliveries (
        id uuid NOT NULL,
        webhook_id uuid NOT NULL,
        event character varying(100) NOT NULL,
        payload jsonb NOT NULL,
        status character varying(50) NOT NULL,
        response_status_code integer,
        response_body text,
        error_message text,
        attempt_count integer NOT NULL,
        next_retry_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL,
        delivered_at timestamp with time zone,
        CONSTRAINT p_k_webhook_deliveries PRIMARY KEY (id),
        CONSTRAINT f_k_webhook_deliveries_webhooks_webhook_id FOREIGN KEY (webhook_id) REFERENCES webhooks (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE TABLE messages (
        id uuid NOT NULL,
        conversation_id uuid NOT NULL,
        role character varying(20) NOT NULL,
        content text NOT NULL,
        cited_chunk_ids jsonb NOT NULL,
        prompt_tokens integer,
        completion_tokens integer,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT p_k_messages PRIMARY KEY (id),
        CONSTRAINT f_k_messages_conversations_conversation_id FOREIGN KEY (conversation_id) REFERENCES conversations (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE TABLE contract_requirements (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        name character varying(200) NOT NULL,
        source_document_id uuid,
        gl_each_occurrence_min numeric(14,2),
        gl_aggregate_min numeric(14,2),
        auto_combined_single_min numeric(14,2),
        umbrella_min numeric(14,2),
        wc_required boolean,
        professional_liability_min numeric(14,2),
        additional_insured_required boolean,
        waiver_of_subrogation_required boolean,
        primary_noncontributory_required boolean,
        full_requirements jsonb NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT p_k_contract_requirements PRIMARY KEY (id),
        CONSTRAINT f_k_contract_requirements__documents_source_document_id FOREIGN KEY (source_document_id) REFERENCES documents (id) ON DELETE SET NULL,
        CONSTRAINT f_k_contract_requirements__tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE TABLE document_chunks (
        id uuid NOT NULL,
        document_id uuid NOT NULL,
        chunk_text text NOT NULL,
        chunk_index integer NOT NULL,
        page_start integer,
        page_end integer,
        section_type character varying(100),
        embedding vector(1536),
        token_count integer,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT p_k_document_chunks PRIMARY KEY (id),
        CONSTRAINT f_k_document_chunks_documents_document_id FOREIGN KEY (document_id) REFERENCES documents (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE TABLE policies (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        source_document_id uuid,
        extraction_confidence numeric(3,2),
        policy_status character varying(50) NOT NULL,
        policy_number character varying(100),
        quote_number character varying(100),
        effective_date date,
        expiration_date date,
        quote_expiration_date date,
        carrier_name character varying(200),
        carrier_naic character varying(20),
        insured_name character varying(300),
        insured_address_line1 character varying(200),
        insured_address_line2 character varying(200),
        insured_city character varying(100),
        insured_state character varying(2),
        insured_zip character varying(20),
        total_premium numeric(12,2),
        submission_group_id uuid,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        raw_extraction jsonb,
        CONSTRAINT p_k_policies PRIMARY KEY (id),
        CONSTRAINT f_k_policies__tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE,
        CONSTRAINT f_k_policies_documents_source_document_id FOREIGN KEY (source_document_id) REFERENCES documents (id) ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE TABLE compliance_checks (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        contract_requirement_id uuid NOT NULL,
        policy_ids jsonb NOT NULL,
        is_compliant boolean,
        compliance_score numeric(3,2),
        gaps jsonb NOT NULL,
        summary text,
        checked_at timestamp with time zone NOT NULL,
        checked_by_user_id uuid,
        CONSTRAINT p_k_compliance_checks PRIMARY KEY (id),
        CONSTRAINT "f_k_compliance_checks__contract_requirements_contract_requirement~" FOREIGN KEY (contract_requirement_id) REFERENCES contract_requirements (id) ON DELETE CASCADE,
        CONSTRAINT f_k_compliance_checks__tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE,
        CONSTRAINT f_k_compliance_checks__users_checked_by_user_id FOREIGN KEY (checked_by_user_id) REFERENCES users (id) ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE TABLE coverages (
        id uuid NOT NULL,
        policy_id uuid NOT NULL,
        coverage_type character varying(100) NOT NULL,
        coverage_subtype character varying(100),
        each_occurrence_limit numeric(14,2),
        aggregate_limit numeric(14,2),
        deductible numeric(14,2),
        premium numeric(12,2),
        is_occurrence_form boolean,
        is_claims_made boolean,
        retroactive_date date,
        details jsonb NOT NULL,
        extraction_confidence numeric(3,2),
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT p_k_coverages PRIMARY KEY (id),
        CONSTRAINT f_k_coverages__policies_policy_id FOREIGN KEY (policy_id) REFERENCES policies (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE INDEX i_x_compliance_checks_checked_by_user_id ON compliance_checks (checked_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE INDEX i_x_compliance_checks_contract_requirement_id ON compliance_checks (contract_requirement_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE INDEX i_x_compliance_checks_tenant_id ON compliance_checks (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE INDEX i_x_contract_requirements_source_document_id ON contract_requirements (source_document_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE INDEX i_x_contract_requirements_tenant_id ON contract_requirements (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE INDEX i_x_conversations_tenant_id ON conversations (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE INDEX i_x_conversations_user_id ON conversations (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE INDEX i_x_coverages_policy_id ON coverages (policy_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE INDEX "IX_coverages_policy_id_coverage_type" ON coverages (policy_id, coverage_type);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE INDEX i_x_document_chunks_document_id ON document_chunks (document_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE INDEX i_x_documents_tenant_id ON documents (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE INDEX i_x_documents_uploaded_by_user_id ON documents (uploaded_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE INDEX "IX_documents_submission_group_id" ON documents (submission_group_id) WHERE submission_group_id IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE INDEX "IX_documents_tenant_id_processing_status" ON documents (tenant_id, processing_status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE INDEX "IX_industry_benchmarks_industry_class" ON industry_benchmarks (industry_class);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE INDEX i_x_messages_conversation_id ON messages (conversation_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE INDEX i_x_policies_source_document_id ON policies (source_document_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE INDEX i_x_policies_tenant_id ON policies (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE INDEX "IX_policies_submission_group_id" ON policies (submission_group_id) WHERE submission_group_id IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE INDEX "IX_policies_tenant_id_insured_name" ON policies (tenant_id, insured_name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE INDEX "IX_policies_tenant_id_policy_status" ON policies (tenant_id, policy_status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE INDEX i_x_users_tenant_id ON users (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_users_email" ON users (email);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE INDEX i_x_webhook_deliveries_webhook_id ON webhook_deliveries (webhook_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    CREATE INDEX i_x_webhooks_tenant_id ON webhooks (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251216032340_InitialCreate') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251216032340_InitialCreate', '9.0.1');
    END IF;
END $EF$;
COMMIT;

