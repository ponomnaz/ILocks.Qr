using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "otp_codes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsUsed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_otp_codes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "qr_code_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckInAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CheckOutAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GuestsCount = table.Column<int>(type: "integer", nullable: false),
                    DoorPassword = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    QrImageBase64 = table.Column<string>(type: "text", nullable: false),
                    DataType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_qr_code_records", x => x.Id);
                    table.CheckConstraint("CK_qr_code_records_checkout_after_checkin", "\"CheckOutAt\" > \"CheckInAt\"");
                    table.CheckConstraint("CK_qr_code_records_guests_count_positive", "\"GuestsCount\" > 0");
                    table.ForeignKey(
                        name: "FK_qr_code_records_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "telegram_bindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChatId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_bindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_telegram_bindings_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_otp_codes_ExpiresAt",
                table: "otp_codes",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_otp_codes_PhoneNumber",
                table: "otp_codes",
                column: "PhoneNumber");

            migrationBuilder.CreateIndex(
                name: "IX_qr_code_records_CreatedAt",
                table: "qr_code_records",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_qr_code_records_UserId",
                table: "qr_code_records",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_telegram_bindings_ChatId",
                table: "telegram_bindings",
                column: "ChatId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_telegram_bindings_UserId",
                table: "telegram_bindings",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_PhoneNumber",
                table: "users",
                column: "PhoneNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "otp_codes");

            migrationBuilder.DropTable(
                name: "qr_code_records");

            migrationBuilder.DropTable(
                name: "telegram_bindings");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
