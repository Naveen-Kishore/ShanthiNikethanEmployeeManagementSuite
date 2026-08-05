using System.Text.RegularExpressions;
using ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Data;

namespace ShanthiNikethan.EmployeeManagement.Modules.Leave.Services;

public class ParsedLeaveMessage
{
    public string? ExtractedNameText { get; set; }
    public DateOnly? Date { get; set; }
    public decimal? Days { get; set; }
    public string? Reason { get; set; }
    public string? SubstituteNotes { get; set; }
    public List<Staff> MatchedStaffCandidates { get; set; } = new();
}

/// <summary>
/// Parses the school's WhatsApp leave-notification format into structured
/// data. Deliberately never auto-commits a match — staff names get typed
/// inconsistently by real people ("G. Abinaya" vs "suganya. D" vs plain
/// "Kesavan"), and with near-duplicate names on staff (e.g. two people
/// both named "Suganthi"), a silent wrong match would be worse than no
/// match at all. This always returns candidates for a human to confirm.
/// </summary>
public static class WhatsAppLeaveParser
{
    // "Name:" up to the next known label — which may be on a later line,
    // since real messages often put "Name:" on its own line (e.g. "- Name:
    // suganya. D" followed by "- Date : ..." on the next line). Singleline
    // lets "." match newlines so the search can look past the line break.
    // "Date(?:\(s\))?" tolerates "Date(s):" as well as plain "Date:".
    private static readonly Regex NameRegex = new(
        @"Name\s*[:.]\s*(.+?)(?=\s*(?:No[\s.]*[Oo]f[\s.]*[Dd]ays|Total[\s.]*Days|Date(?:\(s\))?\s*[.:]|Day\s*[.:]|I\s+have|-{2,}|$))",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    // DD.MM.YYYY / DD-MM-YYYY / DD/MM/YY etc. — India always writes date-first.
    // "Date" is sometimes followed by a stray period before the colon
    // (e.g. "Date.  :31/07/26"), or written as "Date(s):" — both tolerated.
    private static readonly Regex DateRegex = new(
        @"Date(?:\(s\))?[\s.:]*(\d{1,2})\s*[.\-/]\s*(\d{1,2})\s*[.\-/]\s*(\d{2,4})",
        RegexOptions.IgnoreCase);

    // "No Of days :1" / "No.of days:1" / "Total Days:1" or half-days like "0.5"
    private static readonly Regex DaysRegex = new(
        @"(?:No[\s.]*[Oo]f[\s.]*[Dd]ays|Total[\s.]*Days)\s*:?\s*(\d+(?:\.\d+)?)",
        RegexOptions.IgnoreCase);

    // "Reason: Going to attend marriage" — stops before the substitute
    // section (Alternative staff / Substitute Teachers) so that section
    // doesn't get swallowed into the reason text.
    private static readonly Regex ReasonRegex = new(
        @"Reason\s*[:.]\s*(.+?)(?=\s*(?:No[\s.]*[Oo]f[\s.]*[Dd]ays|Total[\s.]*Days|Date(?:\(s\))?\s*[.:]|Day\s*[.:]|Alternative|Substitute|I\s+have|-{2,}|$))",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public static ParsedLeaveMessage Parse(string message, List<Staff> activeStaff)
    {
        var result = new ParsedLeaveMessage();
        if (string.IsNullOrWhiteSpace(message)) return result;

        // WhatsApp text often carries invisible formatting characters
        // (zero-width joiners, word joiners) that are invisible but would
        // otherwise silently corrupt name token matching.
        message = Regex.Replace(message, "[\u200B\u200C\u200D\u2060\uFEFF]", "");

        var nameMatch = NameRegex.Match(message);
        if (nameMatch.Success)
        {
            result.ExtractedNameText = nameMatch.Groups[1].Value.Trim(' ', '-', '.', '\r', '\n', '\t');
            result.MatchedStaffCandidates = MatchStaff(result.ExtractedNameText, activeStaff);
        }

        var dateMatch = DateRegex.Match(message);
        if (dateMatch.Success)
        {
            var day = int.Parse(dateMatch.Groups[1].Value);
            var month = int.Parse(dateMatch.Groups[2].Value);
            var yearRaw = dateMatch.Groups[3].Value;
            var year = yearRaw.Length == 2 ? 2000 + int.Parse(yearRaw) : int.Parse(yearRaw);

            try { result.Date = new DateOnly(year, month, day); }
            catch (ArgumentOutOfRangeException) { /* leave null if the date is nonsensical */ }
        }

        var daysMatch = DaysRegex.Match(message);
        if (daysMatch.Success && decimal.TryParse(daysMatch.Groups[1].Value, out var days))
            result.Days = days;

        var reasonMatch = ReasonRegex.Match(message);
        if (reasonMatch.Success)
        {
            var reasonText = reasonMatch.Groups[1].Value.Trim(' ', '-', '.', '\r', '\n', '\t');
            if (reasonText.Length > 0)
                result.Reason = reasonText;
        }

        // Substitute notes: everything after whichever of Date/Days/Reason
        // appears last in the message — that's reliably where the
        // period-by-period breakdown starts, regardless of which order
        // the sender wrote these fields in, and without Reason's own text
        // leaking into the substitute notes. If none of those were found
        // at all (some messages skip straight to "Alternative staff:"
        // with no Name/Date/Days), fall back to right after the Name
        // match, or the very start of the message if even that's absent —
        // otherwise notes would silently be dropped instead of captured.
        var cutPoints = new[] {
            dateMatch.Success ? dateMatch.Index + dateMatch.Length : -1,
            daysMatch.Success ? daysMatch.Index + daysMatch.Length : -1,
            reasonMatch.Success ? reasonMatch.Index + reasonMatch.Length : -1
        };
        var cutAt = cutPoints.Max();
        if (cutAt < 0)
            cutAt = nameMatch.Success ? nameMatch.Index + nameMatch.Length : 0;
        if (cutAt >= 0 && cutAt < message.Length)
        {
            var tail = message[cutAt..].Trim(' ', '-', '\r', '\n');
            if (tail.Length > 0)
                result.SubstituteNotes = tail;
        }

        return result;
    }

    /// <summary>
    /// Matches an extracted name fragment against active staff, handling
    /// "Name Initial" vs "Initial Name" order and punctuation variants.
    /// Returns candidates ranked best-first; caller should treat this as
    /// suggestions to confirm, not a final answer.
    /// </summary>
    public static List<Staff> MatchStaff(string extractedName, List<Staff> activeStaff)
    {
        var tokens = extractedName
            .Split(new[] { '.', ' ', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToUpperInvariant())
            .ToList();

        if (tokens.Count == 0) return new();

        var fullNameToken = tokens.FirstOrDefault(t => t.Length > 1);   // the actual name, not an initial
        var initialTokens = tokens.Where(t => t.Length == 1).ToList();  // any single-letter initials given

        var scored = activeStaff.Select(s =>
        {
            var dbTokens = s.DisplayName.ToUpperInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .ToList();
            var dbFullNameToken = dbTokens.FirstOrDefault(t => t.Length > 1);
            var dbInitialTokens = dbTokens.Where(t => t.Length == 1).ToList();

            int score = 0;
            if (fullNameToken != null && dbFullNameToken != null && fullNameToken == dbFullNameToken)
                score += 10;
            else if (fullNameToken != null && dbFullNameToken != null &&
                     (dbFullNameToken.StartsWith(fullNameToken) || fullNameToken.StartsWith(dbFullNameToken)))
                score += 5; // partial match, e.g. typo or shortened name

            if (initialTokens.Count > 0 && dbInitialTokens.Count > 0 &&
                initialTokens.Intersect(dbInitialTokens).Any())
                score += 3;

            return (staff: s, score);
        })
        .Where(x => x.score > 0)
        .OrderByDescending(x => x.score)
        .ThenBy(x => x.staff.DisplayName)
        .ToList();

        if (scored.Count == 0) return new();

        // A staff member who only happens to share a single-letter initial
        // (score 3) shouldn't count as "ambiguous" against a genuine
        // full-name match (score 10-13) — only candidates close to the
        // best score represent real ambiguity worth flagging to a human.
        var topScore = scored[0].score;
        return scored.Where(x => x.score >= topScore - 2).Select(x => x.staff).ToList();
    }
}
