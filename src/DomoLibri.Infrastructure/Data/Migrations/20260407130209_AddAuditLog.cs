using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DomoLibri.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EditoraId = table.Column<Guid>(type: "uuid", nullable: true),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    Acao = table.Column<string>(type: "text", nullable: false),
                    Recurso = table.Column<string>(type: "text", nullable: false),
                    RecursoId = table.Column<string>(type: "text", nullable: false),
                    IP = table.Column<string>(type: "text", nullable: false),
                    UserAgent = table.Column<string>(type: "text", nullable: false),
                    DataHora = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DadosOriginais = table.Column<string>(type: "text", nullable: true),
                    DadosNovos = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_DataHora",
                table: "AuditLogs",
                column: "DataHora");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EditoraId",
                table: "AuditLogs",
                column: "EditoraId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EditoraId_DataHora",
                table: "AuditLogs",
                columns: new[] { "EditoraId", "DataHora" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");
        }
    }
}
