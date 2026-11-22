using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;
using PdfSharpCore.Utils;
using PayDesk.Core.Models;

namespace PayDesk.Consolidator.Services;

public class PdfPayslipGenerator
{
    static PdfPayslipGenerator()
    {
        if (GlobalFontSettings.FontResolver == null)
        {
            GlobalFontSettings.FontResolver = new FontResolver();
        }
    }

    public static void GeneratePayslip(ApplicationUser user, string month, List<ExpenseItem> items, decimal totalPayout, string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var document = new PdfDocument();
        document.Info.Title = $"Payslip - {user.UserName} - {month}";
        document.Info.Subject = "Monthly Expense Reimbursement Payslip";

        var page = document.AddPage();
        page.Size = PdfSharpCore.PageSize.A4;

        using var gfx = XGraphics.FromPdfPage(page);

        var titleFont = new XFont("Arial", 20, XFontStyle.Bold);
        var subTitleFont = new XFont("Arial", 12, XFontStyle.Regular);
        var sectionFont = new XFont("Arial", 13, XFontStyle.Bold);
        var bodyBoldFont = new XFont("Arial", 10, XFontStyle.Bold);
        var bodyFont = new XFont("Arial", 10, XFontStyle.Regular);
        var smallFont = new XFont("Arial", 8, XFontStyle.Italic);

        var primaryColor = XColor.FromArgb(24, 76, 120);
        var secondaryColor = XColor.FromArgb(240, 244, 248);
        var borderColor = XColor.FromArgb(210, 220, 230);
        var textColor = XColor.FromArgb(30, 41, 59);

        // Header Background Banner
        gfx.DrawRectangle(new XSolidBrush(primaryColor), 0, 0, page.Width, 80);

        // Header Text
        gfx.DrawString("PAYDESK EXPENSE MANAGEMENT", titleFont, XBrushes.White, new XPoint(40, 45));
        gfx.DrawString("Official Expense Reimbursement Payslip", subTitleFont, XBrushes.LightSteelBlue, new XPoint(40, 65));

        double y = 110;

        // Metadata Box
        gfx.DrawRoundedRectangle(new XPen(borderColor, 1), new XSolidBrush(secondaryColor), 40, y, page.Width - 80, 75, 5, 5);

        gfx.DrawString("Employee Name:", bodyBoldFont, new XSolidBrush(textColor), new XPoint(55, y + 25));
        gfx.DrawString(user.FullName ?? user.UserName ?? "Employee", bodyFont, new XSolidBrush(textColor), new XPoint(160, y + 25));

        gfx.DrawString("Employee Email:", bodyBoldFont, new XSolidBrush(textColor), new XPoint(55, y + 45));
        gfx.DrawString(user.Email ?? "N/A", bodyFont, new XSolidBrush(textColor), new XPoint(160, y + 45));

        gfx.DrawString("Pay Period:", bodyBoldFont, new XSolidBrush(textColor), new XPoint(340, y + 25));
        gfx.DrawString(month, bodyFont, new XSolidBrush(textColor), new XPoint(420, y + 25));

        gfx.DrawString("Generated On:", bodyBoldFont, new XSolidBrush(textColor), new XPoint(340, y + 45));
        gfx.DrawString(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm UTC"), bodyFont, new XSolidBrush(textColor), new XPoint(420, y + 45));

        y += 105;

        // Table Header
        gfx.DrawString("Itemized Expenses", sectionFont, new XSolidBrush(primaryColor), new XPoint(40, y));
        y += 15;

        double tableX = 40;
        double tableWidth = page.Width - 80;
        double headerHeight = 25;

        gfx.DrawRectangle(new XSolidBrush(primaryColor), tableX, y, tableWidth, headerHeight);
        gfx.DrawString("Date", bodyBoldFont, XBrushes.White, new XPoint(tableX + 10, y + 17));
        gfx.DrawString("Description", bodyBoldFont, XBrushes.White, new XPoint(tableX + 110, y + 17));
        gfx.DrawString("Amount (USD)", bodyBoldFont, XBrushes.White, new XPoint(tableX + tableWidth - 100, y + 17));

        y += headerHeight;

        // Table Rows
        double rowHeight = 22;
        int rowIndex = 0;

        foreach (var item in items)
        {
            if (y + rowHeight > page.Height - 120)
            {
                // New page if space runs out
                break;
            }

            var rowBg = (rowIndex % 2 == 0) ? XBrushes.White : new XSolidBrush(secondaryColor);
            gfx.DrawRectangle(rowBg, tableX, y, tableWidth, rowHeight);
            gfx.DrawRectangle(new XPen(borderColor, 0.5), tableX, y, tableWidth, rowHeight);

            gfx.DrawString(item.Date.ToString("yyyy-MM-dd"), bodyFont, new XSolidBrush(textColor), new XPoint(tableX + 10, y + 15));

            string desc = item.Description.Length > 45 ? item.Description[..42] + "..." : item.Description;
            gfx.DrawString(desc, bodyFont, new XSolidBrush(textColor), new XPoint(tableX + 110, y + 15));

            string amountStr = $"${item.Amount:N2}";
            gfx.DrawString(amountStr, bodyFont, new XSolidBrush(textColor), new XPoint(tableX + tableWidth - 90, y + 15));

            y += rowHeight;
            rowIndex++;
        }

        y += 20;

        // Total Box
        double totalBoxWidth = 220;
        double totalBoxX = page.Width - 40 - totalBoxWidth;
        gfx.DrawRoundedRectangle(new XPen(primaryColor), new XSolidBrush(primaryColor), totalBoxX, y, totalBoxWidth, 45, 4, 4);

        gfx.DrawString("TOTAL PAYOUT", bodyBoldFont, XBrushes.White, new XPoint(totalBoxX + 15, y + 28));
        string totalStr = $"${totalPayout:N2}";
        gfx.DrawString(totalStr, titleFont, XBrushes.White, new XPoint(totalBoxX + 105, y + 31));

        // Footer
        gfx.DrawLine(new XPen(borderColor, 1), 40, page.Height - 50, page.Width - 40, page.Height - 50);
        gfx.DrawString("This is a computer-generated payslip created by the PayDesk Consolidation Engine.", smallFont, XBrushes.DarkGray, new XPoint(40, page.Height - 35));

        document.Save(outputPath);
    }
}
