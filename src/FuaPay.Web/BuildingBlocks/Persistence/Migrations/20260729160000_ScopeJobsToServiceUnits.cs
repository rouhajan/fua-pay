using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuaPay.Web.BuildingBlocks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopeJobsToServiceUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                schema: "jobs",
                table: "jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "job_number",
                schema: "jobs",
                table: "jobs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "service_unit_id",
                schema: "jobs",
                table: "jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    IF EXISTS
                    (
                        SELECT 1
                        FROM jobs.jobs AS j
                        WHERE NOT EXISTS
                        (
                            SELECT 1
                            FROM service_units.units AS u
                            WHERE u.code = CASE j.service_type
                                WHEN 1 THEN '3D'
                                WHEN 2 THEN 'PLT'
                                WHEN 3 THEN 'LAS'
                            END
                        )
                    ) THEN
                        RAISE EXCEPTION
                            'Zakázky nelze převést na pracoviště. Chybí kód 3D, PLT nebo LAS.';
                    END IF;

                    IF EXISTS
                    (
                        SELECT 1
                        FROM jobs.jobs AS j
                        INNER JOIN service_units.units AS u
                            ON u.code = CASE j.service_type
                                WHEN 1 THEN '3D'
                                WHEN 2 THEN 'PLT'
                                WHEN 3 THEN 'LAS'
                            END
                        GROUP BY
                            u.id,
                            EXTRACT(YEAR FROM j.created_at AT TIME ZONE 'UTC')
                        HAVING COUNT(*) > 999999
                    ) THEN
                        RAISE EXCEPTION
                            'Některá roční číselná řada zakázek překračuje 999999 položek.';
                    END IF;
                END
                $migration$;

                UPDATE jobs.jobs AS j
                SET
                    created_by_user_id = j.requester_user_id,
                    service_unit_id = u.id
                FROM service_units.units AS u
                WHERE u.code = CASE j.service_type
                    WHEN 1 THEN '3D'
                    WHEN 2 THEN 'PLT'
                    WHEN 3 THEN 'LAS'
                END;

                WITH numbered AS
                (
                    SELECT
                        j.id,
                        u.code,
                        EXTRACT(YEAR FROM j.created_at AT TIME ZONE 'UTC')::integer AS job_year,
                        ROW_NUMBER() OVER
                        (
                            PARTITION BY
                                j.service_unit_id,
                                EXTRACT(YEAR FROM j.created_at AT TIME ZONE 'UTC')
                            ORDER BY j.created_at, j.id
                        ) AS sequence_value
                    FROM jobs.jobs AS j
                    INNER JOIN service_units.units AS u
                        ON u.id = j.service_unit_id
                )
                UPDATE jobs.jobs AS j
                SET job_number =
                    numbered.code || '-' ||
                    numbered.job_year::text || '-' ||
                    LPAD(numbered.sequence_value::text, 6, '0')
                FROM numbered
                WHERE numbered.id = j.id;

                DO $migration$
                BEGIN
                    IF EXISTS
                    (
                        SELECT 1
                        FROM jobs.jobs
                        WHERE
                            created_by_user_id IS NULL OR
                            service_unit_id IS NULL OR
                            job_number IS NULL
                    ) THEN
                        RAISE EXCEPTION
                            'Převod zakázek na pracoviště nebyl úplný.';
                    END IF;
                END
                $migration$;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_user_id",
                schema: "jobs",
                table: "jobs",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "job_number",
                schema: "jobs",
                table: "jobs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "service_unit_id",
                schema: "jobs",
                table: "jobs",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "job_number_sequences",
                schema: "jobs",
                columns: table => new
                {
                    service_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    last_value = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "pk_jobs_job_number_sequences",
                        x => new { x.service_unit_id, x.year });
                    table.CheckConstraint(
                        "ck_jobs_number_sequences_unit_not_empty",
                        "service_unit_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint(
                        "ck_jobs_number_sequences_value_valid",
                        "last_value BETWEEN 1 AND 999999");
                    table.CheckConstraint(
                        "ck_jobs_number_sequences_year_valid",
                        "year BETWEEN 2000 AND 9999");
                });

            migrationBuilder.Sql(
                """
                INSERT INTO jobs.job_number_sequences
                    (service_unit_id, year, last_value)
                SELECT
                    service_unit_id,
                    EXTRACT(YEAR FROM created_at AT TIME ZONE 'UTC')::integer,
                    MAX(RIGHT(job_number, 6)::integer)
                FROM jobs.jobs
                GROUP BY
                    service_unit_id,
                    EXTRACT(YEAR FROM created_at AT TIME ZONE 'UTC');
                """);

            migrationBuilder.DropIndex(
                name: "ix_jobs_jobs_requester_created_at",
                schema: "jobs",
                table: "jobs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_jobs_jobs_requester_not_empty",
                schema: "jobs",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "requester_user_id",
                schema: "jobs",
                table: "jobs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_jobs_jobs_created_by_not_empty",
                schema: "jobs",
                table: "jobs",
                sql: "created_by_user_id <> '00000000-0000-0000-0000-000000000000'::uuid");

            migrationBuilder.AddCheckConstraint(
                name: "ck_jobs_jobs_number_valid",
                schema: "jobs",
                table: "jobs",
                sql: "job_number ~ '^[A-Z0-9]{2,8}-[0-9]{4}-[0-9]{6}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_jobs_jobs_service_unit_not_empty",
                schema: "jobs",
                table: "jobs",
                sql: "service_unit_id <> '00000000-0000-0000-0000-000000000000'::uuid");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_jobs_created_by_created_at",
                schema: "jobs",
                table: "jobs",
                columns: new[] { "created_by_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_jobs_service_unit_created_at",
                schema: "jobs",
                table: "jobs",
                columns: new[] { "service_unit_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "uq_jobs_jobs_number",
                schema: "jobs",
                table: "jobs",
                column: "job_number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_jobs_jobs_created_by_created_at",
                schema: "jobs",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "ix_jobs_jobs_service_unit_created_at",
                schema: "jobs",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "uq_jobs_jobs_number",
                schema: "jobs",
                table: "jobs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_jobs_jobs_created_by_not_empty",
                schema: "jobs",
                table: "jobs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_jobs_jobs_number_valid",
                schema: "jobs",
                table: "jobs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_jobs_jobs_service_unit_not_empty",
                schema: "jobs",
                table: "jobs");

            migrationBuilder.AddColumn<Guid>(
                name: "requester_user_id",
                schema: "jobs",
                table: "jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE jobs.jobs
                SET requester_user_id = created_by_user_id;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "requester_user_id",
                schema: "jobs",
                table: "jobs",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropTable(
                name: "job_number_sequences",
                schema: "jobs");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                schema: "jobs",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "job_number",
                schema: "jobs",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "service_unit_id",
                schema: "jobs",
                table: "jobs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_jobs_jobs_requester_not_empty",
                schema: "jobs",
                table: "jobs",
                sql: "requester_user_id <> '00000000-0000-0000-0000-000000000000'::uuid");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_jobs_requester_created_at",
                schema: "jobs",
                table: "jobs",
                columns: new[] { "requester_user_id", "created_at" });
        }
    }
}
