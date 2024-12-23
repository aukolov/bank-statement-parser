using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BankStatementParser.Extensions;
using Pdf2Text;

namespace BankStatementParser.Banks
{
    public class FibankFileProcessor : IFileProcessor
    {
        private readonly PdfParser _pdfParser = new PdfParser();
        private readonly Regex _accountNumberRegex = new Regex(@"^\w{2}\d{10,}$");
        private readonly Regex _dateRegex = new Regex(@"^\d{2}/\d{2}/\d{4}$");
        private readonly Regex _amountRegex = new Regex(@"^(\d{1,3})(,\d{3})*\.\d{2}$");

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

            var state = State.SearchAccountNumber;
            Transaction currentTrxn = null;
            var currentBalance = 0m;
            var statement = new Statement
            {
                Transactions = new List<Transaction>()
            };

            var transactionDateText = "";
            double? previousDescriptionBottom = null;

            foreach (var page in pdfModel.Pages)
            {
                var nextPage = false;

                for (var i = 0; i < page.Sentences.Count - 1 && !nextPage; i++)
                {
                    var s = page.Sentences[i];

                    var next = page.Sentences[i + 1];
                    var nextNext = i < page.Sentences.Count - 2 ? page.Sentences[i + 2] : null; 
                    switch (state)
                    {
                        case State.SearchAccountNumber:
                            if (s.Text == "Account" && s.Left.IsApproximately(65))
                            {
                                if (!_accountNumberRegex.IsMatch(next.Text))
                                    throw new Exception(
                                        $"Account number was expected to be numeric but was {next.Text}.");
                                statement.AccountNumber = next.Text;
                                state = State.SearchStatementPeriod;
                            }

                            break;
                        case State.SearchStatementPeriod:
                            if (s.Text == "Period:")
                            {
                                var periodMatch = Regex.Match(next.Text,
                                    @"(?<from>\d{2}/\d{2}/\d{4}) - (?<to>\d{2}/\d{2}/\d{4})");
                                var fromText = periodMatch.Groups["from"].Captures[0].Value;
                                var toText = periodMatch.Groups["to"].Captures[0].Value;
                                statement.FromDate = DateTime.ParseExact(fromText, "dd/MM/yyyy",
                                    CultureInfo.InvariantCulture);
                                statement.ToDate =
                                    DateTime.ParseExact(toText, "dd/MM/yyyy", CultureInfo.InvariantCulture);
                                state = State.SearchOpeningBalance;
                            }

                            break;
                        case State.SearchOpeningBalance:
                            if (s.Text == "Opening balance:")
                            {
                                currentBalance = ParseAmount(nextNext.Text) - ParseAmount(next.Text);
                                state = State.ScrollToTable;
                            }

                            break;
                        case State.ScrollToTable:
                            if (s.Text == "explanation"
                                && s.Left.IsApproximately(454))
                            {
                                currentTrxn = new Transaction();
                                state = State.SearchTrxn;
                            }

                            break;
                        case State.SearchTrxn:
                            if (s.PageIndex > 0 && i == 0)
                            {
                                previousDescriptionBottom = s.Bottom;
                            }
                            if (IsDescription(s) && previousDescriptionBottom != null &&
                                s.Top - previousDescriptionBottom > 12)
                            {
                                if (transactionDateText.Length > 0)
                                {
                                    currentTrxn.Date = DateTime.ParseExact(transactionDateText, "dd/MM/yyyy",
                                        CultureInfo.InvariantCulture);
                                }

                                statement.Transactions.Add(currentTrxn);
                                currentTrxn = new Transaction();
                                transactionDateText = "";
                                previousDescriptionBottom = s.Bottom;
                            }

                            if (s.Text == "Total debits and")
                            {
                                currentTrxn.Date = DateTime.ParseExact(transactionDateText, "dd/MM/yyyy",
                                    CultureInfo.InvariantCulture);
                                statement.Transactions.Add(currentTrxn);
                                state = State.SearchClosingBalance;
                            }
                            else if (s.HorizontalCenter().IsApproximately(85))
                            {
                                transactionDateText += s.Text;
                            }
                            else if (s.Right.IsApproximately(212) && s.Text != "0.00")
                            {
                                var value = -decimal.Parse(s.Text, CultureInfo.InvariantCulture);
                                currentTrxn.Amount = value;
                                currentBalance += value;
                            }
                            else if (s.Right.IsApproximately(275) && s.Text != "0.00")
                            {
                                var value = ParseAmount(s.Text);
                                currentTrxn.Amount = value;
                                currentBalance += value;
                            }
                            else if (IsDescription(s))
                            {
                                currentTrxn.Description +=
                                    (currentTrxn.Description is { Length: > 0 }
                                        ? " "
                                        : "") + s.Text;
                                previousDescriptionBottom = s.Bottom;
                            }

                            break;
                        case State.SearchClosingBalance:
                            if (s.Text == "Closing balance:")
                            {
                                var closingBalance = ParseAmount(nextNext.Text) - ParseAmount(next.Text);
                                if (currentBalance != closingBalance)
                                {
                                    throw new Exception(
                                        $"Closing balance mismatch: {currentBalance} vs {closingBalance}.");
                                }       
                            }
                            
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }
            
            return statement;
        }

        private static decimal ParseAmount(string text)
        {
            return decimal.Parse(text, CultureInfo.InvariantCulture);
        }

        private static bool IsDescription(SentenceModel s)
        {
            return s.Left.IsApproximately(284);
        }

        enum State
        {
            SearchAccountNumber,
            SearchStatementPeriod,
            SearchOpeningBalance,
            ScrollToTable,
            SearchTrxn,
            SearchClosingBalance,
        }
    }
}