using Spectre.Console;

namespace OpenKey.Ui;

/// <summary>
/// Interactive prompts that degrade instead of throwing.
/// <para>
/// Spectre's prompts raise <see cref="NotSupportedException"/> when the terminal is not
/// interactive — which is true whenever any standard stream is redirected, so it happens for
/// `openkey &lt; script.txt`, for piped input, and inside CI. Nothing caught it, so a piped
/// <c>/models</c> killed the app with a raw exception name on screen.
/// </para>
/// <para>
/// Every fallback here reads a plain line instead, which is exactly what a piped caller can
/// provide. Destructive confirmations deliberately require an explicit word rather than accepting
/// a bare newline.
/// </para>
/// </summary>
internal static class Prompts
{
    /// <summary>True when Spectre's own prompts are safe to use.</summary>
    public static bool CanPrompt => ConsoleLayout.Rich;

    public static bool Confirm(string question, bool defaultValue = false)
    {
        if (CanPrompt)
        {
            try { return AnsiConsole.Confirm(question, defaultValue); }
            catch (NotSupportedException) { /* fall through */ }
        }

        AnsiConsole.Markup($"{Markup.Escape(question)} [{Theme.Muted}](yes/no)[/] ");
        var answer = Console.ReadLine();
        if (Console.IsInputRedirected) AnsiConsole.WriteLine();

        // No answer means no. A destructive default of "yes" on EOF would be indefensible.
        return answer?.Trim().ToLowerInvariant() is "y" or "yes";
    }

    /// <summary>
    /// Presents a choice. Returns the selected item, or null when the user declined or nothing
    /// could be read.
    /// </summary>
    public static string? Select(string title, IReadOnlyList<string> choices)
    {
        if (choices.Count == 0) return null;

        if (CanPrompt)
        {
            try
            {
                var prompt = new SelectionPrompt<string>
                {
                    Title = Components.PickerTitle(title),
                    PageSize = 12,
                    MoreChoicesText = $"[{Theme.Muted}]More below[/]",
                };
                foreach (var c in choices) prompt.AddChoice(c);
                return AnsiConsole.Prompt(prompt);
            }
            catch (NotSupportedException) { /* fall through */ }
        }

        // Numbered list is the only sane fallback: a piped caller cannot press arrow keys.
        AnsiConsole.MarkupLine(Markup.Escape(title));
        for (var i = 0; i < choices.Count; i++)
            AnsiConsole.MarkupLine($"  [{Theme.Brand}]{i + 1}[/]  {Markup.Escape(choices[i])}");

        Components.HintLine("Type a number and press Enter, or press Enter to cancel.");
        AnsiConsole.Markup($"[{Theme.Brand}]#[/] ");

        var line = Console.ReadLine();
        if (Console.IsInputRedirected) AnsiConsole.WriteLine();

        return int.TryParse(line?.Trim(), out var pick) && pick >= 1 && pick <= choices.Count
            ? choices[pick - 1]
            : null;
    }

    /// <summary>
    /// Reads a secret. Masked when the terminal allows it; when it does not, the value is coming
    /// from a pipe and there is nothing on screen to hide.
    /// </summary>
    public static string? Secret(string label)
    {
        if (CanPrompt)
        {
            try
            {
                return AnsiConsole.Prompt(
                    new TextPrompt<string>(label)
                        .Secret()
                        .Validate(k => string.IsNullOrWhiteSpace(k)
                            ? ValidationResult.Error($"[{Theme.Danger}]Paste a key to continue, or press Ctrl+C to go back.[/]")
                            : ValidationResult.Success()));
            }
            catch (NotSupportedException) { /* fall through */ }
        }

        AnsiConsole.Markup(Markup.Escape(label));
        var value = Console.ReadLine();
        if (Console.IsInputRedirected) AnsiConsole.WriteLine();

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
