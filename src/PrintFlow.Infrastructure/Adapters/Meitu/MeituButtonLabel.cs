namespace PrintFlow.Infrastructure.Adapters.Meitu;

internal static class MeituButtonLabel
{
    // Qt can append zero-width spaces to a button caption. Only these four captions also
    // have a documented line break whose rendering varies with the recognition panel.
    internal static bool Matches(string observed, string expected)
    {
        if (observed == expected) return true;
        if (!expected.Any(char.IsLetterOrDigit)) return false;
        string label = observed.TrimEnd('\u200B');
        if (label == expected) return true;
        return expected switch
        {
            "关闭\n" => label is "关闭" or "关闭\r\n",
            "人像\n宠物" => label is "人像宠物" or "人像\r\n宠物",
            "商品\n货物" => label is "商品货物" or "商品\r\n货物",
            "图标\n印章" => label is "图标印章" or "图标\r\n印章",
            _ => false,
        };
    }
}
