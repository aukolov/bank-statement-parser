using BankStatementParser.Banks;
using CommandLine;
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace BankStatementParser
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class Options
    {
        [Option('p', "path", Required = true, HelpText = "Path to a PDF BoC statement file or folder containing statement files.")]
        public string Path { get; set; }
        
        [Option('b', "bank", Required = true, HelpText = "One of the supported banks: BoC (Bank of Cyprus), Revolut, Hellenic")]
        public Bank Bank { get; set; }
    }
}