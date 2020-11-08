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
    public class HellenicFileProcessor : IFileProcessor
    {
        private readonly PdfParser _pdfParser = new PdfParser();
        private readonly Regex _accountNumberRegex = new Regex(@"^\d+-\d+-\d+-\d+$");
        private readonly Regex _dateRegex = new Regex(@"^\d{2}/\d{2}/\d{4}$");
        private readonly Regex _amountRegex = new Regex(@"^(\d{1,3})(\.\d{3})*\,\d{2}$");
        private readonly CultureInfo _numberCultureInfo;

        public HellenicFileProcessor()
        {
            _numberCultureInfo = (CultureInfo) CultureInfo.InvariantCulture.Clone();
            _numberCultureInfo.NumberFormat.NumberDecimalSeparator = ",";
            _numberCultureInfo.NumberFormat.NumberGroupSeparator = ".";
        }

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

            Transaction currentTrxn = null;
            var currentBalance = 0m;
            var statement = new Statement
            {
                Transactions = new List<Transaction>()
            };

            foreach (var page in pdfModel.Pages)
            {
                var nextPage = false;
                var state = page.PageNumber == 0 ? State.SearchAccountNumber : State.ScrollToTable;

                for (var i = 0; i < page.Sentences.Count && !nextPage; i++)
                {
                    var s = page.Sentences[i];

                    var next = i + 1 < page.Sentences.Count ? page.Sentences[i + 1] : null;
                    switch (state)
                    {
                        case State.SearchAccountNumber:
                            if (s.Text == "ACCOUNT NO"
                                && s.Left.IsApproximately(272))
                            {
                                if (!_accountNumberRegex.IsMatch(next.Text))
                                    throw new Exception(
                                        $"Account number was expected to be numeric with dashes but was {next.Text}.");
                                statement.AccountNumber = next.Text;
                                state = State.SearchStatementPeriod;
                            }

                            break;
                        case State.SearchStatementPeriod:
                            if (s.Text == "STATEMENT PERIOD")
                            {
                                var periodMatch = Regex.Match(next.Text,
                                    @"(?<from>\d{2}/\d{2}/\d{4}) - (?<to>\d{2}/\d{2}/\d{4})");
                                var fromText = periodMatch.Groups["from"].Captures[0].Value;
                                var toText = periodMatch.Groups["to"].Captures[0].Value;
                                statement.FromDate = DateTime.ParseExact(fromText, "dd/MM/yyyy",
                                    CultureInfo.InvariantCulture);
                                statement.ToDate =
                                    DateTime.ParseExact(toText, "dd/MM/yyyy", CultureInfo.InvariantCulture);
                                state = State.SearchBalance;
                            }

                            break;
                        case State.SearchBalance:
                            if (s.Text == "BALANCE B/F")
                            {
                                if (!decimal.TryParse(next.Text, NumberStyles.Number, _numberCultureInfo,
                                    out currentBalance))
                                    throw new Exception(
                                        $"Account number was expected to be numeric but was {next.Text}.");

                                state = State.ScrollToTable;
                            }

                            break;
                        case State.ScrollToTable:
                            if (s.Text == "BALANCE")
                            {
                                state = State.SearchTrxn;
                            }

                            break;
                        case State.SearchTrxn:
                            var potentialTrxnDate = _dateRegex.Match(s.Text);
                            if (potentialTrxnDate.Success
                                && s.Left.IsApproximately(30))
                            {
                                if (currentTrxn != null)
                                {
                                    statement.Transactions.Add(currentTrxn);
                                }

                                currentTrxn = new Transaction
                                {
                                    Date = DateTime.ParseExact(s.Text, "dd/MM/yyyy", CultureInfo.InvariantCulture)
                                };
                                state = State.FirstDescription;
                            }
                            else if (s.Text == "TOTALS:" && s.Left.IsApproximately(186))
                            {
                                nextPage = true;
                                state = State.ScrollToTable;
                            }
                            else if (s.Left.IsApproximately(88)
                                     && currentTrxn != null)
                            {
                                currentTrxn.Description += " " + s.Text;
                            }

                            break;
                        case State.FirstDescription:
                            if (currentTrxn == null)
                                throw new Exception("Current transaction must not be null.");
                            if (currentTrxn.Description != null)
                                throw new Exception("Description was expected to be null but was not.");

                            currentTrxn.Description = s.Text;
                            state = State.Amount;
                            break;
                        case State.Amount:
                            var match = _amountRegex.Match(s.Text);
                            if (!match.Success)
                                throw new Exception($"Amount was expected but got {s.Text}.");
                            if (currentTrxn == null)
                                throw new Exception("Current transaction must not be null.");
                            if (currentTrxn.Amount.HasValue)
                                throw new Exception("Amount was expected to be null but was not.");

                            var amount = decimal.Parse(s.Text, _numberCultureInfo);
                            if (s.Right.IsApproximately(328))
                                currentTrxn.Amount = -amount;
                            else if (s.Right.IsApproximately(401))
                                currentTrxn.Amount = +amount;
                            else
                                throw new Exception($"Found amount at unexpected location: {s.Right}.");
                            state = State.ValueDate;

                            break;
                        case State.ValueDate:
                            if (!_dateRegex.IsMatch(s.Text))
                                throw new Exception($"Date was expected but got {s.Text}.");
                            state = State.Balance;
                            break;
                        case State.Balance:
                            var oldBalance = currentBalance;
                            if (!decimal.TryParse(s.Text, NumberStyles.Number, _numberCultureInfo, out currentBalance))
                                throw new Exception($"Balance was expected to be numeric but was {s.Text}.");
                            if (currentTrxn == null)
                                throw new Exception("Current transaction was not expected to be null but was.");
                            if (oldBalance + currentTrxn.Amount != currentBalance)
                                throw new Exception(
                                    $"Balance was expected to be {oldBalance + currentTrxn.Amount} but was {currentBalance}.");

                            state = State.SearchTrxn;
                            break;

                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }

            if (currentTrxn != null)
            {
                statement.Transactions.Add(currentTrxn);
            }

            return statement;
        }

        private enum State
        {
            SearchAccountNumber,
            SearchStatementPeriod,
            ScrollToTable,
            SearchBalance,
            SearchTrxn,
            ValueDate,
            FirstDescription,
            Amount,
            Balance,
        }
    }
}