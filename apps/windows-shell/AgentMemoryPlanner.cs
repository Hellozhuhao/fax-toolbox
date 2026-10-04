namespace FaxToolbox.Windows;

internal static class AgentMemoryPlanner
{
    internal static int EstimateTokens(string value)
    {
        var ascii = 0;
        var other = 0;
        foreach (var character in value)
            if (character <= 127) ascii++; else other++;
        return (int)Math.Ceiling(ascii / 3.4 + other * 1.3);
    }

    internal static int ChooseCount(IReadOnlyList<string> activeHistory, string instructions,
        string memory, string background, string currentRequest, int contextTokens, long lastPromptTokens,
        int characterBudget = int.MaxValue)
    {
        if (activeHistory.Count <= 4) return 0;
        var estimated = EstimateTokens(instructions + memory + background + currentRequest) +
            activeHistory.Sum(EstimateTokens);
        var characters = memory.Length + background.Length + currentRequest.Length +
            activeHistory.Sum(block => block.Length);
        var threshold = (int)(contextTokens * .8);
        if (estimated < threshold && lastPromptTokens < threshold && characters <= characterBudget) return 0;
        var target = (int)(contextTokens * .48);
        var count = 0;
        while (count < activeHistory.Count - 4 &&
            (estimated > target || characters > characterBudget))
        {
            estimated -= EstimateTokens(activeHistory[count]);
            characters -= activeHistory[count].Length;
            count++;
        }
        return count;
    }
}
