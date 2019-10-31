using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BocStatementParser.Extensions;
using Pdf2Text;

namespace BocStatementParser
{
    public class FileProcessor
    {
        private readonly PdfParser _pdfParser = new PdfParser();
        private readonly Regex _dateRegex = new Regex(@"^\d{2}/\d{2}/\d{4}$");
        private readonly Regex _amountRegex = new Regex(@"^(\d{1,3})(,\d{3})*\.\d{2}$");

        public string Process(string path)
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

            var lines = files.SelectMany(ProcessFile).ToArray();

            var buildResult = BuildResult(lines);
            return buildResult;
        }

        private List<Line> ProcessFile(string pdfPath)
        {
            var pdfModel = _pdfParser.Parse(pdfPath);

            var state = State.ScrollToTable;
            Line currentLine = null;
            var lines = new List<Line>();
            foreach (var page in pdfModel.Pages)
            {
                var nextPage = false;

                for (var i = 0; i < page.Sentences.Count - 1 && !nextPage; i++)
                {
                    var s = page.Sentences[i];

                    var next = page.Sentences[i + 1];
                    switch (state)
                    {
                        case State.ScrollToTable:
                            if (s.Text == "Balance"
                                && s.Left.IsApproximately(538))
                            {
                                state = State.SearchBeginning;
                            }

                            break;
                        case State.SearchBeginning:
                            var potentialTrxnDate = _dateRegex.Match(s.Text);
                            if (potentialTrxnDate.Success
                                && _dateRegex.IsMatch(next.Text)
                                && s.Left.IsApproximately(42))
                            {
                                if (currentLine != null)
                                {
                                    lines.Add(currentLine);
                                }

                                currentLine = new Line
                                {
                                    Date = DateTime.ParseExact(s.Text, "dd/MM/yyyy",
                                        CultureInfo.InvariantCulture)
                                };
                                state = State.ValueDate;
                            }
                            else if (s.Text == "Continue on next Page"
                                     || s.Text == "Total / Balance Carried Forward")
                            {
                                nextPage = true;
                                state = State.ScrollToTable;
                            }
                            else if (s.Left.IsApproximately(141)
                                     && currentLine != null)
                            {
                                currentLine.Description += " " + s.Text;
                            }

                            break;
                        case State.ValueDate:
                            if (!_dateRegex.IsMatch(s.Text))
                                throw new Exception($"Date was expected but got {s.Text}.");
                            state = State.FirstDescription;
                            break;
                        case State.FirstDescription:
                            if (currentLine == null)
                                throw new Exception("Current line must not be null.");
                            if (currentLine.Description != null)
                                throw new Exception("Description was expected to be null but was not.");

                            currentLine.Description = s.Text;
                            state = State.Amount;
                            break;
                        case State.Amount:
                            var match = _amountRegex.Match(s.Text);
                            if (!match.Success)
                                throw new Exception($"Amount was expected but got {s.Text}.");
                            if (currentLine == null)
                                throw new Exception("Current line must not be null.");
                            if (currentLine.Amount.HasValue)
                                throw new Exception("Amount was expected to be null but was not.");

                            var amount = decimal.Parse(s.Text, CultureInfo.InvariantCulture);
                            if (s.Right.IsApproximately(411))
                                currentLine.Amount = -amount;
                            else if (s.Right.IsApproximately(484))
                                currentLine.Amount = +amount;
                            else
                                throw new Exception($"Found amount at unexpected location: {s.Right}.");
                            state = State.Balance;

                            break;
                        case State.Balance:
                            state = State.SearchBeginning;
                            break;

                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }

            if (currentLine != null)
            {
                lines.Add(currentLine);
            }

            return lines;
        }

        private static string BuildResult(Line[] lines)
        {
            var result = new StringBuilder("Date,Description,Amount")
                .AppendLine();
            foreach (var line in lines)
            {
                result.Append(line.Date?.ToString("dd/MM/yyyy")).Append(",");
                if (line.Description.Contains(","))
                    result.Append("\"").Append(line.Description?.Replace("\"", "\"\"")).Append("\",");
                else
                    result.Append(line.Description).Append(",");
                result.Append(line.Amount?.ToString("G29", CultureInfo.InvariantCulture))
                    .AppendLine();
            }

            return result.ToString();
        }
    }

    enum State
    {
        ScrollToTable,
        SearchBeginning,
        ValueDate,
        FirstDescription,
        Amount,
        Balance,
    }
}