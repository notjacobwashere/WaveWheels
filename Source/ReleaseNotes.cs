using System.Text.RegularExpressions;
namespace WaveWheels;
internal static class ReleaseNotes
{
    // Notes remain selectable text; release content never runs HTML or loads remote images.
    internal static string PlainText(string markdown)
    {
        var text=markdown.Replace("\r\n","\n");
        text=Regex.Replace(text,@"!\[([^\]]*)\]\([^\r\n)]*\)","[Image: $1]");
        text=Regex.Replace(text,@"\[([^\]]+)\]\((https?://[^\s)]+)\)","$1 ($2)");
        text=Regex.Replace(text,@"(?m)^#{1,6}\s+","");
        text=Regex.Replace(text,@"(?m)^\s*[-*]\s+","• ");
        return text.Replace("**","").Replace("`","").Trim();
    }
}
