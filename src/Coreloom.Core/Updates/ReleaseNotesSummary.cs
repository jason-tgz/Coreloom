using System.Text;
using System.Text.RegularExpressions;

namespace Coreloom.Core.Updates;

/// <summary>One line of a "What's new" summary: an optional kind ("New", "Fixed"...), an optional short title and a sentence.</summary>
public sealed record ReleaseNoteItem(string? Category, string? Title, string Text);

/// <summary>
/// A short "What's new" summary built from a release's Markdown notes (the GitHub release body, which the release
/// workflow copies from CHANGELOG.md). It keeps the opening sentence and up to a handful of top-level bullet points,
/// each reduced to its bold lead and first sentence, so a release can be understood at a glance in the app.
/// </summary>
/// <remarks>
/// Where the items come from, in order of preference:
/// <list type="number">
/// <item>bullets under a heading such as <c>Highlights</c>, <c>Summary</c> or <c>What's new</c>;</item>
/// <item>bullets under Keep a Changelog headings (<c>Security</c>, <c>Added</c>, <c>Changed</c>, <c>Fixed</c>,
/// <c>Removed</c>, <c>Deprecated</c>), tagged with a short category;</item>
/// <item>any other top-level bullets.</item>
/// </list>
/// Nested bullets, code blocks, tables and everything after a horizontal rule (the download footer the release
/// workflow appends) are ignored.
/// </remarks>
public sealed partial record ReleaseNotesSummary(string? Intro, IReadOnlyList<ReleaseNoteItem> Items, int MoreCount)
{
    public const int DefaultMaxItems = 5;
    public const int MaxIntroLength = 220;
    public const int MaxTextLength = 140;

    public static ReleaseNotesSummary Empty { get; } = new(null, [], 0);

    public bool IsEmpty => Intro is null && Items.Count == 0;

    /// <summary>
    /// One short line for a notification, e.g. "Overview dashboard, Performance History and 3 more." Empty when the
    /// notes had nothing usable.
    /// </summary>
    public string ToOneLine(int maxNames = 2)
    {
        var names = Items
            .Select(i => i.Title ?? ShortName(i.Text))
            .Where(n => n.Length > 0)
            .ToList();
        if (names.Count == 0)
        {
            return Intro is null ? string.Empty : Truncate(Intro, 120);
        }

        int shown = Math.Min(Math.Max(1, maxNames), names.Count);
        int more = names.Count - shown + MoreCount;
        string list = string.Join(", ", names.Take(shown));
        return more > 0 ? $"{list} and {more} more." : $"{list}.";
    }

