using System.Text;

namespace ShanthiNikethan.EmployeeManagement.Modules.Payroll.Services;

/// <summary>
/// Converts a whole rupee amount into words using the Indian numbering
/// system (Lakh/Crore, not Million/Billion). Verified against the school's
/// own historical wage statements — e.g. 564654 → "Five Lakh Sixty Four
/// Thousand Six Hundred and Fifty Four".
/// </summary>
public static class IndianNumberToWords
{
    private static readonly string[] Ones =
    {
        "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten",
        "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen"
    };
    private static readonly string[] Tens =
    {
        "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"
    };

    public static string Convert(long number)
    {
        if (number == 0) return "Zero";
        if (number < 0) return "Minus " + Convert(-number);

        var crore = number / 10000000; number %= 10000000;
        var lakh = number / 100000; number %= 100000;
        var thousand = number / 1000; number %= 1000;
        var hundred = number / 100; number %= 100;
        var remainder = number;

        var words = new StringBuilder();
        if (crore > 0) words.Append(ConvertTwoDigit(crore)).Append(" Crore ");
        if (lakh > 0) words.Append(ConvertTwoDigit(lakh)).Append(" Lakh ");
        if (thousand > 0) words.Append(ConvertTwoDigit(thousand)).Append(" Thousand ");
        if (hundred > 0) words.Append(Ones[hundred]).Append(" Hundred ");
        if (remainder > 0)
        {
            if (hundred > 0 || thousand > 0 || lakh > 0 || crore > 0) words.Append("and ");
            words.Append(ConvertTwoDigit(remainder));
        }

        return words.ToString().Trim();
    }

    /// <summary>Whole rupee amount as "... Rupees Only", for the amount-in-words line.</summary>
    public static string ToRupeesInWords(decimal amount)
    {
        var whole = (long)Math.Round(amount, 0, MidpointRounding.AwayFromZero);
        return $"{Convert(whole)} Rupees Only";
    }

    private static string ConvertTwoDigit(long n)
    {
        if (n < 20) return Ones[n];
        var tens = n / 10;
        var ones = n % 10;
        return (Tens[tens] + (ones > 0 ? " " + Ones[ones] : "")).Trim();
    }
}
