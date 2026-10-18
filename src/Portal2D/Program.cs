using Portal2D.Game;
using Portal2D.Testing;

namespace Portal2D;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Contains("--test")) return TestRunner.Run(args.Contains("-v"), args.SkipWhile(a => a != "--level").Skip(1).Select(int.Parse).FirstOrDefault() - 1);
        if (args.Contains("--dump"))
        {
            for (int i = 0; i < Levels.All.Length; i++)
            {
                Console.WriteLine($"== {i + 1:00} {Levels.All[i].Name}");
                foreach (var row in Levels.All[i].Map) Console.WriteLine(row);
            }
            return 0;
        }
        new App(args).Run();
        return 0;
    }
}