    public static ReleaseNotesSummary Parse(string? markdown, int maxItems = DefaultMaxItems)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return Empty;
        }

        var blocks = ReadBlocks(markdown);
        string? intro = FindIntro(blocks);

        var bullets = blocks.OfType<BulletBlock>().ToList();
        List<(string? Category, string Text)> candidates;
        var highlights = bullets.Where(b => b.Section is { Kind: SectionKind.Highlights }).ToList();
        if (highlights.Count > 0)
        {
            candidates = highlights.Select(b => ((string?)null, b.Text)).ToList();
        }
        else
        {
            var changes = bullets.Where(b => b.Section is { Kind: SectionKind.Change }).ToList();
            candidates = changes.Count > 0
                ? changes
                    .Select((b, index) => (b, index))
                    .OrderBy(x => x.b.Section!.Priority)
                    .ThenBy(x => x.index)
                    .Select(x => ((string?)x.b.Section!.Category, x.b.Text))
                    .ToList()
                : bullets.Select(b => ((string?)null, b.Text)).ToList();
        }

        var items = new List<ReleaseNoteItem>();
        int usable = 0;
        foreach (var (category, text) in candidates)
        {
            var item = ToItem(category, text);
            if (item is null)
            {
                continue;
            }

            usable++;
            if (items.Count < Math.Max(0, maxItems))
            {
                items.Add(item);
            }
        }

        return new ReleaseNotesSummary(intro, items, usable - items.Count);
    }

    // ---- Block reader --------------------------------------------------------------------------------

    private enum SectionKind
    {
        Other,
        Highlights,
        Change,
    }

    private sealed record Section(SectionKind Kind, string? Category, int Priority, int Level);

    private abstract record Block(Section? Section);

    private sealed record ParagraphBlock(Section? Section, bool BeforeAnySection, string Text) : Block(Section);

    private sealed record BulletBlock(Section? Section, string Text) : Block(Section);

    private sealed record HeadingBlock(Section? Section) : Block(Section);

    private static List<Block> ReadBlocks(string markdown)
    {
        var blocks = new List<Block>();
        string[] lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        Section? section = null;
        bool seenSectionHeading = false;
        bool inFence = false;
        bool inComment = false;
        var paragraph = new StringBuilder();
        StringBuilder? bullet = null;
        bool inNested = false;

        void FlushParagraph()
        {
            if (paragraph.Length > 0)
            {
                blocks.Add(new ParagraphBlock(section, !seenSectionHeading, paragraph.ToString()));
                paragraph.Clear();
            }
        }

        void FlushBullet()
        {
            if (bullet is not null)
            {
                blocks.Add(new BulletBlock(section, bullet.ToString()));
                bullet = null;
            }

            inNested = false;
        }

        foreach (string raw in lines)
        {
            string line = raw.TrimEnd();
            string trimmed = line.TrimStart();
            int indent = CountIndent(line);

            if (inComment)
            {
                if (trimmed.Contains("-->", StringComparison.Ordinal))
                {
                    inComment = false;
                }

                continue;
            }

            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                FlushParagraph();
                FlushBullet();
                inFence = !inFence;
                continue;
            }

            if (inFence)
            {
                continue;
            }

            if (trimmed.StartsWith("<!--", StringComparison.Ordinal))
            {
                FlushParagraph();
                FlushBullet();
                inComment = !trimmed.Contains("-->", StringComparison.Ordinal);
                continue;
            }

            if (trimmed.Length == 0)
            {
                FlushParagraph();
                FlushBullet();
                continue;
            }

            // A horizontal rule ends the notes: the release workflow appends its download footer after one.
            if (indent < 4 && HorizontalRule().IsMatch(trimmed))
            {
                FlushParagraph();
                FlushBullet();
                break;
            }

            var heading = HeadingLine().Match(trimmed);
            if (indent < 4 && heading.Success)
            {
                FlushParagraph();
                FlushBullet();
                int level = heading.Groups[1].Value.Length;
                string title = Clean(heading.Groups[2].Value);
                if (level <= 2 && section is null && !seenSectionHeading && LooksLikeVersionHeading(title))
                {
                    continue; // "## [1.2.0] - 2026-10-01": the release itself, not a section
                }

                seenSectionHeading = true;
                section = ClassifySection(title, level);
                blocks.Add(new HeadingBlock(section));
                continue;
            }

            if (trimmed.StartsWith('|') || trimmed.StartsWith('>'))
            {
                FlushParagraph();
                FlushBullet();
                continue; // tables and quotes are not summary material
            }

            var marker = BulletMarker().Match(trimmed);
            if (marker.Success)
            {
                FlushParagraph();
                if (indent >= 2 && bullet is not null)
                {
                    inNested = true; // a sub-point of the current bullet: skipped
                    continue;
                }

                if (indent >= 2)
                {
                    continue; // nested bullet without a parent we kept
                }

                FlushBullet();
                bullet = new StringBuilder(trimmed[marker.Length..]);
                continue;
            }

            if (bullet is not null)
            {
                if (!inNested)
                {
                    bullet.Append(' ').Append(trimmed);
                }

                continue;
            }

            if (paragraph.Length > 0)
            {
                paragraph.Append(' ');
            }

            paragraph.Append(trimmed);
        }

        FlushParagraph();
        FlushBullet();
        return blocks;
    }

    private static string? FindIntro(List<Block> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case ParagraphBlock { BeforeAnySection: true } p:
                    string text = FirstSentences(Clean(p.Text), MaxIntroLength);
                    return text.Length > 0 ? text : null;
                case BulletBlock or HeadingBlock or ParagraphBlock:
                    return null;
            }
        }

        return null;
    }

    private static Section ClassifySection(string title, int level)
    {
        string key = new string(title.ToLowerInvariant().Where(c => char.IsLetter(c) || c == ' ').ToArray()).Trim();
        key = MultipleSpaces().Replace(key, " ");
        return key switch
        {
            "highlights" or "highlight" or "summary" or "whats new" or "what is new" or "whats changed" or "tldr" or "key changes" or "in this release"
                => new Section(SectionKind.Highlights, null, 0, level),
            "security" => new Section(SectionKind.Change, "Security", 0, level),
            "added" or "new" or "features" or "new features" => new Section(SectionKind.Change, "New", 1, level),
            "changed" or "improved" or "improvements" or "enhancements" or "updated" or "performance"
                => new Section(SectionKind.Change, "Improved", 2, level),
            "fixed" or "fixes" or "bug fixes" or "bugfixes" => new Section(SectionKind.Change, "Fixed", 3, level),
            "removed" => new Section(SectionKind.Change, "Removed", 4, level),
            "deprecated" => new Section(SectionKind.Change, "Deprecated", 5, level),
            _ => new Section(SectionKind.Other, null, 9, level),
        };
    }

    private static bool LooksLikeVersionHeading(string title) =>
        VersionHeading().IsMatch(title) || title.Equals("Unreleased", StringComparison.OrdinalIgnoreCase);

    // ---- Items ---------------------------------------------------------------------------------------

    private static ReleaseNoteItem? ToItem(string? category, string rawText)
    {
        string text = rawText.Trim();
        if (text.Length == 0)
        {
            return null;
        }

        var lead = BoldLead().Match(text);
        if (lead.Success)
        {
            string title = TrimTrailingPunctuation(Clean(lead.Groups["title"].Value));
            string rest = lead.Groups["rest"].Value.Trim();
            bool separated = lead.Groups["sep"].Success || title.Length > 0 && ".:!?".Contains(Clean(lead.Groups["title"].Value).TrimEnd()[^1]);
            if (title.Length > 0 && (rest.Length == 0 || separated))
            {
                string detail = rest.Length == 0 ? string.Empty : FirstSentences(Clean(rest), MaxTextLength, single: true);
                return detail.Length == 0
                    ? new ReleaseNoteItem(category, null, EndSentence(title))
                    : new ReleaseNoteItem(category, title, EndSentence(detail));
            }
        }

        string sentence = FirstSentences(Clean(text), MaxTextLength, single: true);
        return sentence.Length == 0 ? null : new ReleaseNoteItem(category, null, EndSentence(sentence));
    }

    private static string EndSentence(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return text;
        }

        if (text.EndsWith(':') || text.EndsWith(';') || text.EndsWith(','))
        {
            text = text[..^1].TrimEnd();
        }

        return text.EndsWith('.') || text.EndsWith('!') || text.EndsWith('?') || text.EndsWith('…') ? text : text + ".";
    }

    private static string TrimTrailingPunctuation(string text) => text.TrimEnd('.', ':', ';', ',', ' ', '—', '–', '-');

    /// <summary>A few words naming an untitled item for a one-line notification ("Renamed from Vigil to Coreloom").</summary>
    private static string ShortName(string text)
    {
        string first = FirstSentences(text, MaxTextLength, single: true).TrimEnd('…');
        int cut = first.Length;
        foreach (string separator in new[] { ", ", ": ", "; ", " — ", " – ", " (" })
        {
            int at = first.IndexOf(separator, StringComparison.Ordinal);
            if (at > 0 && at < cut)
            {
                cut = at;
            }
        }

        string name = first[..cut];
        if (name.Length > 48)
        {
            int space = name.LastIndexOf(' ', 47);
            name = space > 16 ? name[..space] : name[..47];
        }

        return TrimTrailingPunctuation(name);
    }

    // ---- Text helpers --------------------------------------------------------------------------------

    /// <summary>The first sentence (or as many whole sentences as fit), cut at a word boundary when too long.</summary>
    internal static string FirstSentences(string text, int maxLength, bool single = false)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return text;
        }

        int end = 0;
        int searchFrom = 0;
        while (true)
        {
            int next = FindSentenceEnd(text, searchFrom);
            if (next < 0)
            {
                if (end == 0)
                {
                    end = text.Length;
                }

                break;
            }

            if (next > maxLength && end > 0)
            {
                break;
            }

            end = next;
            searchFrom = next;
            if (single || next >= maxLength)
            {
                break;
            }
        }

        string result = text[..end].Trim();
        return Truncate(result, maxLength);
    }

    internal static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        int cut = text.LastIndexOf(' ', Math.Max(0, maxLength - 1));
        if (cut < maxLength / 2)
        {
            cut = maxLength - 1;
        }

        return text[..cut].TrimEnd(' ', ',', ';', ':', '—', '–', '-', '(') + "…";
    }

    /// <summary>Index just past the sentence-ending punctuation that starts at or after <paramref name="from"/>, or -1.</summary>
    private static int FindSentenceEnd(string text, int from)
    {
        for (int i = from; i < text.Length; i++)
        {
            char c = text[i];
            if (c is not ('.' or '!' or '?'))
            {
                continue;
            }

            bool atEnd = i == text.Length - 1;
            if (!atEnd && !(char.IsWhiteSpace(text[i + 1]) && i + 2 < text.Length && (char.IsUpper(text[i + 2]) || char.IsDigit(text[i + 2]) || text[i + 2] is '"' or '“' or '(')))
            {
                continue;
            }

            if (c == '.' && IsAbbreviation(text, i))
            {
                continue;
            }

            return i + 1;
        }

        return -1;
    }

    private static bool IsAbbreviation(string text, int dot)
    {
        int start = dot;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '.'))
        {
            start--;
        }

        string word = text[start..dot].ToLowerInvariant();
        return word is "e.g" or "i.e" or "etc" or "vs" or "approx" or "incl" or "no" or "mr" or "ms" or "dr" || (word.Length == 1 && char.IsLetter(word[0]));
    }

    /// <summary>Markdown inline syntax → plain text (links keep their text, code keeps its content).</summary>
    internal static string Clean(string markdown)
    {
        string s = markdown;
        s = Image().Replace(s, string.Empty);
        s = Link().Replace(s, "$1");
        s = AutoLink().Replace(s, "$1");
        s = HtmlTag().Replace(s, string.Empty);
        s = InlineCode().Replace(s, "$1");
        s = s.Replace("**", string.Empty, StringComparison.Ordinal).Replace("__", string.Empty, StringComparison.Ordinal);
        s = Emphasis().Replace(s, "$1");
        s = Escape().Replace(s, "$1");
        s = s.Replace("&nbsp;", " ", StringComparison.Ordinal).Replace("&amp;", "&", StringComparison.Ordinal)
             .Replace("&lt;", "<", StringComparison.Ordinal).Replace("&gt;", ">", StringComparison.Ordinal);
        s = MultipleSpaces().Replace(s, " ");
        return s.Trim();
    }

    private static int CountIndent(string line)
    {
        int count = 0;
        foreach (char c in line)
        {
            if (c == ' ')
            {
                count++;
            }
            else if (c == '\t')
            {
                count += 4;
            }
            else
            {
                break;
            }
        }

        return count;
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.*?)\s*#*$")]
    private static partial Regex HeadingLine();

    [GeneratedRegex(@"^(?:[-*+]|\d{1,3}[.)])\s+")]
    private static partial Regex BulletMarker();

    [GeneratedRegex(@"^(?:(?:-\s*){3,}|(?:\*\s*){3,}|(?:_\s*){3,})$")]
    private static partial Regex HorizontalRule();

    [GeneratedRegex(@"^\[?v?\d+\.\d+(?:\.\d+)?(?:-[0-9A-Za-z.-]+)?\]?(?:\s*[-–—(].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionHeading();

    [GeneratedRegex(@"^(?:\*\*|__)(?<title>.+?)(?:\*\*|__)\s*(?<sep>[.:—–](?:\s+|$)|-\s+)?(?<rest>.*)$")]
    private static partial Regex BoldLead();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"<(https?://[^>\s]+)>")]
    private static partial Regex AutoLink();

    [GeneratedRegex(@"</?[A-Za-z][^>]*>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"`+([^`]+)`+")]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"(?<![\w*])[*_]([^*_\s](?:[^*_]*[^*_\s])?)[*_](?![\w*])")]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"\\([\\`*_{}\[\]()#+\-.!>|])")]
    private static partial Regex Escape();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultipleSpaces();
}
