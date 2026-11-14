using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Os.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Alinha o schema Quartz ao da v3.22.0 (tables_postgres.sql oficial): colunas exigidas pelo
    /// AdoJobStore atual que o script embutido da migration "Quartz" (versão antiga) não cria.
    /// </summary>
    public partial class QuartzV322 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE qrtz_triggers " +
                "ADD COLUMN IF NOT EXISTS misfire_orig_fire_time BIGINT NULL, " +
                "ADD COLUMN IF NOT EXISTS execution_group VARCHAR(200) NULL, " +
                "ADD COLUMN IF NOT EXISTS preferred_node VARCHAR(200) NULL, " +
                "ADD COLUMN IF NOT EXISTS preferred_node_auto BOOL NOT NULL DEFAULT FALSE;");
            migrationBuilder.Sql(
                "ALTER TABLE qrtz_fired_triggers ADD COLUMN IF NOT EXISTS execution_group VARCHAR(200) NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE qrtz_triggers DROP COLUMN IF EXISTS misfire_orig_fire_time, " +
                "DROP COLUMN IF EXISTS execution_group, DROP COLUMN IF EXISTS preferred_node, " +
                "DROP COLUMN IF EXISTS preferred_node_auto;");
            migrationBuilder.Sql("ALTER TABLE qrtz_fired_triggers DROP COLUMN IF EXISTS execution_group;");
        }
    }
}