using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaidMentorshipTiersAndMentorEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "CounsellorId",
                schema: "mirage",
                table: "payments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "CounsellingSessionId",
                schema: "mirage",
                table: "payments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "MentorProfileId",
                schema: "mirage",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MentorRequestId",
                schema: "mirage",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "OrganisationId",
                schema: "mirage",
                table: "org_events",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            // MentorAudience.Everyone; ignored on a church event.
            migrationBuilder.AddColumn<int>(
                name: "Audience",
                schema: "mirage",
                table: "org_events",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "MentorProfileId",
                schema: "mirage",
                table: "org_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankAccountName",
                schema: "mirage",
                table: "mentors",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankAccountNumber",
                schema: "mirage",
                table: "mentors",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankCode",
                schema: "mirage",
                table: "mentors",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankName",
                schema: "mirage",
                table: "mentors",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FlutterwaveSubaccountId",
                schema: "mirage",
                table: "mentors",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OffersPaidMentorship",
                schema: "mirage",
                table: "mentors",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PaystackSubaccountCode",
                schema: "mirage",
                table: "mentors",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaystackTransferRecipientCode",
                schema: "mirage",
                table: "mentors",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PriceAmount",
                schema: "mirage",
                table: "mentors",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PriceCurrency",
                schema: "mirage",
                table: "mentors",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PaidAt",
                schema: "mirage",
                table: "mentor_requests",
                type: "timestamp with time zone",
                nullable: true);

            // MentorshipTier.Free — every mentee that predates paid mentorship is a free one.
            migrationBuilder.AddColumn<int>(
                name: "Tier",
                schema: "mirage",
                table: "mentor_requests",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // MentorAudience.Everyone — existing posts were addressed to the whole practice.
            migrationBuilder.AddColumn<int>(
                name: "Audience",
                schema: "mirage",
                table: "mentor_posts",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // MentorAudience.Everyone.
            migrationBuilder.AddColumn<int>(
                name: "Audience",
                schema: "mirage",
                table: "mentor_meetings",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // MentorAudience.Everyone.
            migrationBuilder.AddColumn<int>(
                name: "Audience",
                schema: "mirage",
                table: "mentor_group_messages",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_payments_MentorProfileId",
                schema: "mirage",
                table: "payments",
                column: "MentorProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_payments_MentorRequestId",
                schema: "mirage",
                table: "payments",
                column: "MentorRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_org_events_MentorProfileId",
                schema: "mirage",
                table: "org_events",
                column: "MentorProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_mentor_requests_MentorProfileId_Tier_Status",
                schema: "mirage",
                table: "mentor_requests",
                columns: new[] { "MentorProfileId", "Tier", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_org_events_mentors_MentorProfileId",
                schema: "mirage",
                table: "org_events",
                column: "MentorProfileId",
                principalSchema: "mirage",
                principalTable: "mentors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_payments_mentor_requests_MentorRequestId",
                schema: "mirage",
                table: "payments",
                column: "MentorRequestId",
                principalSchema: "mirage",
                principalTable: "mentor_requests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_payments_mentors_MentorProfileId",
                schema: "mirage",
                table: "payments",
                column: "MentorProfileId",
                principalSchema: "mirage",
                principalTable: "mentors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_org_events_mentors_MentorProfileId",
                schema: "mirage",
                table: "org_events");

            migrationBuilder.DropForeignKey(
                name: "FK_payments_mentor_requests_MentorRequestId",
                schema: "mirage",
                table: "payments");

            migrationBuilder.DropForeignKey(
                name: "FK_payments_mentors_MentorProfileId",
                schema: "mirage",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_payments_MentorProfileId",
                schema: "mirage",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_payments_MentorRequestId",
                schema: "mirage",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_org_events_MentorProfileId",
                schema: "mirage",
                table: "org_events");

            migrationBuilder.DropIndex(
                name: "IX_mentor_requests_MentorProfileId_Tier_Status",
                schema: "mirage",
                table: "mentor_requests");

            migrationBuilder.DropColumn(
                name: "MentorProfileId",
                schema: "mirage",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "MentorRequestId",
                schema: "mirage",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "Audience",
                schema: "mirage",
                table: "org_events");

            migrationBuilder.DropColumn(
                name: "MentorProfileId",
                schema: "mirage",
                table: "org_events");

            migrationBuilder.DropColumn(
                name: "BankAccountName",
                schema: "mirage",
                table: "mentors");

            migrationBuilder.DropColumn(
                name: "BankAccountNumber",
                schema: "mirage",
                table: "mentors");

            migrationBuilder.DropColumn(
                name: "BankCode",
                schema: "mirage",
                table: "mentors");

            migrationBuilder.DropColumn(
                name: "BankName",
                schema: "mirage",
                table: "mentors");

            migrationBuilder.DropColumn(
                name: "FlutterwaveSubaccountId",
                schema: "mirage",
                table: "mentors");

            migrationBuilder.DropColumn(
                name: "OffersPaidMentorship",
                schema: "mirage",
                table: "mentors");

            migrationBuilder.DropColumn(
                name: "PaystackSubaccountCode",
                schema: "mirage",
                table: "mentors");

            migrationBuilder.DropColumn(
                name: "PaystackTransferRecipientCode",
                schema: "mirage",
                table: "mentors");

            migrationBuilder.DropColumn(
                name: "PriceAmount",
                schema: "mirage",
                table: "mentors");

            migrationBuilder.DropColumn(
                name: "PriceCurrency",
                schema: "mirage",
                table: "mentors");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                schema: "mirage",
                table: "mentor_requests");

            migrationBuilder.DropColumn(
                name: "Tier",
                schema: "mirage",
                table: "mentor_requests");

            migrationBuilder.DropColumn(
                name: "Audience",
                schema: "mirage",
                table: "mentor_posts");

            migrationBuilder.DropColumn(
                name: "Audience",
                schema: "mirage",
                table: "mentor_meetings");

            migrationBuilder.DropColumn(
                name: "Audience",
                schema: "mirage",
                table: "mentor_group_messages");

            migrationBuilder.AlterColumn<Guid>(
                name: "CounsellorId",
                schema: "mirage",
                table: "payments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CounsellingSessionId",
                schema: "mirage",
                table: "payments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "OrganisationId",
                schema: "mirage",
                table: "org_events",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
