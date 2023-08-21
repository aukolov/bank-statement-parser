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
    public class RevolutFileProcessor : IFileProcessor
    {
        private readonly PdfParser _pdfParser = new PdfParser();
        private readonly Regex _accountNumberRegex = new Regex(@"^\w{2}\d{10,}$");
        private readonly Regex _dateRegex = new Regex(@"^\w{3} \d{1,2}, \d{4}$");
        private readonly Regex _trxnTypeRegex = new Regex(@"^\w{3}$");
        private readonly Regex _amountRegex = new Regex(@"^[$€₺£]?[A-Z]{0,3}(?<amount>(\d{1,3})(,\d{3})*\.\d{2}$)");
            
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
            decimal? currentBalance = null;
            var statement = new Statement
            {
                Transactions = new List<Transaction>()
            };

            foreach (var page in pdfModel.Pages)
            {
                var nextPage = false;

                for (var i = 0; i < page.Sentences.Count - 1 && !nextPage; i++)
                {
                    var s = page.Sentences[i];

                    var next = page.Sentences[i + 1];
                    switch (state)
                    {
                        case State.SearchAccountNumber:
                            if (s.Text == "IBAN (SEPA)"
                                && s.Left.IsApproximately(406))
                            {
                                if (!_accountNumberRegex.IsMatch(next.Text))
                                    throw new Exception(
                                        $"Account number was expected to be numeric but was {next.Text}.");
                                if (statement.AccountNumber != null && statement.AccountNumber != next.Text)
                                {
                                    throw new Exception(
                                        $"More than one account in one statement file: '{statement.AccountNumber}' and '{next.Text}'");
                                }

                                statement.AccountNumber = next.Text;
                                state = State.SearchStatementPeriod;
                            }

                            break;
                        case State.SearchStatementPeriod:
                            if (s.Text.StartsWith("Transactions from "))
                            {
                                var periodMatch = Regex.Match(s.Text,
                                    @"from (?<from>\w{3} \d{1,2}, \d{4}) to (?<to>\w{3} \d{1,2}, \d{4})");
                                var fromText = periodMatch.Groups["from"].Captures[0].Value;
                                var toText = periodMatch.Groups["to"].Captures[0].Value;
                                var from = DateTime.ParseExact(fromText, "MMM d, yyyy",
                                    CultureInfo.InvariantCulture);
                                statement.FromDate = statement.FromDate == DateTime.MinValue
                                    ? from
                                    : from < statement.FromDate
                                        ? from
                                        : statement.FromDate;
                                var to = DateTime.ParseExact(toText, "MMM d, yyyy", CultureInfo.InvariantCulture);
                                statement.ToDate = statement.ToDate == DateTime.MinValue
                                    ? to
                                    : to > statement.ToDate
                                        ? to
                                        : statement.ToDate;
                                state = State.ScrollToTable;
                            }

                            break;
                        case State.ScrollToTable:
                            if (s.Text == "Balance"
                                && s.Left.IsApproximately(531))
                            {
                                state = State.SearchTrxn;
                            }

                            break;
                        case State.SearchTrxn:
                            var potentialTrxnDate = _dateRegex.Match(s.Text);
                            if (potentialTrxnDate.Success
                                && s.Left.IsApproximately(37.5))
                            {
                                if (currentTrxn != null)
                                {
                                    statement.Transactions.Add(currentTrxn);
                                }

                                currentTrxn = new Transaction
                                {
                                    Date = DateTime.ParseExact(s.Text, "MMM d, yyyy",
                                        CultureInfo.InvariantCulture)
                                };
                                state = State.TrxnType;
                            }
                            else if (s.Text == "© 2021 Revolut Payments UAB")
                            {
                                nextPage = true;
                            }
                            else if (s.Left.IsApproximately(116)
                                     && currentTrxn != null)
                            {
                                currentTrxn.Description += " " + s.Text;
                            }
                            else if (s.Text == "Statement" && s.Left.IsApproximately(471))
                            {
                                currentBalance = null;
                                state = State.SearchAccountNumber;
                            }

                            break;
                        case State.TrxnType:
                            if (!_trxnTypeRegex.IsMatch(s.Text))
                                throw new Exception($"Transaction type was expected, but got '{s.Text}'.");
                            state = State.FirstDescription;
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

                            var amount = decimal.Parse(match.Groups["amount"].Value, CultureInfo.InvariantCulture);
                            if (s.Right.IsApproximately(453))
                                currentTrxn.Amount = -amount;
                            else if (s.Right.IsApproximately(505))
                                currentTrxn.Amount = +amount;
                            else
                                throw new Exception($"Found amount at unexpected location: {s.Right}.");
                            state = State.Balance;

                            break;
                        case State.Balance:
                            var oldBalance = currentBalance;
                            var matchBalance = _amountRegex.Match(s.Text);
                            if (!matchBalance.Success)
                                throw new Exception($"Balance was expected to be numeric but was {s.Text}.");
                            currentBalance = decimal.Parse(matchBalance.Groups["amount"].Value, CultureInfo.InvariantCulture);
                            if (currentTrxn == null)
                                throw new Exception("Current transaction was not expected to be null but was.");
                            if (oldBalance != null && oldBalance != currentBalance)
                                throw new Exception(
                                    $"Balance was expected to be {oldBalance + currentTrxn.Amount} but was {currentBalance}.");

                            currentBalance -= currentTrxn.Amount;
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

        enum State
        {
            SearchAccountNumber,
            SearchStatementPeriod,
            ScrollToTable,
            SearchTrxn,
            TrxnType,
            FirstDescription,
            Amount,
            Balance,
        }
    }
}