using ClosedXML.Excel;
using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace POS.Services.Service;

public class ReportService : IReportService
{
    private readonly IUnitOfWork _unitOfWork;

    private const string BrandColor = "#16A34A";
    private const string CancelledRowColor = "#FEF2F2";

    public ReportService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    #region Public API Methods

    /// <summary>
    /// Gets report summary data for a date range.
    /// Uses SQL-level aggregation via repository projections — no .Include() or entity tracking.
    /// </summary>
    public async Task<ReportSummary> GetSummaryAsync(int branchId, DateOnly from, DateOnly to)
    {
        var (startUtc, endUtc) = await ResolveUtcRangeAsync(branchId, from, to);

        var dailyMetrics = await _unitOfWork.Orders.GetDailyMetricsAsync(branchId, startUtc, endUtc);
        var paymentTotals = await _unitOfWork.Orders.GetPaymentTotalsAsync(branchId, startUtc, endUtc);
        var topProducts = await _unitOfWork.Orders.GetTopProductsAsync(branchId, startUtc, endUtc);
        var orderRows = await _unitOfWork.Orders.GetFlatOrderRowsAsync(branchId, startUtc, endUtc);

        var completedMetrics = dailyMetrics.Where(m => !m.IsCancelled).ToList();
        var cancelledMetrics = dailyMetrics.Where(m => m.IsCancelled).ToList();

        var totalOrders = dailyMetrics.Sum(m => m.OrderCount);
        var completedOrders = completedMetrics.Sum(m => m.OrderCount);
        var cancelledOrders = cancelledMetrics.Sum(m => m.OrderCount);
        var totalCents = completedMetrics.Sum(m => m.TotalCents);
        var discountCents = completedMetrics.Sum(m => m.DiscountCents);
        var averageTicket = completedOrders > 0 ? (decimal)totalCents / completedOrders : 0;

        var cashCents = paymentTotals
            .Where(p => p.Method == PaymentMethod.Cash)
            .Sum(p => p.TotalCents);
        var cardCents = paymentTotals
            .Where(p => p.Method == PaymentMethod.Card)
            .Sum(p => p.TotalCents);

        var dailySummaries = dailyMetrics
            .GroupBy(m => m.Date)
            .OrderBy(g => g.Key)
            .Select(g => new DailySummary
            {
                Date = g.Key,
                OrderCount = g.Where(m => !m.IsCancelled).Sum(m => m.OrderCount),
                TotalCents = g.Where(m => !m.IsCancelled).Sum(m => m.TotalCents),
                CancelledCount = g.Where(m => m.IsCancelled).Sum(m => m.OrderCount)
            }).ToList();

        return new ReportSummary
        {
            From = from.ToDateTime(TimeOnly.MinValue),
            To = to.ToDateTime(TimeOnly.MinValue),
            TotalOrders = totalOrders,
            CancelledOrders = cancelledOrders,
            CompletedOrders = completedOrders,
            TotalCents = totalCents,
            CashCents = cashCents,
            CardCents = cardCents,
            TotalDiscountCents = discountCents,
            AverageTicketCents = averageTicket,
            DailySummaries = dailySummaries,
            TopProducts = topProducts,
            Orders = orderRows
        };
    }

