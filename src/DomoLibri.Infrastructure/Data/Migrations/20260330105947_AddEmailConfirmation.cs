using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DomoLibri.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmailConfirmado",
                table: "UsuariosEditora",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiracaoToken",
                table: "UsuariosEditora",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TokenConfirmacao",
                table: "UsuariosEditora",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailConfirmado",
                table: "UsuariosEditora");

            migrationBuilder.DropColumn(
                name: "ExpiracaoToken",
                table: "UsuariosEditora");

            migrationBuilder.DropColumn(
                name: "TokenConfirmacao",
                table: "UsuariosEditora");
        }
    }
}
