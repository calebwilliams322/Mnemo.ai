using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mnemo.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubmissionGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_policies_submission_group_id",
                table: "policies",
                newName: "i_x_policies_submission_group_id");

            migrationBuilder.RenameIndex(
                name: "IX_documents_submission_group_id",
                table: "documents",
                newName: "i_x_documents_submission_group_id");

            migrationBuilder.CreateTable(
                name: "submission_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    insured_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expiration_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_submission_groups", x => x.id);
                    table.ForeignKey(
                        name: "f_k_submission_groups__tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_submission_groups_tenant_id",
                table: "submission_groups",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_submission_groups_tenant_id_insured_name",
                table: "submission_groups",
                columns: new[] { "tenant_id", "insured_name" });

            migrationBuilder.AddForeignKey(
                name: "f_k_documents__submission_groups_submission_group_id",
                table: "documents",
                column: "submission_group_id",
                principalTable: "submission_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "f_k_policies__submission_groups_submission_group_id",
                table: "policies",
                column: "submission_group_id",
                principalTable: "submission_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_documents__submission_groups_submission_group_id",
                table: "documents");

            migrationBuilder.DropForeignKey(
                name: "f_k_policies__submission_groups_submission_group_id",
                table: "policies");

            migrationBuilder.DropTable(
                name: "submission_groups");

            migrationBuilder.RenameIndex(
                name: "i_x_policies_submission_group_id",
                table: "policies",
                newName: "IX_policies_submission_group_id");

            migrationBuilder.RenameIndex(
                name: "i_x_documents_submission_group_id",
                table: "documents",
                newName: "IX_documents_submission_group_id");
        }
    }
}
