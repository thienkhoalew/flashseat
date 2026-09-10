using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FlashSeat.Payment.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddWebhookReceipt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "payment_webhook_receipts",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                EventKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                OrderCode = table.Column<long>(type: "bigint", nullable: false),
                ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payment_webhook_receipts", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_payment_webhook_receipts_EventKey",
            table: "payment_webhook_receipts",
            column: "EventKey",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "payment_webhook_receipts");
    }
}
