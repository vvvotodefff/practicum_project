using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectWork.Events.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessedBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "processed_bookings",
                columns: table => new
                {
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_bookings", x => x.booking_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processed_bookings");
        }
    }
}
