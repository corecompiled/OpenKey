using System.Text;
using Spectre.Console;

namespace OpenKey.Ui;

/// <summary>
/// Startup-time console setup. Must run before anything prints: Spectre computes and caches
/// terminal capabilities on first access, so encoding has to be settled first.
/// </summary>
internal static class ConsoleLayout
{
    /// <summary>
    /// Widest line the app will draw. Capping matters more than it sounds: without it a maximized
    /// 200-column terminal turns a three-line code snippet into a 200-wide box, and a reply reads
    /// differently on every machine. 100 is a comfortable measure for prose.
    /// </summary>
    public const int MaxWidth = 100;

    /// <summary>Below this, Spectre's own wrapping takes over; no narrow layout is attempted.</summary>
    public const int MinWidth = 60;

    public static int Width { get; private set; } = MaxWidth;

    /// <summary>
    /// True when the console can be drawn on richly — ANSI available and not redirected.
    /// <para>
    /// Both halves matter. Spectre 0.55 disables ANSI whenever stdout is redirected, and makes
    /// <c>Capabilities.Interactive</c> false if <em>any</em> std stream is redirected — at which
    /// point its prompts throw. Anything that moves the cursor, clears the screen, or prompts must
    /// check this first.
    /// </para>
    /// </summary>
    public static bool Rich { get; private set; } = true;

    public static void Initialize()
    {
        // LLM replies are full of em-dashes, smart quotes and emoji; without UTF-8 they arrive as
        // '?'. Guarded because stdout may be redirected or absent.
        try { Console.OutputEncoding = Encoding.UTF8; } catch (IOException) { } catch (System.Security.SecurityException) { }

        Glyphs.Resolve();

        var caps = AnsiConsole.Profile.Capabilities;
        Rich = caps.Ansi && caps.Interactive;

        Width = MaxWidth;
        try
        {
            if (!Console.IsOutputRedirected)
                Width = Math.Clamp(Console.WindowWidth, MinWidth, MaxWidth);
        }
        catch (IOException)
        {
            // No console attached; keep the default.
        }

        AnsiConsole.Profile.Width = Width;
    }

    /// <summary>
    /// Re-reads the terminal width. Spectre reads <c>Console.WindowWidth</c> live on every access,
    /// so a resize changes new output but never reflows what is already drawn.
    /// </summary>
    public static int CurrentWidth()
    {
        try
        {
            if (Console.IsOutputRedirected) return MaxWidth;
            return Math.Clamp(Console.WindowWidth, MinWidth, MaxWidth);
        }
        catch (IOException)
        {
            return Width;
        }
    }

    /// <summary>Viewport height, used to decide whether a block can still be erased.</summary>
    public static int CurrentHeight()
    {
        try
        {
            if (Console.IsOutputRedirected) return int.MaxValue;
            return Math.Max(1, Console.WindowHeight);
        }
        catch (IOException)
        {
            return int.MaxValue;
        }
    }
}
