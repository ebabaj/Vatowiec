using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.Company
{
    /// <inheritdoc />
    public partial class CompanyModelV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ref");

            migrationBuilder.EnsureSchema(
                name: "core");

            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "AddressTypes",
                schema: "ref",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AddressTypes", x => x.Id);
                    table.CheckConstraint("CK_AddressTypes_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
                });

            migrationBuilder.CreateTable(
                name: "CompanyProfile",
                schema: "core",
                columns: table => new
                {
                    SingletonKey = table.Column<byte>(type: "tinyint", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Nip = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Regon = table.Column<string>(type: "nvarchar(14)", maxLength: 14, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyProfile", x => x.SingletonKey);
                    table.UniqueConstraint("AK_CompanyProfile_CompanyId", x => x.CompanyId);
                    table.CheckConstraint("CK_CompanyProfile_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_CompanyProfile_Dates", "[UpdatedAtUtc] >= [CreatedAtUtc]");
                    table.CheckConstraint("CK_CompanyProfile_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
                    table.CheckConstraint("CK_CompanyProfile_Singleton", "[SingletonKey] = 1");
                });

            migrationBuilder.CreateTable(
                name: "Contractors",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CountryCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    TaxId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    VatEuId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    KsefIdWew = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contractors", x => x.Id);
                    table.CheckConstraint("CK_Contractors_Code", "LEN(LTRIM(RTRIM([Code]))) > 0");
                    table.CheckConstraint("CK_Contractors_CountryCode", "LEN([CountryCode]) = 2 AND [CountryCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%'");
                    table.CheckConstraint("CK_Contractors_Dates", "[UpdatedAtUtc] >= [CreatedAtUtc]");
                    table.CheckConstraint("CK_Contractors_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
                    table.CheckConstraint("CK_Contractors_Parent", "[ParentId] IS NULL OR [ParentId] <> [Id]");
                    table.ForeignKey(
                        name: "FK_Contractors_Contractors_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "core",
                        principalTable: "Contractors",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ContractorAddresses",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractorId = table.Column<int>(type: "int", nullable: false),
                    AddressTypeId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Street = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BuildingNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ApartmentNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    City = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Region = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CountryCode = table.Column<string>(type: "char(2)", nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractorAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractorAddresses_AddressTypes_AddressTypeId",
                        column: x => x.AddressTypeId,
                        principalSchema: "ref",
                        principalTable: "AddressTypes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ContractorAddresses_Contractors_ContractorId",
                        column: x => x.ContractorId,
                        principalSchema: "core",
                        principalTable: "Contractors",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ContractorBankAccounts",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractorId = table.Column<int>(type: "int", nullable: false),
                    AccountNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Bic = table.Column<string>(type: "varchar(11)", nullable: true),
                    CurrencyCode = table.Column<string>(type: "char(3)", nullable: true),
                    AccountHolderName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractorBankAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractorBankAccounts_Contractors_ContractorId",
                        column: x => x.ContractorId,
                        principalSchema: "core",
                        principalTable: "Contractors",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ContractorChanges",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractorId = table.Column<int>(type: "int", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorIssuerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SnapshotVersion = table.Column<int>(type: "int", nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractorChanges", x => x.Id);
                    table.CheckConstraint("CK_ContractorChanges_Actor", "[ActorId] <> '00000000-0000-0000-0000-000000000000' AND [ActorIssuerId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_ContractorChanges_Json", "ISJSON([AfterJson]) = 1 AND ([BeforeJson] IS NULL OR ISJSON([BeforeJson]) = 1)");
                    table.ForeignKey(
                        name: "FK_ContractorChanges_Contractors_ContractorId",
                        column: x => x.ContractorId,
                        principalSchema: "core",
                        principalTable: "Contractors",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ContractorContacts",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractorId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Position = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Street = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BuildingNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ApartmentNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    City = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CountryCode = table.Column<string>(type: "char(2)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractorContacts", x => x.Id);
                    table.CheckConstraint("CK_ContractorContacts_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
                    table.ForeignKey(
                        name: "FK_ContractorContacts_Contractors_ContractorId",
                        column: x => x.ContractorId,
                        principalSchema: "core",
                        principalTable: "Contractors",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ContractorCreateOperations",
                schema: "core",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorIssuerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "varchar(64)", nullable: false),
                    ContractorId = table.Column<int>(type: "int", nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractorCreateOperations", x => new { x.ActorIssuerId, x.ActorId, x.OperationId });
                    table.ForeignKey(
                        name: "FK_ContractorCreateOperations_Contractors_ContractorId",
                        column: x => x.ContractorId,
                        principalSchema: "core",
                        principalTable: "Contractors",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                schema: "ref",
                table: "AddressTypes",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "Adres rejestrowy" },
                    { 2, "Adres korespondencyjny" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AddressTypes_Name",
                schema: "ref",
                table: "AddressTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractorAddresses_AddressTypeId",
                schema: "core",
                table: "ContractorAddresses",
                column: "AddressTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractorAddresses_ContractorId_AddressTypeId",
                schema: "core",
                table: "ContractorAddresses",
                columns: new[] { "ContractorId", "AddressTypeId" },
                unique: true,
                filter: "[IsDefault] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ContractorBankAccounts_ContractorId_CurrencyCode",
                schema: "core",
                table: "ContractorBankAccounts",
                columns: new[] { "ContractorId", "CurrencyCode" },
                unique: true,
                filter: "[IsDefault] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ContractorChanges_ContractorId_OccurredAtUtc",
                schema: "audit",
                table: "ContractorChanges",
                columns: new[] { "ContractorId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractorContacts_ContractorId",
                schema: "core",
                table: "ContractorContacts",
                column: "ContractorId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractorCreateOperations_ContractorId",
                schema: "core",
                table: "ContractorCreateOperations",
                column: "ContractorId");

            migrationBuilder.CreateIndex(
                name: "IX_Contractors_Code",
                schema: "core",
                table: "Contractors",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contractors_IsActive_Code",
                schema: "core",
                table: "Contractors",
                columns: new[] { "IsActive", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_Contractors_ParentId",
                schema: "core",
                table: "Contractors",
                column: "ParentId");

            migrationBuilder.Sql("""
                CREATE TRIGGER core.TR_Contractors_Hierarchy ON core.Contractors
                AFTER INSERT, UPDATE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF NOT UPDATE(ParentId) RETURN;
                    DECLARE @lockResult int;
                    EXEC @lockResult = sys.sp_getapplock @Resource=N'ContractorHierarchy',
                        @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
                    IF @lockResult < 0 THROW 51002, 'Hierarchy lock unavailable.', 1;
                    DECLARE @cycle bit = 0;
                    ;WITH Ancestors AS (
                        SELECT Id AS RootId, ParentId FROM inserted WHERE ParentId IS NOT NULL
                        UNION ALL
                        SELECT a.RootId, c.ParentId FROM Ancestors a
                        JOIN core.Contractors c ON c.Id = a.ParentId
                        WHERE a.ParentId <> a.RootId
                    )
                    SELECT TOP (1) @cycle = 1 FROM Ancestors WHERE ParentId = RootId
                    OPTION (MAXRECURSION 32767);
                    IF @cycle = 1 THROW 51001, 'Contractor hierarchy cycle.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyProfile",
                schema: "core");

            migrationBuilder.DropTable(
                name: "ContractorAddresses",
                schema: "core");

            migrationBuilder.DropTable(
                name: "ContractorBankAccounts",
                schema: "core");

            migrationBuilder.DropTable(
                name: "ContractorChanges",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "ContractorContacts",
                schema: "core");

            migrationBuilder.DropTable(
                name: "ContractorCreateOperations",
                schema: "core");

            migrationBuilder.DropTable(
                name: "AddressTypes",
                schema: "ref");

            migrationBuilder.DropTable(
                name: "Contractors",
                schema: "core");
        }
    }
}
