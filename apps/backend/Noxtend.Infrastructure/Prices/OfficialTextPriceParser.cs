using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Noxtend.Tuning.Domain.Ports;

namespace Noxtend.Infrastructure.Prices;

internal static class OfficialTextPriceParser
{
    private const string Standard = "Paid Standard; USD per 1M tokens";

    public static IReadOnlyList<OfficialPriceCandidate> Parse(
        string provider, byte[] body, string url, DateTimeOffset collectedAt, IReadOnlyList<string> models)
    {
        var document = new HtmlParser().ParseDocument(Encoding.UTF8.GetString(body));
        foreach (var element in document.QuerySelectorAll("script, style")) element.Remove();
        var sha = Convert.ToHexStringLower(SHA256.HashData(body));
        DateTimeOffset? effective = null;
        string? dateBlocked = null;
        try { effective = EffectiveDate(document); }
        catch (FormatException) { dateBlocked = "공식 시행일 상충 확인 필요"; }
        return models.Distinct(StringComparer.Ordinal).SelectMany(model =>
        {
            var matches = (provider switch
            {
                "openai" => OpenAi(document, model),
                "anthropic" => Anthropic(document, model),
                "google" => Google(document, model),
                _ => [],
            }).ToArray();
            if (matches.Length == 0)
                matches = [new("text", Standard, null, "정확한 모델·Standard 요금·단위 확인 필요",
                    "정확한 모델·Standard 요금·단위 확인 필요")];
            return matches.GroupBy(match => (match.Area, match.Conditions)).Select(group =>
            {
                var first = group.First();
                var conflicting = group.Select(match => (match.Terms, match.BlockedReason)).Distinct().Count() > 1;
                var terms = conflicting || dateBlocked is not null ? null : first.Terms;
                if (terms is not null) terms = terms with { OfficialEffectiveFrom = effective };
                return new OfficialPriceCandidate(model, first.Area, first.Area, first.Conditions, terms,
                    group.Select(match => new OfficialPriceEvidence(url, collectedAt, sha, match.Evidence))
                        .Concat(dateBlocked is null ? [] : document.QuerySelectorAll("time[datetime]")
                            .Where(t => Text(t.ParentElement).StartsWith("All prices on this page become effective from", StringComparison.Ordinal))
                            .Select(t => new OfficialPriceEvidence(url, collectedAt, sha, Text(t.ParentElement))))
                        .Distinct().ToArray(),
                    dateBlocked ?? (conflicting ? "공식 문서 내 동일 요금 조건 상충 확인 필요" : first.BlockedReason));
            });
        }).ToList();
    }

    private static IEnumerable<Parsed> OpenAi(IDocument document, string model)
    {
        var heading = "";
        var section = "";
        foreach (var element in document.QuerySelectorAll("h2, h3, table"))
        {
            if (element.LocalName is "h2" or "h3")
            {
                heading = Text(element);
                if (element.LocalName == "h2") section = heading;
                continue;
            }
            if (section is not ("Flagship models" or "Cyber models" or "Specialized models") && heading != "Image generation models") continue;
            var pane = element.Closest("[data-content-switcher-pane]");
            if (pane?.GetAttribute("data-value") != "standard") continue;
            var switcher = pane.Closest("[data-content-switcher-root]");
            var meta = switcher?.PreviousElementSibling?.QuerySelector(".pricing-switcher-meta");
            if (meta is null || !Text(meta).Contains("Prices per 1M tokens", StringComparison.Ordinal)) continue;
            var headers = element.QuerySelectorAll("thead th").Select(Text).ToArray();
            var modelIndex = Array.IndexOf(headers, "Model");
            var inputIndex = Array.IndexOf(headers, "Input");
            var outputIndex = Array.IndexOf(headers, "Output");
            if (modelIndex < 0 || inputIndex < 0 || outputIndex < 0) continue;
            foreach (var row in element.QuerySelectorAll("tbody tr"))
            {
                var cells = row.Children.ToArray();
                if (cells.Length != headers.Length || Text(cells[modelIndex]) != model) continue;
                if (headers.Contains("Modality"))
                {
                    var textRow = row.NextElementSibling;
                    var evidence = heading + "; Standard; " + string.Join(" / ", headers) + "; " + Text(row);
                    if (textRow is not null && Text(textRow.Children.FirstOrDefault()) == "Text")
                        evidence += "; Text input: " + Text(textRow);
                    yield return new Parsed("image", Standard + "; mixed text/image modalities", null,
                        evidence, "텍스트·이미지 입력별 토큰 요금 계산 지원 필요");
                    continue;
                }
                if (!Money(Text(cells[inputIndex]), out var input) || !Money(Text(cells[outputIndex]), out var output)) continue;
                yield return new Parsed("text", Standard, new(input, output), heading + "; Standard; " + Text(row), null);
            }
        }
    }

