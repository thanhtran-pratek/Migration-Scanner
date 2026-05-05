// MigrationScanner/Report/HtmlReporter.cs
using System.Text;
using MigrationScanner.Scanner;

namespace MigrationScanner.Report;

public static class HtmlReporter
{
    public static string Generate(IEnumerable<Finding> findings, string solutionName)
    {
        var list = findings.ToList();
        var errors = list.Count(f => f.Severity == Severity.Error);
        var warnings = list.Count(f => f.Severity == Severity.Warning);
        var infos = list.Count(f => f.Severity == Severity.Info);
        var date = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        var cards = new StringBuilder();
        foreach (var f in list)
        {
            var badgeColor = f.Severity switch
            {
                Severity.Error => "#dc3545",
                Severity.Warning => "#fd7e14",
                _ => "#0dcaf0"
            };
            var suggestion = f.Suggestion != null
                ? $"<div class='suggestion'>💡 {Escape(f.Suggestion)}</div>"
                : "";
            var sev = f.Severity.ToString().ToLower();
            cards.AppendLine($"<div class='finding {sev}' data-severity='{sev}'>");
            cards.AppendLine($"  <div class='finding-header'>");
            cards.AppendLine($"    <span class='badge' style='background:{badgeColor}'>{f.Severity}</span>");
            cards.AppendLine($"    <span class='category'>{Escape(f.Category)}</span>");
            cards.AppendLine($"    <span class='location'>{Escape(f.FilePath)}:{f.Line}</span>");
            cards.AppendLine($"  </div>");
            cards.AppendLine($"  <div class='message'>{Escape(f.Message)}</div>");
            cards.AppendLine($"  <pre class='snippet'>{Escape(f.Snippet)}</pre>");
            if (suggestion.Length > 0) cards.AppendLine($"  {suggestion}");
            cards.AppendLine("</div>");
        }

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang='en'>");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset='UTF-8'>");
        sb.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");
        sb.AppendLine("<title>Migration Readiness Report</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("  body { font-family: system-ui, sans-serif; margin: 0; padding: 0; background: #f8f9fa; color: #212529; }");
        sb.AppendLine("  .header { background: #1e1e2e; color: white; padding: 24px 32px; }");
        sb.AppendLine("  .header h1 { margin: 0 0 4px; font-size: 1.6rem; }");
        sb.AppendLine("  .header .meta { color: #adb5bd; font-size: 0.9rem; }");
        sb.AppendLine("  .summary { display: flex; gap: 16px; padding: 20px 32px; background: white; border-bottom: 1px solid #dee2e6; }");
        sb.AppendLine("  .stat { padding: 12px 20px; border-radius: 8px; text-align: center; min-width: 80px; }");
        sb.AppendLine("  .stat.error { background: #f8d7da; color: #842029; }");
        sb.AppendLine("  .stat.warning { background: #fff3cd; color: #664d03; }");
        sb.AppendLine("  .stat.info { background: #cff4fc; color: #055160; }");
        sb.AppendLine("  .stat .count { font-size: 2rem; font-weight: bold; }");
        sb.AppendLine("  .stat .label { font-size: 0.8rem; text-transform: uppercase; }");
        sb.AppendLine("  .controls { padding: 16px 32px; background: white; border-bottom: 1px solid #dee2e6; }");
        sb.AppendLine("  .controls button { margin-right: 8px; padding: 6px 16px; border: 1px solid #dee2e6; border-radius: 4px; cursor: pointer; background: white; }");
        sb.AppendLine("  .controls button.active { background: #0d6efd; color: white; border-color: #0d6efd; }");
        sb.AppendLine("  .findings { padding: 20px 32px; max-width: 1200px; margin: 0 auto; }");
        sb.AppendLine("  .finding { background: white; border-radius: 8px; padding: 16px; margin-bottom: 12px; border-left: 4px solid #dee2e6; box-shadow: 0 1px 3px rgba(0,0,0,.08); }");
        sb.AppendLine("  .finding.error { border-left-color: #dc3545; }");
        sb.AppendLine("  .finding.warning { border-left-color: #fd7e14; }");
        sb.AppendLine("  .finding.info { border-left-color: #0dcaf0; }");
        sb.AppendLine("  .finding-header { display: flex; align-items: center; gap: 10px; margin-bottom: 8px; flex-wrap: wrap; }");
        sb.AppendLine("  .badge { padding: 2px 10px; border-radius: 12px; color: white; font-size: 0.75rem; font-weight: 600; text-transform: uppercase; }");
        sb.AppendLine("  .category { font-weight: 600; font-size: 0.9rem; }");
        sb.AppendLine("  .location { color: #6c757d; font-size: 0.85rem; margin-left: auto; font-family: monospace; }");
        sb.AppendLine("  .message { margin-bottom: 8px; }");
        sb.AppendLine("  .snippet { background: #1e1e2e; color: #cdd6f4; padding: 10px; border-radius: 4px; font-size: 0.82rem; overflow-x: auto; margin: 8px 0; }");
        sb.AppendLine("  .suggestion { color: #0f5132; background: #d1e7dd; padding: 6px 12px; border-radius: 4px; font-size: 0.85rem; }");
        sb.AppendLine("  .finding.hidden { display: none; }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<div class='header'>");
        sb.AppendLine("  <h1>Migration Readiness Report</h1>");
        sb.AppendLine($"  <div class='meta'>Scanned: {Escape(solutionName)} &nbsp;|&nbsp; Generated: {date}</div>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class='summary'>");
        sb.AppendLine($"  <div class='stat error'><div class='count'>{errors}</div><div class='label'>Errors</div></div>");
        sb.AppendLine($"  <div class='stat warning'><div class='count'>{warnings}</div><div class='label'>Warnings</div></div>");
        sb.AppendLine($"  <div class='stat info'><div class='count'>{infos}</div><div class='label'>Info</div></div>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class='controls'>");
        sb.AppendLine("  <strong>Filter: </strong>");
        sb.AppendLine($"  <button class='active' onclick='filter(\"all\")'>All ({list.Count})</button>");
        sb.AppendLine($"  <button onclick='filter(\"error\")'>Errors ({errors})</button>");
        sb.AppendLine($"  <button onclick='filter(\"warning\")'>Warnings ({warnings})</button>");
        sb.AppendLine($"  <button onclick='filter(\"info\")'>Info ({infos})</button>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class='findings'>");
        sb.Append(cards);
        sb.AppendLine("</div>");
        sb.AppendLine("<script>");
        sb.AppendLine("function filter(sev) {");
        sb.AppendLine("  document.querySelectorAll('.controls button').forEach(b => b.classList.remove('active'));");
        sb.AppendLine("  event.target.classList.add('active');");
        sb.AppendLine("  document.querySelectorAll('.finding').forEach(el => {");
        sb.AppendLine("    el.classList.toggle('hidden', sev !== 'all' && el.dataset.severity !== sev);");
        sb.AppendLine("  });");
        sb.AppendLine("}");
        sb.AppendLine("</script>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;")
         .Replace("<", "&lt;")
         .Replace(">", "&gt;")
         .Replace("\"", "&quot;")
         .Replace("'", "&#39;");
}
