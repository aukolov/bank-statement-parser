using CommandLine;
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace BankStatementParser
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class Options
    {
        [Option('p', "path", Required = true, HelpText = "Path to a PDF BoC statement file or folder containing statement files.")]
        public string Path { get; set; }
    }
}