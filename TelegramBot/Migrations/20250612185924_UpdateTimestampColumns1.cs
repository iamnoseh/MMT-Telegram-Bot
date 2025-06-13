using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramBot.Migrations
{
    /// <inheritdoc />
    public partial class UpdateTimestampColumns1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndedAt",
                table: "DuelGames");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "DuelGames");

            migrationBuilder.AlterColumn<DateTime>(
                name: "FinishedAt",
                table: "DuelGames",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<int>(
                name: "CurrentRound",
                table: "DuelGames",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentRound",
                table: "DuelGames");

            migrationBuilder.AlterColumn<DateTime>(
                name: "FinishedAt",
                table: "DuelGames",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EndedAt",
                table: "DuelGames",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "DuelGames",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
