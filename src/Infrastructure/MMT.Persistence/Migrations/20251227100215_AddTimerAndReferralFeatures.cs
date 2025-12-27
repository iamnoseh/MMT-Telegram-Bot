using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMT.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTimerAndReferralFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "QuizPoints",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReferralPoints",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "QuestionStartTime",
                table: "TestSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TimeExpired",
                table: "TestSessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasTimer",
                table: "Subjects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TimerSeconds",
                table: "Subjects",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ChallengerScore",
                table: "Duels",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OpponentScore",
                table: "Duels",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "QuestionCount",
                table: "Duels",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "AnswerTime",
                table: "DuelAnswers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Points",
                table: "DuelAnswers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "TimeTaken",
                table: "DuelAnswers",
                type: "interval",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QuizPoints",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ReferralPoints",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "QuestionStartTime",
                table: "TestSessions");

            migrationBuilder.DropColumn(
                name: "TimeExpired",
                table: "TestSessions");

            migrationBuilder.DropColumn(
                name: "HasTimer",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "TimerSeconds",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "ChallengerScore",
                table: "Duels");

            migrationBuilder.DropColumn(
                name: "OpponentScore",
                table: "Duels");

            migrationBuilder.DropColumn(
                name: "QuestionCount",
                table: "Duels");

            migrationBuilder.DropColumn(
                name: "AnswerTime",
                table: "DuelAnswers");

            migrationBuilder.DropColumn(
                name: "Points",
                table: "DuelAnswers");

            migrationBuilder.DropColumn(
                name: "TimeTaken",
                table: "DuelAnswers");
        }
    }
}