    private static IEnumerable<Parsed> Anthropic(IDocument document, string model)
    {
        var heading = "";
        foreach (var element in document.QuerySelectorAll("h2, table"))
        {
            if (element.LocalName == "h2") { heading = Text(element); continue; }
            if (!heading.StartsWith("Model pricing", StringComparison.Ordinal)) continue;
            var headerRows = element.QuerySelectorAll("thead tr").ToArray();
            if (headerRows.Length != 2) continue;
            var groups = headerRows[0].Children.ToArray();
            var columns = headerRows[1].Children.ToArray();
            if (!groups.Select(Text).SequenceEqual(["Model", "Base tokens", "Prompt caching"]) ||
                !columns.Select(Text).SequenceEqual(["Name", "Input", "Output", "5m writes", "1h writes", "Hits and refreshes"])) continue;
            if ((groups[0].GetAttribute("colspan") ?? "1") != "1" || groups[1].GetAttribute("colspan") != "2" ||
                groups[2].GetAttribute("colspan") != "3" || columns.Any(c => (c.GetAttribute("colspan") ?? "1") != "1") ||
                groups.Concat(columns).Any(c => (c.GetAttribute("rowspan") ?? "1") != "1")) continue;
            foreach (var row in element.QuerySelectorAll("tbody tr"))
            {
                var cells = row.Children.ToArray();
                if (cells.Length != 6) continue;
                var link = cells[0].QuerySelector("a[href]")?.GetAttribute("href");
                if (link is null || !link.StartsWith("/docs/en/models/", StringComparison.Ordinal) || !link.EndsWith("/overview", StringComparison.Ordinal)) continue;
                var exactId = "claude-" + link["/docs/en/models/".Length..^"/overview".Length];
                if (exactId != model) continue;
                if (!TokenMoney(Text(cells[1]), out var input) || !TokenMoney(Text(cells[2]), out var output)) continue;
                if (cells[0].GetAttribute("rowspan") is not (null or "1"))
                {
                    yield return new Parsed("text", Standard, null, Text(row), "컨텍스트 구간별 요금 확인 필요");
                    continue;
                }
                yield return new Parsed("text", Standard, new(input, output), "Model pricing; Base tokens; " + Text(row), null);
            }
        }
    }

