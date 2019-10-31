using System;
using System.IO;
using CommandLine;

namespace BocStatementParser
{
    class Program
    {
        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
                .WithParsed<Options>(o =>
                {
                    var fileProcessor = new FileProcessor();
                    var result = fileProcessor.Process(o.Path);
                    File.WriteAllText(
                        $"statement_gen{DateTime.Now:yyyyMMdd-hhmmss}.csv",
                        result);
                });
        }
    }
}
