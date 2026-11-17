using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Os.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Quartz : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            using var s = typeof(OsDbContext).Assembly.GetManifestResourceStream(
                "Os.Infrastructure.Persistence.Sql.quartz_tables_postgres.sql")!;
            using var r = new StreamReader(s);
            migrationBuilder.Sql(r.ReadToEnd());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var t in new[] { "qrtz_fired_triggers", "qrtz_paused_trigger_grps", "qrtz_scheduler_state", "qrtz_locks",
                         "qrtz_simprop_triggers", "qrtz_simple_triggers", "qrtz_cron_triggers", "qrtz_blob_triggers",
                         "qrtz_triggers", "qrtz_job_details", "qrtz_calendars" })
                migrationBuilder.Sql($"DROP TABLE IF EXISTS {t} CASCADE;");
        }
    }
}