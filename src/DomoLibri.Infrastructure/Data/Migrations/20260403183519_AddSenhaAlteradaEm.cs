using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DomoLibri.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSenhaAlteradaEm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SenhaAlteradaEm",
                table: "UsuariosEditora",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SenhaAlteradaEm",
                table: "UsuariosEditora");
        }
    }
}
