using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlashSeat.Events.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEventArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_events_Slug",
                table: "events");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_events_Slug",
                table: "events",
                column: "Slug",
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_events_Status_EndsAt",
                table: "events",
                columns: ["Status", "EndsAt"]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_events_Slug",
                table: "events");

            migrationBuilder.DropIndex(
                name: "IX_events_Status_EndsAt",
                table: "events");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "events");

            migrationBuilder.CreateIndex(
                name: "IX_events_Slug",
                table: "events",
                column: "Slug",
                unique: true);
        }
    }
}
