using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pdf2Text;

namespace BankStatementParser.Banks.Hellenic
{
    public class HellenicFileProcessor : IFileProcessor
    {
        private readonly PdfParser _pdfParser = new();
        
        public Statement[] Process(string path)
        {
            var files = new List<string>();
            if (Directory.Exists(path))
            {
                files.AddRange(
                    Directory.GetFiles(path, "*.pdf", SearchOption.AllDirectories));
                if (!files.Any())
                    throw new Exception("No PDF files found.");
            }
            else if (File.Exists(path))
            {
                files.Add(path);
            }

            var statements = files.Select(ProcessFile).ToArray();
            return statements;
        }

        private Statement ProcessFile(string pdfPath)
        {
            var pdfModel = _pdfParser.Parse(pdfPath);
            return pdfModel.Pages[0].Sentences[0].Text == "ACCOUNT ACTIVITY"
                ? new HellenicActivityFileProcessor().ProcessFile(pdfPath)
                : new HellenicStatementFileProcessor().ProcessFile(pdfPath);
        }
    }
}