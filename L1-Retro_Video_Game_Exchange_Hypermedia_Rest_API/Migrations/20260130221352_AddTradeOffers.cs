using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Migrations
{
    /// <inheritdoc />
    public partial class AddTradeOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TradeOffers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequestedGameId = table.Column<int>(type: "INTEGER", nullable: false),
                    OfferedGameId = table.Column<int>(type: "INTEGER", nullable: false),
                    RequesterUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnerUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeOffers_Games_OfferedGameId",
                        column: x => x.OfferedGameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeOffers_Games_RequestedGameId",
                        column: x => x.RequestedGameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeOffers_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeOffers_Users_RequesterUserId",
                        column: x => x.RequesterUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TradeOffers_OfferedGameId",
                table: "TradeOffers",
                column: "OfferedGameId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeOffers_OwnerUserId",
                table: "TradeOffers",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeOffers_RequestedGameId",
                table: "TradeOffers",
                column: "RequestedGameId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeOffers_RequesterUserId",
                table: "TradeOffers",
                column: "RequesterUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TradeOffers");
        }
    }
}
