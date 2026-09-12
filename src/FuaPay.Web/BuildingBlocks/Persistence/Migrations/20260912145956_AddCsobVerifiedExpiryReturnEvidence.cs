using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuaPay.Web.BuildingBlocks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCsobVerifiedExpiryReturnEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "verified_expiry_observed_at",
                schema: "payments",
                table: "csob_payment_reconciliation",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "verified_expiry_payment_status",
                schema: "payments",
                table: "csob_payment_reconciliation",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "verified_expiry_result_code",
                schema: "payments",
                table: "csob_payment_reconciliation",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "verified_expiry_return_dttm",
                schema: "payments",
                table: "csob_payment_reconciliation",
                type: "character varying(14)",
                maxLength: 14,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "verified_expiry_signature",
                schema: "payments",
                table: "csob_payment_reconciliation",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "verified_expiry_text_to_sign",
                schema: "payments",
                table: "csob_payment_reconciliation",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_csob_reconciliation_verified_expiry_consistent",
                schema: "payments",
                table: "csob_payment_reconciliation",
                sql: "(verified_expiry_return_dttm IS NULL AND verified_expiry_result_code IS NULL AND verified_expiry_payment_status IS NULL AND verified_expiry_text_to_sign IS NULL AND verified_expiry_signature IS NULL AND verified_expiry_observed_at IS NULL) OR (verified_expiry_return_dttm IS NOT NULL AND length(verified_expiry_return_dttm) = 14 AND verified_expiry_return_dttm ~ '^[0-9]{14}$' AND verified_expiry_result_code = 130 AND verified_expiry_payment_status = 6 AND verified_expiry_text_to_sign IS NOT NULL AND length(verified_expiry_text_to_sign) > 0 AND verified_expiry_signature IS NOT NULL AND length(verified_expiry_signature) > 0 AND verified_expiry_observed_at IS NOT NULL AND verified_expiry_observed_at >= created_at)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_csob_reconciliation_verified_expiry_consistent",
                schema: "payments",
                table: "csob_payment_reconciliation");

            migrationBuilder.DropColumn(
                name: "verified_expiry_observed_at",
                schema: "payments",
                table: "csob_payment_reconciliation");

            migrationBuilder.DropColumn(
                name: "verified_expiry_payment_status",
                schema: "payments",
                table: "csob_payment_reconciliation");

            migrationBuilder.DropColumn(
                name: "verified_expiry_result_code",
                schema: "payments",
                table: "csob_payment_reconciliation");

            migrationBuilder.DropColumn(
                name: "verified_expiry_return_dttm",
                schema: "payments",
                table: "csob_payment_reconciliation");

            migrationBuilder.DropColumn(
                name: "verified_expiry_signature",
                schema: "payments",
                table: "csob_payment_reconciliation");

            migrationBuilder.DropColumn(
                name: "verified_expiry_text_to_sign",
                schema: "payments",
                table: "csob_payment_reconciliation");
        }
    }
}
