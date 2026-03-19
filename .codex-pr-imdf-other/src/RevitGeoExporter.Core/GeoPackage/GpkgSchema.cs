using System;
using Microsoft.Data.Sqlite;
using RevitGeoExporter.Core.Coordinates;

namespace RevitGeoExporter.Core.GeoPackage;

public static class GpkgSchema
{
    public static void EnsureCoreTables(SqliteConnection connection, SqliteTransaction transaction)
    {
        if (connection is null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        if (transaction is null)
        {
            throw new ArgumentNullException(nameof(transaction));
        }

        ExecuteNonQuery(
            connection,
            transaction,
            @"CREATE TABLE IF NOT EXISTS gpkg_spatial_ref_sys (
                srs_name TEXT NOT NULL,
                srs_id INTEGER NOT NULL PRIMARY KEY,
                organization TEXT NOT NULL,
                organization_coordsys_id INTEGER NOT NULL,
                definition TEXT NOT NULL,
                description TEXT
              );");

        ExecuteNonQuery(
            connection,
            transaction,
            @"CREATE TABLE IF NOT EXISTS gpkg_contents (
                table_name TEXT NOT NULL PRIMARY KEY,
                data_type TEXT NOT NULL,
                identifier TEXT UNIQUE,
                description TEXT DEFAULT '',
                last_change DATETIME NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                min_x DOUBLE,
                min_y DOUBLE,
                max_x DOUBLE,
                max_y DOUBLE,
                srs_id INTEGER,
                CONSTRAINT fk_gc_r_srs_id FOREIGN KEY (srs_id) REFERENCES gpkg_spatial_ref_sys(srs_id)
              );");

        ExecuteNonQuery(
            connection,
            transaction,
            @"CREATE TABLE IF NOT EXISTS gpkg_geometry_columns (
                table_name TEXT NOT NULL,
                column_name TEXT NOT NULL,
                geometry_type_name TEXT NOT NULL,
                srs_id INTEGER NOT NULL,
                z TINYINT NOT NULL DEFAULT 0,
                m TINYINT NOT NULL DEFAULT 0,
                PRIMARY KEY (table_name, column_name),
                CONSTRAINT fk_gc_tn FOREIGN KEY (table_name) REFERENCES gpkg_contents(table_name),
                CONSTRAINT fk_gc_srs FOREIGN KEY (srs_id) REFERENCES gpkg_spatial_ref_sys(srs_id)
              );");
    }

    public static void EnsureSpatialReference(SqliteConnection connection, SqliteTransaction transaction, int srsId)
    {
        if (connection is null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        if (transaction is null)
        {
            throw new ArgumentNullException(nameof(transaction));
        }

        InsertSpatialReference(
            connection,
            transaction,
            srsName: "Undefined Cartesian SRS",
            srsId: -1,
            organization: "NONE",
            organizationCoordsysId: -1,
            definition: "undefined",
            description: "undefined cartesian coordinate reference system");

        InsertSpatialReference(
            connection,
            transaction,
            srsName: "Undefined geographic SRS",
            srsId: 0,
            organization: "NONE",
            organizationCoordsysId: 0,
            definition: "undefined",
            description: "undefined geographic coordinate reference system");

        InsertSpatialReference(
            connection,
            transaction,
            srsName: "WGS 84",
            srsId: 4326,
            organization: "EPSG",
            organizationCoordsysId: 4326,
            definition: "GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\"],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]]",
            description: "longitude/latitude coordinates in decimal degrees on the WGS 84 spheroid");

        if (srsId != -1 && srsId != 0 && srsId != 4326)
        {
            string definition = CoordinateSystemCatalog.TryGetDefinitionWkt(srsId, out string knownDefinition)
                ? knownDefinition
                : "undefined";
            InsertSpatialReference(
                connection,
                transaction,
                srsName: $"EPSG:{srsId}",
                srsId: srsId,
                organization: "EPSG",
                organizationCoordsysId: srsId,
                definition: definition,
                description: "custom coordinate reference system");
        }
    }

    private static void InsertSpatialReference(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string srsName,
        int srsId,
        string organization,
        int organizationCoordsysId,
        string definition,
        string description)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            @"INSERT OR IGNORE INTO gpkg_spatial_ref_sys
              (srs_name, srs_id, organization, organization_coordsys_id, definition, description)
              VALUES ($srs_name, $srs_id, $organization, $organization_coordsys_id, $definition, $description);";
        command.Parameters.AddWithValue("$srs_name", srsName);
        command.Parameters.AddWithValue("$srs_id", srsId);
        command.Parameters.AddWithValue("$organization", organization);
        command.Parameters.AddWithValue("$organization_coordsys_id", organizationCoordsysId);
        command.Parameters.AddWithValue("$definition", definition);
        command.Parameters.AddWithValue("$description", description);
        command.ExecuteNonQuery();
    }

    private static void ExecuteNonQuery(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
