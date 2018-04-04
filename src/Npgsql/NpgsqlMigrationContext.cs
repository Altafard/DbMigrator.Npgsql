using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text;
using System.Threading.Tasks;
using AltaDigital.DbMigrator.Configurations;
using AltaDigital.DbMigrator.Core;
using AltaDigital.DbMigrator.Exceptions;
using AltaDigital.DbMigrator.Npgsql.Resources;
using Npgsql;
using NpgsqlTypes;

namespace AltaDigital.DbMigrator.Npgsql
{
    /// <inheritdoc />
    /// <summary>
    /// Npgsql context for DbMigrator.
    /// </summary>
    public sealed class NpgsqlMigrationContext : MigrationContextBase
    {
        private readonly NpgsqlConnection _connection;

        /// <summary>
        /// Ctor
        /// </summary>
        public NpgsqlMigrationContext(MigrationContextConfig cfg) : base(cfg)
        {
            _connection = new NpgsqlConnection(cfg.ConnectionString);
        }

        /// <inheritdoc />
        protected override async Task InsertMigrationAsync(IMigration migration, string name)
        {
            await _connection.OpenAsync();
            using (NpgsqlCommand command = this._connection.CreateCommand())
            {
                command.CommandText = Sql.InsertMigration;
                command.Parameters.AddWithValue("key", NpgsqlDbType.Bigint, migration.Key);
                command.Parameters.AddWithValue("date", NpgsqlDbType.Timestamp, DateTime.UtcNow);
                command.Parameters.AddWithValue("type", NpgsqlDbType.Text, name ?? migration.GetType().Name);
                await command.ExecuteNonQueryAsync();
            }
            _connection.Close();
        }

        /// <inheritdoc />
        protected override async Task RemoveMigrationAsync(IMigration migration)
        {
            await _connection.OpenAsync();
            using (NpgsqlCommand command = this._connection.CreateCommand())
            {
                command.CommandText = Sql.RemoveMigration;
                command.Parameters.AddWithValue("key", NpgsqlDbType.Bigint, migration.Key);
                await command.ExecuteNonQueryAsync();
            }
            _connection.Close();
        }

        /// <inheritdoc />
        protected override async Task EnsureExistDatabaseAsync()
        {
            if (Configuration.ConnectionClaims.ContainsKey("Server") == false)
                throw new MigrationContextException("Cannot detect host server claim");
            if (Configuration.ConnectionClaims.ContainsKey("Port") == false)
                throw new MigrationContextException("Cannot detect host port claim");
            if (Configuration.ConnectionClaims.ContainsKey("User ID") == false)
                throw new MigrationContextException("Cannot detect user name claim");
            if (Configuration.ConnectionClaims.ContainsKey("Password") == false)
                throw new MigrationContextException("Cannot detect password claim");
            if (Configuration.ConnectionClaims.ContainsKey("Database") == false)
                throw new MigrationContextException("Cannot detect database name claim");

            var connectionStringBuilder = new StringBuilder();
            connectionStringBuilder
                .Append($"Server={Configuration.ConnectionClaims["Server"]};")
                .Append($"Port={Configuration.ConnectionClaims["Port"]};")
                .Append($"User ID={Configuration.ConnectionClaims["User ID"]};")
                .Append($"Password={Configuration.ConnectionClaims["Password"]};")
                .Append("Database=postgres;")
                ;
            _connection.ConnectionString = connectionStringBuilder.ToString();

            await _connection.OpenAsync();
            if (await CheckIfDatabaseExists() == false) await CreateDatabase();
            _connection.Close();

            _connection.ConnectionString = Configuration.ConnectionString;
        }

        /// <inheritdoc />
        protected override async Task EnsureExistMigrationTableAsync()
        {
            await _connection.OpenAsync();
            using (NpgsqlCommand command = _connection.CreateCommand())
            {
                command.CommandText = Sql.CreateTableIfNotExists;
                await command.ExecuteNonQueryAsync();
            }
            _connection.Close();
        }

        /// <inheritdoc />
        protected override async Task<Dictionary<long, string>> LoadMigrationsAsync()
        {
            var temp = new Dictionary<long, string>();
            await _connection.OpenAsync();
            using (NpgsqlCommand command = _connection.CreateCommand())
            {
                command.CommandText = Sql.SelectAppliedMigrations;

                using (DbDataReader reader = await command.ExecuteReaderAsync())
                {
                    if (reader.HasRows)
                    {
                        while (reader.Read())
                        {
                            long key = reader.GetInt64(0);
                            string type = reader.GetString(1);
                            temp.Add(key, type);
                        }
                    }
                }
            }
            _connection.Close();
            return temp;
        }

        /// <inheritdoc />
        public override async Task ExecuteAsync(string sql)
        {
            await _connection.OpenAsync();
            using (NpgsqlCommand command = _connection.CreateCommand())
            {
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync();
            }
            _connection.Close();
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            _connection.Close();
            _connection.Dispose();
        }

        #region ' Privates methods '

        private async Task<bool> CheckIfDatabaseExists()
        {
            using (NpgsqlCommand command = _connection.CreateCommand())
            {
                command.CommandText = Sql.CheckIfDatabaseExists;
                command.Parameters.AddWithValue("dbName", NpgsqlDbType.Text, Configuration.ConnectionClaims["Database"]);
                object result = await command.ExecuteScalarAsync();
                return (bool?) result ?? false;
            }
        }

        private async Task CreateDatabase()
        {
            using (NpgsqlCommand command = _connection.CreateCommand())
            {
                command.CommandText = string.Format(Sql.CreateDatabase,
                    Configuration.ConnectionClaims["Database"],
                    Configuration.ConnectionClaims["User ID"]);
                await command.ExecuteNonQueryAsync();
            }
        }

        #endregion
    }
}
