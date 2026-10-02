using Microsoft.EntityFrameworkCore;

namespace Tdla.Web;

public static class SchemaUpgrade
{
    // Additive, restart-safe upgrade from the first release; existing accounts/files are retained.
    public static async Task ApplyAsync(AppDb db)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var lockCommand = db.Database.GetDbConnection().CreateCommand();
            lockCommand.CommandText = "SELECT GET_LOCK(CONCAT(DATABASE(), ':tdla-schema'), 30)";
            if (Convert.ToInt32(await lockCommand.ExecuteScalarAsync()) != 1)
                throw new InvalidOperationException("Could not acquire the database schema upgrade lock.");
            try
            {
                await AddColumn(db, "User", "UserFullName", "varchar(200) NOT NULL DEFAULT ''");
                await AddColumn(db, "User", "UserEmployerName", "varchar(200) NOT NULL DEFAULT ''");
                await AddColumn(db, "User", "UserVerified", "tinyint(1) NOT NULL DEFAULT 0");
                await AddColumn(db, "Setting", "SettingSchemaVersion", "int NOT NULL DEFAULT 1");
                // This happens once when upgrading; later administrator choices are preserved.
                await db.Database.ExecuteSqlRawAsync("UPDATE `Setting` SET `SettingRegistrationEnabled` = 1, `SettingSchemaVersion` = 2 WHERE `SettingSchemaVersion` < 2");
                await AddColumn(db, "File", "FileDescription", "varchar(4000) NOT NULL DEFAULT ''");
                await db.Database.ExecuteSqlRawAsync("UPDATE `Setting` SET `SettingSchemaVersion` = 3 WHERE `SettingSchemaVersion` < 3");
            }
            finally
            {
                lockCommand.CommandText = "SELECT RELEASE_LOCK(CONCAT(DATABASE(), ':tdla-schema'))";
                await lockCommand.ExecuteScalarAsync();
            }
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private static async Task AddColumn(AppDb db, string table, string column, string definition)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND COLUMN_NAME = @column";
        var tableParameter = command.CreateParameter(); tableParameter.ParameterName = "@table"; tableParameter.Value = table; command.Parameters.Add(tableParameter);
        var columnParameter = command.CreateParameter(); columnParameter.ParameterName = "@column"; columnParameter.Value = column; command.Parameters.Add(columnParameter);
        if (Convert.ToInt32(await command.ExecuteScalarAsync()) == 0)
        {
            // Identifiers and definitions come only from the fixed list above, never request input.
            command.Parameters.Clear();
            command.CommandText = $"ALTER TABLE `{table}` ADD COLUMN `{column}` {definition}";
            await command.ExecuteNonQueryAsync();
        }
    }
}
