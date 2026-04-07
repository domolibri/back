using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DomoLibri.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLGPDConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsentimentosLGPD",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EditoraId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    TipoConsentimento = table.Column<string>(type: "text", nullable: false),
                    VersaoTermo = table.Column<string>(type: "text", nullable: false),
                    DataConsentimento = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsentimentosLGPD", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsentimentosLGPD_Editoras_EditoraId",
                        column: x => x.EditoraId,
                        principalTable: "Editoras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConsentimentosLGPD_UsuariosEditora_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "UsuariosEditora",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsentimentosLGPD_EditoraId",
                table: "ConsentimentosLGPD",
                column: "EditoraId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsentimentosLGPD_UsuarioId",
                table: "ConsentimentosLGPD",
                column: "UsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsentimentosLGPD");
        }
    }
}
