using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCelebrationEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "celebration_entries",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Body = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: false),
                    EmailSentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PartnerEmailSentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_celebration_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_celebration_entries_AspNetUsers_PartnerUserId",
                        column: x => x.PartnerUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_celebration_entries_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_celebration_entries_CreatedAt",
                schema: "mirage",
                table: "celebration_entries",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_celebration_entries_PartnerUserId",
                schema: "mirage",
                table: "celebration_entries",
                column: "PartnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_celebration_entries_Type_Year_UserId",
                schema: "mirage",
                table: "celebration_entries",
                columns: new[] { "Type", "Year", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_celebration_entries_UserId",
                schema: "mirage",
                table: "celebration_entries",
                column: "UserId");

            // Move the celebration posts that used to live as Testimonial rows into the new table
            // (preserving id/dates; EmailSentAt is stamped so migrated entries never re-email),
            // then remove them and their dependents from the testimonials feed.
            migrationBuilder.Sql("""
                INSERT INTO mirage.celebration_entries
                    ("Id", "UserId", "PartnerUserId", "Type", "Year", "Title", "Body",
                     "EmailSentAt", "PartnerEmailSentAt", "CreatedAt", "UpdatedAt")
                SELECT t."Id", t."TaggedUserId", NULL, t."CelebrationType",
                       EXTRACT(YEAR FROM t."CreatedAt")::int, t."Title", t."Body",
                       t."CreatedAt", NULL, t."CreatedAt", t."UpdatedAt"
                FROM mirage.testimonials t
                WHERE t."CelebrationType" IS NOT NULL AND t."TaggedUserId" IS NOT NULL;

                DELETE FROM mirage.testimonial_comment_likes cl
                USING mirage.testimonial_comments c, mirage.testimonials t
                WHERE cl."CommentId" = c."Id" AND c."TestimonialId" = t."Id"
                  AND t."CelebrationType" IS NOT NULL;

                DELETE FROM mirage.testimonial_comments c
                USING mirage.testimonials t
                WHERE c."TestimonialId" = t."Id" AND t."CelebrationType" IS NOT NULL
                  AND c."ParentCommentId" IS NOT NULL;

                DELETE FROM mirage.testimonial_comments c
                USING mirage.testimonials t
                WHERE c."TestimonialId" = t."Id" AND t."CelebrationType" IS NOT NULL;

                DELETE FROM mirage.testimonial_likes l
                USING mirage.testimonials t
                WHERE l."TestimonialId" = t."Id" AND t."CelebrationType" IS NOT NULL;

                DELETE FROM mirage.testimonial_reads r
                USING mirage.testimonials t
                WHERE r."TestimonialId" = t."Id" AND t."CelebrationType" IS NOT NULL;

                DELETE FROM mirage.testimonials WHERE "CelebrationType" IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "CelebrationType",
                schema: "mirage",
                table: "testimonials");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "celebration_entries",
                schema: "mirage");

            migrationBuilder.AddColumn<int>(
                name: "CelebrationType",
                schema: "mirage",
                table: "testimonials",
                type: "integer",
                nullable: true);
        }
    }
}
