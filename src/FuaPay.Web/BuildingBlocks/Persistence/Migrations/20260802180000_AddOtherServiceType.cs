using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuaPay.Web.BuildingBlocks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOtherServiceType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_jobs_jobs_service_type_valid",
                schema: "jobs",
                table: "jobs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_service_units_units_service_type_valid",
                schema: "service_units",
                table: "units");

            migrationBuilder.AddCheckConstraint(
                name: "ck_jobs_jobs_service_type_valid",
                schema: "jobs",
                table: "jobs",
                sql: "service_type IN (1, 2, 3, 4)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_service_units_units_service_type_valid",
                schema: "service_units",
                table: "units",
                sql: "default_service_type IN (1, 2, 3, 4)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_jobs_jobs_service_type_valid",
                schema: "jobs",
                table: "jobs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_service_units_units_service_type_valid",
                schema: "service_units",
                table: "units");

            migrationBuilder.AddCheckConstraint(
                name: "ck_jobs_jobs_service_type_valid",
                schema: "jobs",
                table: "jobs",
                sql: "service_type IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_service_units_units_service_type_valid",
                schema: "service_units",
                table: "units",
                sql: "default_service_type IN (1, 2, 3)");
        }
    }
}
