using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class Mode101 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BoardTiles_GameId_Index",
                table: "BoardTiles");

            migrationBuilder.DropColumn(
                name: "DrawnTileLeft",
                table: "GameMoves");

            migrationBuilder.DropColumn(
                name: "DrawnTileRight",
                table: "GameMoves");

            migrationBuilder.RenameColumn(
                name: "ConsecutivePasses",
                table: "Games",
                newName: "Team2Score");

            migrationBuilder.RenameColumn(
                name: "Score",
                table: "GameParticipants",
                newName: "TotalScore");

            migrationBuilder.RenameColumn(
                name: "HasPassed",
                table: "GameParticipants",
                newName: "HasVotedToStart");

            migrationBuilder.RenameColumn(
                name: "ScoreGained",
                table: "GameMoves",
                newName: "PointsGained");

            migrationBuilder.AddColumn<Guid>(
                name: "DeferredFromTieRoundWinnerId",
                table: "Games",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeferredFromTieRoundWinningTeam",
                table: "Games",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeferredPoints",
                table: "Games",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsTeamGame",
                table: "Games",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RoundHistoryJson",
                table: "Games",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "RoundNumber",
                table: "Games",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "RoundStarterId",
                table: "Games",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Team1Score",
                table: "Games",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "VotesJson",
                table: "Games",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "WinningTeam",
                table: "Games",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "GameParticipants",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ConsecutiveLowWins",
                table: "GameParticipants",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RoundScore",
                table: "GameParticipants",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Team",
                table: "GameParticipants",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DrawnTilesJson",
                table: "GameMoves",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RoundNumber",
                table: "GameMoves",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PlayedByColor",
                table: "BoardTiles",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlayedByPosition",
                table: "BoardTiles",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RoundNumber",
                table: "BoardTiles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_BoardTiles_GameId_RoundNumber_Index",
                table: "BoardTiles",
                columns: new[] { "GameId", "RoundNumber", "Index" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BoardTiles_GameId_RoundNumber_Index",
                table: "BoardTiles");

            migrationBuilder.DropColumn(
                name: "DeferredFromTieRoundWinnerId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "DeferredFromTieRoundWinningTeam",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "DeferredPoints",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "IsTeamGame",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "RoundHistoryJson",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "RoundNumber",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "RoundStarterId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "Team1Score",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "VotesJson",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "WinningTeam",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "Color",
                table: "GameParticipants");

            migrationBuilder.DropColumn(
                name: "ConsecutiveLowWins",
                table: "GameParticipants");

            migrationBuilder.DropColumn(
                name: "RoundScore",
                table: "GameParticipants");

            migrationBuilder.DropColumn(
                name: "Team",
                table: "GameParticipants");

            migrationBuilder.DropColumn(
                name: "DrawnTilesJson",
                table: "GameMoves");

            migrationBuilder.DropColumn(
                name: "RoundNumber",
                table: "GameMoves");

            migrationBuilder.DropColumn(
                name: "PlayedByColor",
                table: "BoardTiles");

            migrationBuilder.DropColumn(
                name: "PlayedByPosition",
                table: "BoardTiles");

            migrationBuilder.DropColumn(
                name: "RoundNumber",
                table: "BoardTiles");

            migrationBuilder.RenameColumn(
                name: "Team2Score",
                table: "Games",
                newName: "ConsecutivePasses");

            migrationBuilder.RenameColumn(
                name: "TotalScore",
                table: "GameParticipants",
                newName: "Score");

            migrationBuilder.RenameColumn(
                name: "HasVotedToStart",
                table: "GameParticipants",
                newName: "HasPassed");

            migrationBuilder.RenameColumn(
                name: "PointsGained",
                table: "GameMoves",
                newName: "ScoreGained");

            migrationBuilder.AddColumn<int>(
                name: "DrawnTileLeft",
                table: "GameMoves",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DrawnTileRight",
                table: "GameMoves",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BoardTiles_GameId_Index",
                table: "BoardTiles",
                columns: new[] { "GameId", "Index" },
                unique: true);
        }
    }
}
