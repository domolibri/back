using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DomoLibri.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUsuarioCadastro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_VinculosUsuarioEditora_EditoraId_UsuarioId",
                table: "VinculosUsuarioEditora",
                columns: new[] { "EditoraId", "UsuarioId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VinculosUsuarioEditora_EditoraId_UsuarioId",
                table: "VinculosUsuarioEditora");
        }
    }
}
