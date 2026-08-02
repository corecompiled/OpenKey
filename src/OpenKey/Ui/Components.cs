using System.Reflection;
using Spectre.Console;

namespace OpenKey.Ui;

internal enum Severity
{
    /// <summary>Failed and will not recover on its own.</summary>
    Danger,

    /// <summary>Recoverable, self-healing, or user-initiated destruction.</summary>
    Warn,
}

/// <summary>
/// Every visible element in the app. Nothing is styled at a call site — that is what kept the old
/// console incoherent, with three different cases and four border styles inside single methods.
/// </summary>
internal static class Components
{
    /// <summary>
    /// Display version. InformationalVersion carries a "+&lt;commit sha&gt;" suffix that the SDK appends;
    /// it is noise on a banner a non-technical user reads, so it is trimmed off.
    /// </summary>
    public static string Version { get; } = BuildVersion();

    private static string BuildVersion()
    {
        var raw = typeof(Components).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(Components).Assembly.GetName().Version?.ToString()
            ?? "dev";

        var plus = raw.IndexOf('+', StringComparison.Ordinal);
        return plus > 0 ? raw[..plus] : raw;
    }

    public static void Banner()
    {
        AnsiConsole.Write(
            new Rule($"[{Theme.BrandStrong}]OpenKey[/] [{Theme.Muted}]v{Markup.Escape(Version)}[/]")
                .LeftJustified()
                .RuleStyle(Theme.Muted));
        AnsiConsole.MarkupLine($"[{Theme.Muted}]Developed by Paolo Patron[/]");
        AnsiConsole.WriteLine();
    }

    /// <summary>The only thing that clears the screen.</summary>
    public static void HomeHeader()
    {
        if (ConsoleLayout.Rich)
        {
            try { AnsiConsole.Clear(); }
            catch (IOException) { /* handle isn't a console */ }
        }

        Banner();
        HintPanel();
        AnsiConsole.WriteLine();
    }

    private static void HintPanel()
    {
        var body = new Markup(
            "Type a message and press Enter to chat.\n" +
            "\n" +
            $"[{Theme.Brand}]/models[/]   Choose which AI model answers you\n" +
            $"[{Theme.Brand}]/new[/]      Start a fresh conversation\n" +
            $"[{Theme.Brand}]/help[/]     See everything OpenKey can do\n" +
            $"[{Theme.Brand}]/quit[/]     Close OpenKey");

        AnsiConsole.Write(new Panel(body)
            .Header($"[{Theme.BrandStrong}] Getting started [/]")
            .Border(Glyphs.Box)
            .BorderColor(Color.Grey)
            .Expand()
            .Padding(1, 1, 1, 1));
    }

    /// <summary>
    /// Identity line for a reply. Printed the moment the request goes out — the model is known
    /// before the first byte, so there is no reason to make the user stare at nothing.
    /// </summary>
    public static void ReplyHeader(string modelId, TimeSpan elapsed)
    {
        AnsiConsole.MarkupLine(
            $"[{Theme.BrandStrong}]OpenKey AI[/][{Theme.Muted}]  {Glyphs.Sep}  {Markup.Escape(modelId)}  {Glyphs.Sep}  {FormatElapsed(elapsed)}[/]");
        AnsiConsole.WriteLine();
    }

    public static string FormatElapsed(TimeSpan t) =>
        t.TotalSeconds < 60
            ? $"{t.TotalSeconds:0.0}s"
            : $"{(int)t.TotalMinutes}m {t.Seconds}s";

    /// <summary>
    /// One quiet line noting that rotation happened. Rotation working correctly is not a warning,
    /// so this is grey rather than yellow, mentions no model id (the header already carries it),
    /// and never leaks a ChatErrorKind name.
    /// </summary>
    public static void RotationNote(int skipped)
    {
        if (skipped <= 0) return;
        var word = skipped == 1 ? "model" : "models";
        AnsiConsole.MarkupLine($"[{Theme.Muted}]Moved past {skipped} busy {word}.[/]");
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// The single error surface. Body is always two paragraphs: what happened, then what to do.
    /// A card with no next step is a bug — it leaves the user at a dead end.
    /// </summary>
    public static void StatusCard(Severity severity, string title, string detail, string nextStep)
    {
        var (border, titleStyle) = severity switch
        {
            Severity.Warn => (Color.Yellow, $"bold {Theme.Warn}"),
            _ => (Color.Red, $"bold {Theme.Danger}"),
        };

        var body = new Markup(
            $"[{Theme.Muted}]{Markup.Escape(detail)}[/]\n\n{nextStep}");

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(body)
            .Header($"[{titleStyle}] {Markup.Escape(title)} [/]")
            .Border(Glyphs.Box)
            .BorderColor(border)
            .Expand()
            .Padding(1, 1, 1, 1));
        AnsiConsole.WriteLine();
    }

    /// <summary>General-purpose "here is what to do" line.</summary>
    public static void HintLine(string markup) =>
        AnsiConsole.MarkupLine($"[{Theme.Muted}]{markup}[/]");

    /// <summary>The glyph carries the signal; the sentence stays readable at default foreground.</summary>
    public static void SuccessLine(string sentence) =>
        AnsiConsole.MarkupLine($"[{Theme.Ok}]{Glyphs.Ok}[/] {Markup.Escape(sentence)}");

    public static void KeyValuePanel(string header, IReadOnlyList<(string Label, string Value)> rows)
    {
        var grid = new Grid()
            .AddColumn(new GridColumn().NoWrap().PadRight(3).Width(16))
            .AddColumn();

        foreach (var (label, value) in rows)
            grid.AddRow($"[{Theme.Muted}]{Markup.Escape(label)}[/]", Markup.Escape(value));

        AnsiConsole.Write(new Panel(grid)
            .Header($"[{Theme.BrandStrong}] {Markup.Escape(header)} [/]")
            .Border(Glyphs.Box)
            .BorderColor(Color.Grey)
            .Expand()
            .Padding(1, 1, 1, 1));
    }

    /// <summary>
    /// Picker title carries its own hint: Spectre owns every row below the title, so a hint printed
    /// afterwards is impossible.
    /// </summary>
    public static string PickerTitle(string question) =>
        $"{question}[{Theme.Muted}]   (Up/Down to move, Enter to choose)[/]";
}
