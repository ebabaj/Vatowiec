-- Historyczny skrypt starej architektury. Vatowiec_Dev została usunięta za zgodą użytkownika.
-- Aktualna inspekcja: dotnet run --project tools/database/Vatowiec.DatabaseTools -- inspect
-- Poniżej zachowano dawny odczyt katalogu i liczby rekordów; nie dotyczy nowych baz.
SET NOCOUNT ON;
IF DB_NAME() <> N'Vatowiec_Dev'
    THROW 50001, 'Dozwolona jest wyłącznie baza Vatowiec_Dev.', 1;

SELECT DB_NAME() AS DatabaseName, SERVERPROPERTY('ProductVersion') AS SqlServerVersion;

SELECT s.name AS SchemaName, t.name AS TableName
FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
ORDER BY s.name, t.name;

SELECT OBJECT_SCHEMA_NAME(c.object_id) AS SchemaName,
       OBJECT_NAME(c.object_id) AS TableName, c.name AS ColumnName,
       TYPE_NAME(c.user_type_id) AS SqlType, c.max_length, c.is_nullable
FROM sys.columns c
WHERE c.object_id IN (OBJECT_ID(N'core.Companies'), OBJECT_ID(N'core.Contractors'))
ORDER BY c.object_id, c.column_id;

SELECT OBJECT_NAME(i.object_id) AS TableName, i.name AS IndexName,
       i.is_primary_key, i.is_unique, c.name AS ColumnName, ic.key_ordinal
FROM sys.indexes i
JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE i.object_id IN (OBJECT_ID(N'core.Companies'), OBJECT_ID(N'core.Contractors'))
ORDER BY i.object_id, i.index_id, ic.key_ordinal;

SELECT name, OBJECT_NAME(parent_object_id) AS TableName,
       OBJECT_NAME(referenced_object_id) AS ReferencedTable,
       delete_referential_action_desc, is_disabled, is_not_trusted
FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'core.Contractors');

SELECT name, definition, is_disabled, is_not_trusted
FROM sys.check_constraints
WHERE parent_object_id IN (OBJECT_ID(N'core.Companies'), OBJECT_ID(N'core.Contractors'));

SELECT MigrationId, ProductVersion FROM dbo.__EFMigrationsHistory;
SELECT N'core.Companies' AS TableName, COUNT_BIG(*) AS RecordCount FROM core.Companies
UNION ALL
SELECT N'core.Contractors', COUNT_BIG(*) FROM core.Contractors;
