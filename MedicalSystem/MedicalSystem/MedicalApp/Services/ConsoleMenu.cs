namespace MedicalApp.Services;

public static class ConsoleMenu
{
    public static void PrintHeader(string title)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"╔══════════════════════════════════════╗");
        Console.WriteLine($"║  {title,-36}║");
        Console.WriteLine($"╚══════════════════════════════════════╝");
        Console.ResetColor();
    }

    public static void PrintSuccess(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ {msg}");
        Console.ResetColor();
    }

    public static void PrintError(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"✗ {msg}");
        Console.ResetColor();
    }

    public static void PrintInfo(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"→ {msg}");
        Console.ResetColor();
    }

    public static string ReadRequired(string prompt)
    {
        while (true)
        {
            Console.Write($"  {prompt}: ");
            var val = Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(val)) return val;
            PrintError("Polje je obavezno.");
        }
    }

    public static string? ReadOptional(string prompt)
    {
        Console.Write($"  {prompt} (Enter za preskočiti): ");
        var val = Console.ReadLine()?.Trim();
        return string.IsNullOrEmpty(val) ? null : val;
    }

    public static DateTime ReadDate(string prompt)
    {
        while (true)
        {
            Console.Write($"  {prompt} (dd.MM.yyyy): ");
            var input = Console.ReadLine()?.Trim();
            if (DateTime.TryParseExact(input, "dd.MM.yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
                return dt;
            PrintError("Neispravan format datuma. Koristite dd.MM.yyyy.");
        }
    }

    public static int ReadInt(string prompt)
    {
        while (true)
        {
            Console.Write($"  {prompt}: ");
            if (int.TryParse(Console.ReadLine(), out var val)) return val;
            PrintError("Molimo unesite cijeli broj.");
        }
    }

    public static int SelectFromList<T>(List<T> items, Func<T, string> display, string prompt)
    {
        for (int i = 0; i < items.Count; i++)
            Console.WriteLine($"  [{i + 1}] {display(items[i])}");

        while (true)
        {
            var idx = ReadInt(prompt) - 1;
            if (idx >= 0 && idx < items.Count) return idx;
            PrintError("Nevažeći odabir.");
        }
    }
}
