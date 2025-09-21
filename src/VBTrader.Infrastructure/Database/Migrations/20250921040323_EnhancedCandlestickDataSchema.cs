using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VBTrader.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class EnhancedCandlestickDataSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_CandlestickData_Timestamp",
                table: "candlestick_data",
                newName: "IX_CandlestickData_Timestamp_Legacy");

            migrationBuilder.RenameIndex(
                name: "IX_CandlestickData_Symbol_Timestamp",
                table: "candlestick_data",
                newName: "IX_CandlestickData_Symbol_Timestamp_Legacy");

            migrationBuilder.AlterColumn<decimal>(
                name: "BollingerUpper",
                table: "candlestick_data",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "BollingerMiddle",
                table: "candlestick_data",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "BollingerLower",
                table: "candlestick_data",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DataSource",
                table: "candlestick_data",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                defaultValue: "Schwab");

            migrationBuilder.AddColumn<string>(
                name: "DataType",
                table: "candlestick_data",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "ohlc");

            migrationBuilder.AddColumn<DateTime>(
                name: "FetchedAt",
                table: "candlestick_data",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "IsExtendedHours",
                table: "candlestick_data",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsRealTime",
                table: "candlestick_data",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "MarketTimestamp",
                table: "candlestick_data",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<long>(
                name: "MarketTimestampMs",
                table: "candlestick_data",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<decimal>(
                name: "PreviousClose",
                table: "candlestick_data",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TimeFrameSeconds",
                table: "candlestick_data",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeFrameType",
                table: "candlestick_data",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "minute");

            migrationBuilder.AddColumn<int>(
                name: "TimeFrameValue",
                table: "candlestick_data",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "candlestick_data",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CandlestickData_DataSource",
                table: "candlestick_data",
                column: "DataSource");

            migrationBuilder.CreateIndex(
                name: "IX_CandlestickData_DataType_IsRealTime",
                table: "candlestick_data",
                columns: new[] { "DataType", "IsRealTime" });

            migrationBuilder.CreateIndex(
                name: "IX_CandlestickData_FetchedAt",
                table: "candlestick_data",
                column: "FetchedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CandlestickData_MarketTimestamp",
                table: "candlestick_data",
                column: "MarketTimestamp");

            migrationBuilder.CreateIndex(
                name: "IX_CandlestickData_Symbol_MarketTimestamp",
                table: "candlestick_data",
                columns: new[] { "Symbol", "MarketTimestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_CandlestickData_Symbol_TimeFrame_MarketTimestamp",
                table: "candlestick_data",
                columns: new[] { "Symbol", "TimeFrameType", "TimeFrameValue", "MarketTimestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CandlestickData_DataSource",
                table: "candlestick_data");

            migrationBuilder.DropIndex(
                name: "IX_CandlestickData_DataType_IsRealTime",
                table: "candlestick_data");

            migrationBuilder.DropIndex(
                name: "IX_CandlestickData_FetchedAt",
                table: "candlestick_data");

            migrationBuilder.DropIndex(
                name: "IX_CandlestickData_MarketTimestamp",
                table: "candlestick_data");

            migrationBuilder.DropIndex(
                name: "IX_CandlestickData_Symbol_MarketTimestamp",
                table: "candlestick_data");

            migrationBuilder.DropIndex(
                name: "IX_CandlestickData_Symbol_TimeFrame_MarketTimestamp",
                table: "candlestick_data");

            migrationBuilder.DropColumn(
                name: "DataSource",
                table: "candlestick_data");

            migrationBuilder.DropColumn(
                name: "DataType",
                table: "candlestick_data");

            migrationBuilder.DropColumn(
                name: "FetchedAt",
                table: "candlestick_data");

            migrationBuilder.DropColumn(
                name: "IsExtendedHours",
                table: "candlestick_data");

            migrationBuilder.DropColumn(
                name: "IsRealTime",
                table: "candlestick_data");

            migrationBuilder.DropColumn(
                name: "MarketTimestamp",
                table: "candlestick_data");

            migrationBuilder.DropColumn(
                name: "MarketTimestampMs",
                table: "candlestick_data");

            migrationBuilder.DropColumn(
                name: "PreviousClose",
                table: "candlestick_data");

            migrationBuilder.DropColumn(
                name: "TimeFrameSeconds",
                table: "candlestick_data");

            migrationBuilder.DropColumn(
                name: "TimeFrameType",
                table: "candlestick_data");

            migrationBuilder.DropColumn(
                name: "TimeFrameValue",
                table: "candlestick_data");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "candlestick_data");

            migrationBuilder.RenameIndex(
                name: "IX_CandlestickData_Timestamp_Legacy",
                table: "candlestick_data",
                newName: "IX_CandlestickData_Timestamp");

            migrationBuilder.RenameIndex(
                name: "IX_CandlestickData_Symbol_Timestamp_Legacy",
                table: "candlestick_data",
                newName: "IX_CandlestickData_Symbol_Timestamp");

            migrationBuilder.AlterColumn<decimal>(
                name: "BollingerUpper",
                table: "candlestick_data",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "BollingerMiddle",
                table: "candlestick_data",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "BollingerLower",
                table: "candlestick_data",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldNullable: true);
        }
    }
}
