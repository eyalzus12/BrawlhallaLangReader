using System.IO;

namespace BrawlhallaLangReader;

internal class Sample
{
    public static void ExtractStringTable(string fromPath, string outputPath)
    {
        LangFile file;
        using (FileStream stream = new(fromPath, FileMode.Open, FileAccess.Read))
            file = LangFile.Load(stream);

        using FileStream outStream = new(outputPath, FileMode.Create, FileAccess.Write);
        using StreamWriter writer = new(outStream);
        foreach ((string key, string text) in file.Entries)
        {
            writer.Write($"{key}\t{text}");
        }
    }
}