    /// <summary>
    /// Generates Excel report as byte array.
    /// </summary>
    public async Task<byte[]> GenerateExcelAsync(int branchId, DateOnly from, DateOnly to)
    {
        var summary = await GetSummaryAsync(branchId, from, to);

        using var workbook = new XLWorkbook();

        BuildSummarySheet(workbook, summary);
        BuildDailySheet(workbook, summary);
        BuildOrdersSheet(workbook, summary);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Generates PDF report as byte array.
    /// </summary>
    public async Task<byte[]> GeneratePdfAsync(int branchId, DateOnly from, DateOnly to)
    {
        var summary = await GetSummaryAsync(branchId, from, to);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Element(c => BuildPdfHeader(c, summary));
                page.Content().Element(c => BuildPdfContent(c, summary));
                page.Footer().Element(BuildPdfFooter);
            });
        });

        return document.GeneratePdf();
    }

    /// <summary>
    /// Generates a fiscal CSV export with invoice status for each order.
    /// Uses SQL-level projection via repository — no .Include() or entity tracking.
    /// </summary>
    public async Task<byte[]> GenerateFiscalCsvAsync(int branchId, DateOnly from, DateOnly to)
    {
        var (startUtc, endUtc) = await ResolveUtcRangeAsync(branchId, from, to);
        var rows = await _unitOfWork.Orders.GetFlatOrdersForCsvAsync(branchId, startUtc, endUtc);

        using var stream = new MemoryStream();
        using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(true));

        // Header
        await writer.WriteLineAsync("OrderId,Date,Total,PaymentMethod,InvoiceStatus");

        foreach (var row in rows)
        {
            var paymentMethods = string.Join("|",
                row.PaymentMethods.Select(m => m.ToString()).Distinct());
            var total = (row.TotalCents / 100m).ToString("F2");
            var date = row.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
            var status = row.InvoiceStatus.ToString();

            await writer.WriteLineAsync(
                $"{row.Id},{date},{total},{paymentMethods},{status}");
        }

        await writer.FlushAsync();
        return stream.ToArray();
    }

    #endregion

    #region BDD-006: Advanced BI Methods

    /// <summary>
    /// Returns aggregated chart data (sales over time, top products, payment breakdown)
    /// for the BI dashboard. Delegates to repository projection methods.
    /// Only paid, non-cancelled orders are included.
    /// </summary>
    public async Task<DashboardChartsDto> GetDashboardChartsAsync(
        int branchId, DateOnly from, DateOnly to, string granularity)
    {
        var (startUtc, endUtc) = await ResolveUtcRangeAsync(branchId, from, to);

        var salesOverTime = await _unitOfWork.Orders.GetSalesOverTimeAsync(branchId, startUtc, endUtc, granularity);
        var topProducts = await _unitOfWork.Orders.GetTopProductsBIAsync(branchId, startUtc, endUtc);
        var salesByPaymentMethod = await _unitOfWork.Orders.GetSalesByPaymentMethodAsync(branchId, startUtc, endUtc);

        return new DashboardChartsDto
        {
            SalesOverTime = salesOverTime,
            TopProducts = topProducts,
            SalesByPaymentMethod = salesByPaymentMethod
        };
    }

    /// <summary>
    /// Generates a detailed sales CSV for accounting or auditing.
    /// Columns: OrderId, Date, Total, PaymentMethods, CustomerName, Facturado.
    /// Delegates to repository projection for SQL-level optimization.
    /// Only paid, non-cancelled orders are included.
    /// </summary>
    public async Task<string> GetDetailedSalesCsvAsync(int branchId, DateOnly from, DateOnly to)
    {
        var (startUtc, endUtc) = await ResolveUtcRangeAsync(branchId, from, to);
        var rows = await _unitOfWork.Orders.GetDetailedSalesCsvRowsAsync(branchId, startUtc, endUtc);

        var sb = new System.Text.StringBuilder();
        sb.Append('\uFEFF'); // BOM — ensures Excel opens the file with correct encoding
        sb.AppendLine("OrderId,Date,Total,PaymentMethods,CustomerName,Facturado");

        foreach (var row in rows)
        {
            var date = row.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
            var total = (row.TotalCents / 100m).ToString("F2");
            var methods = string.Join("|", row.PaymentMethods.Select(m => m.ToString()).Distinct());
            var fullName = string.IsNullOrEmpty(row.CustomerLastName)
                ? row.CustomerFirstName
                : $"{row.CustomerFirstName} {row.CustomerLastName}";
            var customerName = fullName.Replace(",", " "); // prevent CSV injection
            var facturado = row.InvoiceStatus.ToString();

            sb.AppendLine($"{row.Id},{date},{total},{methods},{customerName},{facturado}");
        }

        return sb.ToString();
    }

    #endregion

    #region Excel Sheet Builders

    private static void BuildSummarySheet(XLWorkbook workbook, ReportSummary summary)
    {
        var ws = workbook.Worksheets.Add("Resumen");

        ws.Cell("A1").Value = "Reporte de Ventas";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 16;

        ws.Cell("A2").Value = $"Período: {summary.From:dd/MM/yyyy} - {summary.To:dd/MM/yyyy}";

        var headerRow = 4;
        ws.Cell(headerRow, 1).Value = "Métrica";
        ws.Cell(headerRow, 2).Value = "Valor";
        StyleHeader(ws.Range(headerRow, 1, headerRow, 2));

        var metrics = new (string Label, string Value)[]
        {
            ("Total de ventas", FormatMoney(summary.TotalCents)),
            ("Órdenes completadas", summary.CompletedOrders.ToString()),
            ("Órdenes canceladas", summary.CancelledOrders.ToString()),
            ("Ticket promedio", FormatMoney((int)summary.AverageTicketCents)),
            ("Ventas efectivo", FormatMoney(summary.CashCents)),
            ("Ventas tarjeta", FormatMoney(summary.CardCents)),
            ("Total descuentos", FormatMoney(summary.TotalDiscountCents))
        };

        for (var i = 0; i < metrics.Length; i++)
        {
            ws.Cell(headerRow + 1 + i, 1).Value = metrics[i].Label;
            ws.Cell(headerRow + 1 + i, 2).Value = metrics[i].Value;
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(headerRow);
    }

    private static void BuildDailySheet(XLWorkbook workbook, ReportSummary summary)
    {
        var ws = workbook.Worksheets.Add("Ventas por día");

        var headers = new[] { "Fecha", "Órdenes", "Canceladas", "Total" };
        for (var i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];

        StyleHeader(ws.Range(1, 1, 1, headers.Length));

        for (var i = 0; i < summary.DailySummaries.Count; i++)
        {
            var day = summary.DailySummaries[i];
            var row = i + 2;
            ws.Cell(row, 1).Value = day.Date.ToString("dd/MM/yyyy");
            ws.Cell(row, 2).Value = day.OrderCount;
            ws.Cell(row, 3).Value = day.CancelledCount;
            ws.Cell(row, 4).Value = FormatMoney(day.TotalCents);
        }

        var totalRow = summary.DailySummaries.Count + 2;
        ws.Cell(totalRow, 1).Value = "TOTAL";
        ws.Cell(totalRow, 1).Style.Font.Bold = true;
        ws.Cell(totalRow, 2).Value = summary.DailySummaries.Sum(d => d.OrderCount);
        ws.Cell(totalRow, 2).Style.Font.Bold = true;
        ws.Cell(totalRow, 3).Value = summary.DailySummaries.Sum(d => d.CancelledCount);
        ws.Cell(totalRow, 3).Style.Font.Bold = true;
        ws.Cell(totalRow, 4).Value = FormatMoney(summary.TotalCents);
        ws.Cell(totalRow, 4).Style.Font.Bold = true;

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);
        if (summary.DailySummaries.Count > 0)
            ws.Range(1, 1, totalRow - 1, headers.Length).SetAutoFilter();
    }

    private static void BuildOrdersSheet(XLWorkbook workbook, ReportSummary summary)
    {
        var ws = workbook.Worksheets.Add("Órdenes");

        var headers = new[] { "#", "Fecha", "Hora", "Artículos", "Método", "Descuento", "Total", "Estado", "Motivo" };
        for (var i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];

        StyleHeader(ws.Range(1, 1, 1, headers.Length));

        for (var i = 0; i < summary.Orders.Count; i++)
        {
            var order = summary.Orders[i];
            var row = i + 2;
            ws.Cell(row, 1).Value = order.OrderNumber;
            ws.Cell(row, 2).Value = order.CreatedAt.ToString("dd/MM/yyyy");
            ws.Cell(row, 3).Value = order.CreatedAt.ToString("HH:mm");
            ws.Cell(row, 4).Value = order.ItemCount;
            ws.Cell(row, 5).Value = order.PaymentMethod;
            ws.Cell(row, 6).Value = order.TotalDiscountCents > 0 ? FormatMoney(order.TotalDiscountCents) : "";
            ws.Cell(row, 7).Value = FormatMoney(order.TotalCents);
            ws.Cell(row, 8).Value = order.Status;
            ws.Cell(row, 9).Value = order.CancellationReason ?? "";

            if (order.Status == "Cancelada")
            {
                ws.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml(CancelledRowColor);
            }
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);
        if (summary.Orders.Count > 0)
            ws.Range(1, 1, summary.Orders.Count + 1, headers.Length).SetAutoFilter();
    }

    private static void StyleHeader(IXLRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Font.FontColor = XLColor.White;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml(BrandColor);
    }

    #endregion

    #region PDF Builders

    private static void BuildPdfHeader(IContainer container, ReportSummary summary)
    {
        container.Row(row =>
        {
            row.RelativeItem().Text("POS Táctil — Reporte de Ventas")
                .Bold().FontSize(14);

            row.ConstantItem(200).AlignRight()
                .Text($"{summary.From:dd/MM/yyyy} - {summary.To:dd/MM/yyyy}")
                .FontColor(Colors.Grey.Medium).FontSize(10);
        });
    }

    private static void BuildPdfContent(IContainer container, ReportSummary summary)
    {
        container.PaddingTop(10).Column(column =>
        {
            // Section 1 — Metric cards
            column.Item().PaddingBottom(15).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn();
                    c.RelativeColumn();
                    c.RelativeColumn();
                    c.RelativeColumn();
                });

                var metrics = new (string Label, string Value)[]
                {
                    ("Total ventas", FormatMoney(summary.TotalCents)),
                    ("Órdenes", summary.CompletedOrders.ToString()),
                    ("Ticket promedio", FormatMoney((int)summary.AverageTicketCents)),
                    ("Descuentos", FormatMoney(summary.TotalDiscountCents)),
                    ("Efectivo", FormatMoney(summary.CashCents)),
                    ("Tarjeta", FormatMoney(summary.CardCents)),
                    ("Completadas", summary.CompletedOrders.ToString()),
                    ("Canceladas", summary.CancelledOrders.ToString())
                };

                for (var i = 0; i < metrics.Length; i++)
                {
                    var row = (uint)(i / 4 + 1);
                    var col = (uint)(i % 4 + 1);
                    table.Cell().Row(row).Column(col).Padding(5).Column(inner =>
                    {
                        inner.Item().Text(metrics[i].Label)
                            .FontSize(8).FontColor(Colors.Grey.Medium);
                        inner.Item().Text(metrics[i].Value)
                            .Bold().FontSize(12).FontColor(Colors.Green.Darken2);
                    });
                }
            });

            // Section 2 — Daily summaries
            if (summary.DailySummaries.Count > 1)
            {
                column.Item().PaddingBottom(10).Text("Ventas por día").Bold().FontSize(11);
                column.Item().PaddingBottom(15).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(3);
                        c.RelativeColumn(2);
                        c.RelativeColumn(3);
                    });

                    PdfTableHeader(table, "Fecha", "Órdenes", "Total");

                    foreach (var day in summary.DailySummaries)
                    {
                        PdfTableCell(table, day.Date.ToString("dd/MM/yyyy"));
                        PdfTableCell(table, day.OrderCount.ToString());
                        PdfTableCell(table, FormatMoney(day.TotalCents));
                    }
                });
            }

            // Section 3 — Top products
            if (summary.TopProducts.Count > 0)
            {
                column.Item().PaddingBottom(10).Text("Top 10 productos").Bold().FontSize(11);
                column.Item().PaddingBottom(15).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(30);
                        c.RelativeColumn(4);
                        c.RelativeColumn(2);
                        c.RelativeColumn(3);
                    });

                    PdfTableHeader(table, "#", "Producto", "Cantidad", "Total vendido");

                    for (var i = 0; i < summary.TopProducts.Count; i++)
                    {
                        var product = summary.TopProducts[i];
                        PdfTableCell(table, (i + 1).ToString());
                        PdfTableCell(table, product.Name);
                        PdfTableCell(table, product.Quantity.ToString());
                        PdfTableCell(table, FormatMoney(product.TotalCents));
                    }
                });
            }

            // Section 4 — Order details
            column.Item().PaddingBottom(10).Text("Detalle de órdenes").Bold().FontSize(11);
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(40);
                    c.RelativeColumn(3);
                    c.RelativeColumn(2);
                    c.RelativeColumn(2);
                    c.RelativeColumn(2);
                });

                PdfTableHeader(table, "#", "Fecha/Hora", "Método", "Total", "Estado");

                foreach (var order in summary.Orders)
                {
                    var isCancelled = order.Status == "Cancelada";
                    var bgColor = isCancelled ? Colors.Red.Lighten5 : Colors.White;

                    PdfTableCell(table, order.OrderNumber.ToString(), bgColor);
                    PdfTableCell(table, order.CreatedAt.ToString("dd/MM HH:mm"), bgColor);
                    PdfTableCell(table, order.PaymentMethod, bgColor);
                    PdfTableCell(table, FormatMoney(order.TotalCents), bgColor);
                    PdfTableCell(table, order.Status, bgColor);
                }
            });
        });
    }

    private static void BuildPdfFooter(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Text($"Generado el {DateTime.Now:dd/MM/yyyy HH:mm}")
                .FontSize(8).FontColor(Colors.Grey.Medium);

            row.RelativeItem().AlignRight().Text(text =>
            {
                text.Span("Página ").FontSize(8).FontColor(Colors.Grey.Medium);
                text.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                text.Span(" de ").FontSize(8).FontColor(Colors.Grey.Medium);
                text.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
            });
        });
    }

    private static void PdfTableHeader(TableDescriptor table, params string[] headers)
    {
        foreach (var header in headers)
        {
            table.Cell().Background(Colors.Green.Darken2).Padding(5)
                .Text(header).FontColor(Colors.White).Bold().FontSize(9);
        }
    }

    private static void PdfTableCell(TableDescriptor table, string value, string? bgColor = null)
    {
        var cell = table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(4);
        if (bgColor != null)
            cell = cell.Background(bgColor);
        cell.Text(value).FontSize(8);
    }

    #endregion

    #region Helpers

    private static string FormatMoney(int cents)
    {
        return $"${cents / 100m:N2}";
    }

    /// <summary>
    /// Resolves the inclusive local range <c>[from, to]</c> into a half-open UTC
    /// window <c>[startUtc, endUtc)</c> using the branch's persistent timezone.
    /// </summary>
    private async Task<(DateTime startUtc, DateTime endUtc)> ResolveUtcRangeAsync(
        int branchId, DateOnly from, DateOnly to)
    {
        var branch = await _unitOfWork.Branches.GetByIdAsync(branchId)
            ?? throw new NotFoundException($"Branch with id {branchId} not found");

        var (startUtc, _) = TimeZoneHelper.GetUtcRangeForLocalDate(from, branch.TimeZoneId);
        var (_, endUtc) = TimeZoneHelper.GetUtcRangeForLocalDate(to, branch.TimeZoneId);
        return (startUtc, endUtc);
    }

    #endregion
}
