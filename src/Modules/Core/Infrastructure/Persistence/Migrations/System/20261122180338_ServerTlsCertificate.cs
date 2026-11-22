using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.System
{
    /// <inheritdoc />
    public partial class ServerTlsCertificate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServerTlsChanges",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    AtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    AfterJson = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerTlsChanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServerTlsConfiguration",
                schema: "security",
                columns: table => new
                {
                    SingletonKey = table.Column<byte>(type: "tinyint", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    DnsNames = table.Column<string>(type: "nvarchar(max)", maxLength: 6000, nullable: false),
                    IpAddresses = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false),
                    IssuerThumbprint = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    CaGeneration = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerTlsConfiguration", x => x.SingletonKey);
                    table.CheckConstraint("CK_ServerTlsConfiguration_Generation", "[CaGeneration] >= 1");
                    table.CheckConstraint("CK_ServerTlsConfiguration_Mode", "[Mode] IN (N'InstallationCa', N'OwnCertificate')");
                    table.CheckConstraint("CK_ServerTlsConfiguration_Singleton", "[SingletonKey] = 1");
                });

            migrationBuilder.CreateTable(
                name: "ServerTlsState",
                schema: "security",
                columns: table => new
                {
                    SingletonKey = table.Column<byte>(type: "tinyint", nullable: false),
                    CaGeneration = table.Column<int>(type: "int", nullable: false),
                    CaSpkiSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true),
                    CaThumbprint = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    CaNotAfter = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CaCertificate = table.Column<byte[]>(type: "varbinary(max)", maxLength: 8192, nullable: true),
                    ServerCertificateThumbprint = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    ServerCertificateNotAfter = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ServerCertificateNames = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    LastErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LastErrorAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerTlsState", x => x.SingletonKey);
                    table.CheckConstraint("CK_ServerTlsState_Singleton", "[SingletonKey] = 1");
                });

            migrationBuilder.InsertData(
                schema: "security",
                table: "ServerTlsConfiguration",
                columns: new[] { "SingletonKey", "CaGeneration", "DnsNames", "IpAddresses", "IssuerThumbprint", "Mode" },
                values: new object[] { (byte)1, 1, "", "", null, "InstallationCa" });

            migrationBuilder.CreateIndex(
                name: "IX_ServerTlsChanges_AtUtc",
                schema: "audit",
                table: "ServerTlsChanges",
                column: "AtUtc");

            // ADR-047: niezmienna historia zmian certyfikatu serwera (bez kluczy i haseł).
            migrationBuilder.Sql("CREATE TRIGGER [audit].[TR_ServerTlsChanges_Immutable] ON [audit].[ServerTlsChanges] INSTEAD OF UPDATE, DELETE AS THROW 51000, 'Server TLS history is immutable', 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServerTlsChanges",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "ServerTlsConfiguration",
                schema: "security");

            migrationBuilder.DropTable(
                name: "ServerTlsState",
                schema: "security");
        }
    }
}
