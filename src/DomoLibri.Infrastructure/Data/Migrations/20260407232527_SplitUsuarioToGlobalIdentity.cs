using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DomoLibri.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SplitUsuarioToGlobalIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ConsentimentosLGPD_UsuariosEditora_UsuarioId",
                table: "ConsentimentosLGPD");

            migrationBuilder.DropForeignKey(
                name: "FK_Convites_UsuariosEditora_ConvidadoPorUsuarioId",
                table: "Convites");

            migrationBuilder.DropTable(
                name: "UsuarioRoles");

            migrationBuilder.DropTable(
                name: "UsuariosEditora");

            migrationBuilder.CreateTable(
                name: "Usuarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    SenhaHash = table.Column<string>(type: "text", nullable: false),
                    Nome = table.Column<string>(type: "text", nullable: false),
                    EmailConfirmado = table.Column<bool>(type: "boolean", nullable: false),
                    TokenConfirmacao = table.Column<string>(type: "text", nullable: true),
                    ExpiracaoToken = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TokenRedefinicaoSenha = table.Column<string>(type: "text", nullable: true),
                    ExpiracaoTokenRedefinicaoSenha = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SenhaAlteradaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcessosFalhos = table.Column<int>(type: "integer", nullable: false),
                    BloqueioAte = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VinculosUsuarioEditora",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EditoraId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    DataEntrada = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TipoVinculo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VinculosUsuarioEditora", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VinculosUsuarioEditora_Editoras_EditoraId",
                        column: x => x.EditoraId,
                        principalTable: "Editoras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VinculosUsuarioEditora_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VinculoRoles",
                columns: table => new
                {
                    RolesId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuariosId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VinculoRoles", x => new { x.RolesId, x.UsuariosId });
                    table.ForeignKey(
                        name: "FK_VinculoRoles_Roles_RolesId",
                        column: x => x.RolesId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VinculoRoles_VinculosUsuarioEditora_UsuariosId",
                        column: x => x.UsuariosId,
                        principalTable: "VinculosUsuarioEditora",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_Email",
                table: "Usuarios",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VinculoRoles_UsuariosId",
                table: "VinculoRoles",
                column: "UsuariosId");

            migrationBuilder.CreateIndex(
                name: "IX_VinculosUsuarioEditora_EditoraId",
                table: "VinculosUsuarioEditora",
                column: "EditoraId");

            migrationBuilder.CreateIndex(
                name: "IX_VinculosUsuarioEditora_UsuarioId",
                table: "VinculosUsuarioEditora",
                column: "UsuarioId");

            migrationBuilder.AddForeignKey(
                name: "FK_ConsentimentosLGPD_VinculosUsuarioEditora_UsuarioId",
                table: "ConsentimentosLGPD",
                column: "UsuarioId",
                principalTable: "VinculosUsuarioEditora",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Convites_VinculosUsuarioEditora_ConvidadoPorUsuarioId",
                table: "Convites",
                column: "ConvidadoPorUsuarioId",
                principalTable: "VinculosUsuarioEditora",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ConsentimentosLGPD_VinculosUsuarioEditora_UsuarioId",
                table: "ConsentimentosLGPD");

            migrationBuilder.DropForeignKey(
                name: "FK_Convites_VinculosUsuarioEditora_ConvidadoPorUsuarioId",
                table: "Convites");

            migrationBuilder.DropTable(
                name: "VinculoRoles");

            migrationBuilder.DropTable(
                name: "VinculosUsuarioEditora");

            migrationBuilder.DropTable(
                name: "Usuarios");

            migrationBuilder.CreateTable(
                name: "UsuariosEditora",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EditoraId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcessosFalhos = table.Column<int>(type: "integer", nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    BloqueioAte = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: false),
                    EmailConfirmado = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiracaoToken = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiracaoTokenRedefinicaoSenha = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Nome = table.Column<string>(type: "text", nullable: false),
                    SenhaAlteradaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SenhaHash = table.Column<string>(type: "text", nullable: false),
                    TokenConfirmacao = table.Column<string>(type: "text", nullable: true),
                    TokenRedefinicaoSenha = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsuariosEditora", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UsuariosEditora_Editoras_EditoraId",
                        column: x => x.EditoraId,
                        principalTable: "Editoras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UsuarioRoles",
                columns: table => new
                {
                    RolesId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuariosId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsuarioRoles", x => new { x.RolesId, x.UsuariosId });
                    table.ForeignKey(
                        name: "FK_UsuarioRoles_Roles_RolesId",
                        column: x => x.RolesId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UsuarioRoles_UsuariosEditora_UsuariosId",
                        column: x => x.UsuariosId,
                        principalTable: "UsuariosEditora",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsuarioRoles_UsuariosId",
                table: "UsuarioRoles",
                column: "UsuariosId");

            migrationBuilder.CreateIndex(
                name: "IX_UsuariosEditora_EditoraId",
                table: "UsuariosEditora",
                column: "EditoraId");

            migrationBuilder.CreateIndex(
                name: "IX_UsuariosEditora_Email_EditoraId",
                table: "UsuariosEditora",
                columns: new[] { "Email", "EditoraId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ConsentimentosLGPD_UsuariosEditora_UsuarioId",
                table: "ConsentimentosLGPD",
                column: "UsuarioId",
                principalTable: "UsuariosEditora",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Convites_UsuariosEditora_ConvidadoPorUsuarioId",
                table: "Convites",
                column: "ConvidadoPorUsuarioId",
                principalTable: "UsuariosEditora",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
