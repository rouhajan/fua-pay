using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuaPay.Web.Modules.Jobs.Infrastructure.Persistence;

internal sealed class JobNumberSequenceConfiguration :
    IEntityTypeConfiguration<JobNumberSequenceEntity>
{
    public void Configure(
        EntityTypeBuilder<JobNumberSequenceEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "job_number_sequences",
            "jobs",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_jobs_number_sequences_unit_not_empty",
                    "service_unit_id <> '00000000-0000-0000-0000-000000000000'::uuid");

                table.HasCheckConstraint(
                    "ck_jobs_number_sequences_year_valid",
                    "year BETWEEN 2000 AND 9999");

                table.HasCheckConstraint(
                    "ck_jobs_number_sequences_value_valid",
                    "last_value BETWEEN 1 AND 999999");
            });

        builder.HasKey(
                item => new
                {
                    item.ServiceUnitId,
                    item.Year
                })
            .HasName("pk_jobs_job_number_sequences");

        builder.Property(item => item.ServiceUnitId)
            .HasColumnName("service_unit_id")
            .ValueGeneratedNever();

        builder.Property(item => item.Year)
            .HasColumnName("year")
            .ValueGeneratedNever();

        builder.Property(item => item.LastValue)
            .HasColumnName("last_value")
            .IsRequired();
    }
}
