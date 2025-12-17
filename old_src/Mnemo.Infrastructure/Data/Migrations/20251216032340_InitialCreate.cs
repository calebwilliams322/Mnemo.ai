using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace Mnemo.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "industry_benchmarks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    industry_class = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    naics_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    sic_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    recommended_coverages = table.Column<string>(type: "jsonb", nullable: false),
                    source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_industry_benchmarks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    address_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    state = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    zip_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    plan = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supabase_user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_users", x => x.id);
                    table.ForeignKey(
                        name: "f_k_users_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "webhooks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    secret = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    events = table.Column<string>(type: "jsonb", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_webhooks", x => x.id);
                    table.ForeignKey(
                        name: "f_k_webhooks_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    policy_ids = table.Column<string>(type: "jsonb", nullable: false),
                    document_ids = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_conversations", x => x.id);
                    table.ForeignKey(
                        name: "f_k_conversations__tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_conversations__users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    storage_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    page_count = table.Column<int>(type: "integer", nullable: true),
                    document_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    processing_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    processing_error = table.Column<string>(type: "text", nullable: true),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    uploaded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    submission_group_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_documents", x => x.id);
                    table.ForeignKey(
                        name: "f_k_documents__tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_documents__users_uploaded_by_user_id",
                        column: x => x.uploaded_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "webhook_deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    webhook_id = table.Column<Guid>(type: "uuid", nullable: false),
                    @event = table.Column<string>(name: "event", type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    response_status_code = table.Column<int>(type: "integer", nullable: true),
                    response_body = table.Column<string>(type: "text", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_retry_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    delivered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_webhook_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "f_k_webhook_deliveries_webhooks_webhook_id",
                        column: x => x.webhook_id,
                        principalTable: "webhooks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    cited_chunk_ids = table.Column<string>(type: "jsonb", nullable: false),
                    prompt_tokens = table.Column<int>(type: "integer", nullable: true),
                    completion_tokens = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_messages", x => x.id);
                    table.ForeignKey(
                        name: "f_k_messages_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contract_requirements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    gl_each_occurrence_min = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    gl_aggregate_min = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    auto_combined_single_min = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    umbrella_min = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    wc_required = table.Column<bool>(type: "boolean", nullable: true),
                    professional_liability_min = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    additional_insured_required = table.Column<bool>(type: "boolean", nullable: true),
                    waiver_of_subrogation_required = table.Column<bool>(type: "boolean", nullable: true),
                    primary_noncontributory_required = table.Column<bool>(type: "boolean", nullable: true),
                    full_requirements = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_contract_requirements", x => x.id);
                    table.ForeignKey(
                        name: "f_k_contract_requirements__documents_source_document_id",
                        column: x => x.source_document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_contract_requirements__tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "document_chunks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chunk_text = table.Column<string>(type: "text", nullable: false),
                    chunk_index = table.Column<int>(type: "integer", nullable: false),
                    page_start = table.Column<int>(type: "integer", nullable: true),
                    page_end = table.Column<int>(type: "integer", nullable: true),
                    section_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    embedding = table.Column<Vector>(type: "vector(1536)", nullable: true),
                    token_count = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_document_chunks", x => x.id);
                    table.ForeignKey(
                        name: "f_k_document_chunks_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    extraction_confidence = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: true),
                    policy_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    policy_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    quote_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expiration_date = table.Column<DateOnly>(type: "date", nullable: true),
                    quote_expiration_date = table.Column<DateOnly>(type: "date", nullable: true),
                    carrier_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    carrier_naic = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    insured_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    insured_address_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    insured_address_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    insured_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    insured_state = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    insured_zip = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    total_premium = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    submission_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    raw_extraction = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_policies", x => x.id);
                    table.ForeignKey(
                        name: "f_k_policies__tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_policies_documents_source_document_id",
                        column: x => x.source_document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "compliance_checks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_requirement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_ids = table.Column<string>(type: "jsonb", nullable: false),
                    is_compliant = table.Column<bool>(type: "boolean", nullable: true),
                    compliance_score = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: true),
                    gaps = table.Column<string>(type: "jsonb", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: true),
                    checked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    checked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_compliance_checks", x => x.id);
                    table.ForeignKey(
                        name: "f_k_compliance_checks__contract_requirements_contract_requirement~",
                        column: x => x.contract_requirement_id,
                        principalTable: "contract_requirements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_compliance_checks__tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_compliance_checks__users_checked_by_user_id",
                        column: x => x.checked_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "coverages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    coverage_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    coverage_subtype = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    each_occurrence_limit = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    aggregate_limit = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    deductible = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    premium = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    is_occurrence_form = table.Column<bool>(type: "boolean", nullable: true),
                    is_claims_made = table.Column<bool>(type: "boolean", nullable: true),
                    retroactive_date = table.Column<DateOnly>(type: "date", nullable: true),
                    details = table.Column<string>(type: "jsonb", nullable: false),
                    extraction_confidence = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_coverages", x => x.id);
                    table.ForeignKey(
                        name: "f_k_coverages__policies_policy_id",
                        column: x => x.policy_id,
                        principalTable: "policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_compliance_checks_checked_by_user_id",
                table: "compliance_checks",
                column: "checked_by_user_id");

            migrationBuilder.CreateIndex(
                name: "i_x_compliance_checks_contract_requirement_id",
                table: "compliance_checks",
                column: "contract_requirement_id");

            migrationBuilder.CreateIndex(
                name: "i_x_compliance_checks_tenant_id",
                table: "compliance_checks",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "i_x_contract_requirements_source_document_id",
                table: "contract_requirements",
                column: "source_document_id");

            migrationBuilder.CreateIndex(
                name: "i_x_contract_requirements_tenant_id",
                table: "contract_requirements",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "i_x_conversations_tenant_id",
                table: "conversations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "i_x_conversations_user_id",
                table: "conversations",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "i_x_coverages_policy_id",
                table: "coverages",
                column: "policy_id");

            migrationBuilder.CreateIndex(
                name: "IX_coverages_policy_id_coverage_type",
                table: "coverages",
                columns: new[] { "policy_id", "coverage_type" });

            migrationBuilder.CreateIndex(
                name: "i_x_document_chunks_document_id",
                table: "document_chunks",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "i_x_documents_tenant_id",
                table: "documents",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "i_x_documents_uploaded_by_user_id",
                table: "documents",
                column: "uploaded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_documents_submission_group_id",
                table: "documents",
                column: "submission_group_id",
                filter: "submission_group_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_documents_tenant_id_processing_status",
                table: "documents",
                columns: new[] { "tenant_id", "processing_status" });

            migrationBuilder.CreateIndex(
                name: "IX_industry_benchmarks_industry_class",
                table: "industry_benchmarks",
                column: "industry_class");

            migrationBuilder.CreateIndex(
                name: "i_x_messages_conversation_id",
                table: "messages",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "i_x_policies_source_document_id",
                table: "policies",
                column: "source_document_id");

            migrationBuilder.CreateIndex(
                name: "i_x_policies_tenant_id",
                table: "policies",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_policies_submission_group_id",
                table: "policies",
                column: "submission_group_id",
                filter: "submission_group_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_policies_tenant_id_insured_name",
                table: "policies",
                columns: new[] { "tenant_id", "insured_name" });

            migrationBuilder.CreateIndex(
                name: "IX_policies_tenant_id_policy_status",
                table: "policies",
                columns: new[] { "tenant_id", "policy_status" });

            migrationBuilder.CreateIndex(
                name: "i_x_users_tenant_id",
                table: "users",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_webhook_deliveries_webhook_id",
                table: "webhook_deliveries",
                column: "webhook_id");

            migrationBuilder.CreateIndex(
                name: "i_x_webhooks_tenant_id",
                table: "webhooks",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "compliance_checks");

            migrationBuilder.DropTable(
                name: "coverages");

            migrationBuilder.DropTable(
                name: "document_chunks");

            migrationBuilder.DropTable(
                name: "industry_benchmarks");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "webhook_deliveries");

            migrationBuilder.DropTable(
                name: "contract_requirements");

            migrationBuilder.DropTable(
                name: "policies");

            migrationBuilder.DropTable(
                name: "conversations");

            migrationBuilder.DropTable(
                name: "webhooks");

            migrationBuilder.DropTable(
                name: "documents");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "tenants");
        }
    }
}
