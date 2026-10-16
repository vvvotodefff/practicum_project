using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectWork.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUsersAndBookingOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "bookings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    login = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bookings_user_id_status",
                table: "bookings",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_users_login",
                table: "users",
                column: "login",
                unique: true);

            // У существующих броней ещё нет владельца. Сохраняем данные и назначаем
            // технического пользователя со случайным хешем без известного пароля.
            var legacyHash = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            migrationBuilder.Sql($"""
                INSERT INTO users (id, login, password_hash, role)
                SELECT '00000000-0000-0000-0000-000000000801'::uuid,
                       '__legacy_bookings_000000000801', '{legacyHash}', 'User'
                WHERE EXISTS (SELECT 1 FROM bookings);
                UPDATE bookings SET user_id = '00000000-0000-0000-0000-000000000801'::uuid
                WHERE user_id = '00000000-0000-0000-0000-000000000000'::uuid;
                ALTER TABLE bookings ALTER COLUMN user_id DROP DEFAULT;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_bookings_users_user_id",
                table: "bookings",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_bookings_users_user_id",
                table: "bookings");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropIndex(
                name: "IX_bookings_user_id_status",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "bookings");
        }
    }
}
