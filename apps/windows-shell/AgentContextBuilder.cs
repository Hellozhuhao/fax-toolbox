namespace FaxToolbox.Windows;

internal static class AgentContextBuilder
{
    // Keep the current request intact. Recent tool results take priority over older chat.
    internal static string Build(IEnumerable<string> history, string currentRequest,
        IEnumerable<string> toolSteps, int maxCharacters)
    {
        if (maxCharacters < currentRequest.Length) throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        var remaining = maxCharacters - currentRequest.Length;
        var toolBlocks = toolSteps.ToArray();
        var toolBudget = toolBlocks.Length == 0 ? 0 : remaining - Math.Min(1800, remaining / 3);
        var tools = SelectRecent(toolBlocks, ref toolBudget, true);
        remaining -= tools.Sum(block => block.Length);
        var prior = SelectRecent(history, ref remaining, true);
        return string.Concat(prior) + currentRequest + string.Concat(tools);
    }

    private static List<string> SelectRecent(IEnumerable<string> source, ref int remaining,
        bool allowNewestTruncation)
    {
        var chosen = new List<string>();
        foreach (var block in source.Reverse())
        {
            if (remaining == 0) break;
            if (block.Length > remaining && (!allowNewestTruncation || chosen.Count > 0)) break;
            var fit = block.Length <= remaining ? block : block[..remaining];
            chosen.Add(fit);
            remaining -= fit.Length;
            if (fit.Length < block.Length) break;
        }
        chosen.Reverse();
        return chosen;
    }
}
