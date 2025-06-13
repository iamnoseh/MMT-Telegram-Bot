using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TelegramBot.Migrations
{
    /// <inheritdoc />
    public partial class fix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DuelGames",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Player1ChatId = table.Column<long>(type: "bigint", nullable: false),
                    Player2ChatId = table.Column<long>(type: "bigint", nullable: false),
                    SubjectId = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Player1Score = table.Column<int>(type: "integer", nullable: false),
                    Player2Score = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsFinished = table.Column<bool>(type: "boolean", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DuelGames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DuelGames_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuestionsToAdmin",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserChatId = table.Column<long>(type: "bigint", nullable: false),
                    QuestionText = table.Column<string>(type: "text", nullable: false),
                    Answer = table.Column<string>(type: "text", nullable: true),
                    IsAnswered = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionsToAdmin", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserReferrals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReferrerChatId = table.Column<long>(type: "bigint", nullable: false),
                    ReferredChatId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsRewarded = table.Column<bool>(type: "boolean", nullable: false),
                    ReferrerId = table.Column<int>(type: "integer", nullable: false),
                    ReferredId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserReferrals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserReferrals_Users_ReferredId",
                        column: x => x.ReferredId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserReferrals_Users_ReferrerId",
                        column: x => x.ReferrerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DuelGames_SubjectId",
                table: "DuelGames",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_UserReferrals_ReferredId",
                table: "UserReferrals",
                column: "ReferredId");

            migrationBuilder.CreateIndex(
                name: "IX_UserReferrals_ReferrerId",
                table: "UserReferrals",
                column: "ReferrerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DuelGames");

            migrationBuilder.DropTable(
                name: "QuestionsToAdmin");

            migrationBuilder.DropTable(
                name: "UserReferrals");
        }
    }
}