    private static IEnumerable<Parsed> Google(IDocument document, string model)
    {
        var currentModel = "";
        var elements = document.QuerySelectorAll("h2, table, p").ToArray();
        for (var i = 0; i < elements.Length; i++)
        {
            var element = elements[i];
            if (element.LocalName == "h2") { currentModel = element.Id; continue; }
            if (element.LocalName != "table" || currentModel != model) continue;
            if (Text(element.ParentElement?.QuerySelector("h3")) != "Standard") continue;
            var headers = element.QuerySelectorAll("thead th").Select(Text).ToArray();
            var paidIndex = Array.FindIndex(headers, h => h == "Paid Tier, per 1M tokens in USD");
            if (paidIndex < 0) continue;
            var inputs = new List<string>();
            var outputs = new List<string>();
            foreach (var row in element.QuerySelectorAll("tbody tr"))
            {
                var cells = row.Children.ToArray();
                if (cells.Length != headers.Length) continue;
                var label = Text(cells[0]);
                if (label.StartsWith("Input price", StringComparison.Ordinal)) inputs.Add(TextWithBreaks(cells[paidIndex]));
                if (label.StartsWith("Output price", StringComparison.Ordinal)) outputs.Add(TextWithBreaks(cells[paidIndex]));
            }
            if (inputs.Count == 0 || outputs.Count == 0) continue;
            if (inputs.Distinct().Count() != 1 || outputs.Distinct().Count() != 1)
            {
                yield return new Parsed("text", Standard, null,
                    "Standard; Paid Tier; input=" + string.Join(" / ", inputs) + "; output=" + string.Join(" / ", outputs),
                    "공식 표 내 입력·출력 요금 상충 확인 필요");
                continue;
            }
            var inputText = inputs[0];
            var outputText = outputs[0];
            if (Regex.IsMatch(outputText, @"^\$\d+(?:\.\d+)? per image\*?$"))
            {
                if (!GoogleInput(inputText, "text / image", out var input, out _) || !Money(outputText.Split(' ')[0], out var perImage)) continue;
                var notes = new List<string>();
                for (var j = i + 1; j < elements.Length && elements[j].LocalName != "h2"; j++)
                    if (elements[j].LocalName == "p") notes.Add(Text(elements[j]));
                var note = string.Join(" ", notes);
                var size = Regex.Match(note, @"up to (\d+x\d+)px");
                if (!size.Success || !note.Contains("per 1,000,000 tokens", StringComparison.Ordinal)) continue;
                var evidence = $"input={input.ToString("0.00", CultureInfo.InvariantCulture)} USD/1M text/image tokens; output={perImage.ToString(CultureInfo.InvariantCulture)} USD/image; size<={size.Groups[1].Value}; {note}";
                yield return new Parsed("image", Standard + "; text/image input + image output", null, evidence,
                    "입력 토큰·출력 이미지 혼합 요금 계산 지원 필요");
                continue;
            }
            if (!GoogleInput(inputText, "text / image / video", out var textInput, out var mixedInput) || !Money(outputText, out var textOutput)) continue;
            yield return new Parsed("text", Standard + "; text/image/video input" + (inputText.Contains("(audio)", StringComparison.Ordinal) ? "; audio input" : ""), new(textInput, textOutput),
                "Standard; Paid Tier, per 1M tokens in USD; input=" + inputText + "; output=" + outputText,
                mixedInput ? "입력 모달리티별 서로 다른 요금 계산 지원 필요" : null);
        }
    }

    private static bool GoogleInput(string value, string modalities, out decimal money, out bool mixedInput)
    {
        mixedInput = false;
        if (Money(value, out money)) return true;
        var lines = value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return false;
        var main = lines[0];
        var suffix = " (" + modalities + ")";
        if (!main.EndsWith(suffix, StringComparison.Ordinal)) return false;
        if (lines.Length > 2 || lines.Length == 2 && !Regex.IsMatch(lines[1], @"^\$\d+(?:\.\d+)? \(audio\)$")) return false;
        if (!Money(main[..^suffix.Length], out money)) return false;
        if (lines.Length == 2)
        {
            if (!Money(lines[1][..^" (audio)".Length], out var audioRate)) return false;
            mixedInput = audioRate != money;
        }
        return true;
    }

    private static bool TokenMoney(string value, out decimal money)
    {
        const string unit = " / MTok";
        money = 0;
        return value.EndsWith(unit, StringComparison.Ordinal) && Money(value[..^unit.Length], out money);
    }

    private static bool Money(string value, out decimal money)
    {
        money = 0;
        return Regex.IsMatch(value, @"^\$\d+(?:\.\d+)?$") &&
            decimal.TryParse(value[1..], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out money);
    }

    internal static DateTimeOffset? EffectiveDate(IDocument document)
    {
        var dates = document.QuerySelectorAll("time[datetime]")
            .Where(t => Text(t.ParentElement).StartsWith("All prices on this page become effective from", StringComparison.Ordinal))
            .Select(t => DateTimeOffset.TryParse(t.GetAttribute("datetime"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var value) ? (DateTimeOffset?)value.ToUniversalTime() : null)
            .Distinct().ToArray();
        if (dates.Length > 1 || dates.Length == 1 && dates[0] is null)
            throw new FormatException("공식 시행일 확인 필요");
        return dates.SingleOrDefault();
    }

    private static string Text(IElement? element) => Regex.Replace(element?.TextContent ?? "", @"\s+", " ").Trim();

    private static string TextWithBreaks(IElement element)
    {
        var clone = (IElement)element.Clone(true);
        foreach (var br in clone.QuerySelectorAll("br")) br.Replace(clone.Owner!.CreateTextNode(" | "));
        return Text(clone);
    }

    private sealed record Parsed(string Area, string Conditions, OfficialPriceTerms? Terms, string Evidence, string? BlockedReason);
}
