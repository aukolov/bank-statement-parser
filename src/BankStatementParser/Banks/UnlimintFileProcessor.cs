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
    public class UnlimintFileProcessor : IFileProcessor
    {
        private readonly PdfParser _pdfParser = new PdfParser();
        private readonly Regex _accountNumberRegex = new Regex(@"^\w{2}\d\{2}( \d{4}){6}$");
        private readonly Regex _trxnTypeRegex = new Regex(@"^\w{3}$");
        private readonly Regex _amountRegex = new Regex(@"^(?<amount>(\d{1,3})([, ]\d{3})*\.\d{2}$)");

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

            var statements = files.SelectMany(ProcessFile).ToArray();
            return statements;
        }

        private Statement[] ProcessFile(string pdfPath)
        {
            var pdfModel = _pdfParser.Parse(pdfPath);

            var state = State.SearchAccountNumber;
            Transaction currentTrxn = null;
            decimal? currentBalance = null;
            var statements = new List<Statement>();
            Statement statement = null;

            var valueDateLeft = 0d;
            var paymentDetailsLeft = 0d;
            var beneficiaryLeft = 0d;
            var debitRight = 0d;
            var creditRight = 0d;

            foreach (var page in pdfModel.Pages)
            {
                var nextPage = false;
                if (page.Sentences.Count > 0 && page.Sentences[0].Text == "Customer Account")
                {
                    if (currentTrxn != null && statement != null)
                    {
                        statement.Transactions.Add(currentTrxn);
                    }
                    statement = new Statement
                    {
                        Transactions = new List<Transaction>()
                    };
                    currentTrxn = null;
                    currentBalance = null;
                    debitRight = 0;
                    creditRight = 0;
                    state = State.SearchAccountNumber;
                }

                for (var i = 0; i < page.Sentences.Count - 1 && !nextPage; i++)
                {
                    var s = page.Sentences[i];
                    Console.WriteLine(Math.Round(s.Top).ToString().PadLeft(10, ' ' ) + s.Text + " " + s.Left);

                    var next = page.Sentences[i + 1];
                    switch (state)
                    {
                        case State.SearchAccountNumber:
                            if ((s.Text == "Customer Account")
                                && s.Left.IsApproximately(406))
                            {
                                if (!_accountNumberRegex.IsMatch(next.Text))
                                    throw new Exception(
                                        $"Account number has unexpected format: {next.Text}.");

                                statement.AccountNumber += "-" + next.Text;
                                state = State.SearchStatementPeriod;
                            }

                            break;
                        case State.SearchStatementPeriod:
                            if (s.Text.StartsWith("Period"))
                            {
                                var periodMatch = Regex.Match(s.Text,
                                    @"(?<from>.+) - (?<to>.+)");
                                var fromText = periodMatch.Groups["from"].Captures[0].Value;
                                var toText = periodMatch.Groups["to"].Captures[0].Value;
                                var from = DateTime.ParseExact(fromText, "dd.MM.yyyy", CultureInfo.InvariantCulture);
                                statement.FromDate = statement.FromDate == DateTime.MinValue
                                    ? from
                                    : from < statement.FromDate
                                        ? from
                                        : statement.FromDate;
                                var to = DateTime.ParseExact(toText, "dd.MM.yyyy", CultureInfo.InvariantCulture);
                                statement.ToDate = statement.ToDate == DateTime.MinValue
                                    ? to
                                    : to > statement.ToDate
                                        ? to
                                        : statement.ToDate;
                                state = State.SearchValueDateColumn;
                            }

                            break;
                        case State.SearchValueDateColumn:
                            if (s.Text == "Value Date")
                            {
                                state = State.SearchPaymentDetailsColumn;
                                valueDateLeft = s.Left;
                            }

                            break;
                        case State.SearchPaymentDetailsColumn:
                            if (s.Text == "Payment Details")
                            {
                                state = State.SearchBeneficiaryColumn;
                                paymentDetailsLeft = s.Left;
                            }

                            break;
                        case State.SearchBeneficiaryColumn:
                            if (s.Text == "Remitter / Beneficiary")
                            {
                                state = State.SearchDebitColumn;
                                beneficiaryLeft = s.Left;
                            }

                            break;
                        case State.SearchDebitColumn:
                            if (s.Text == "Debit")
                            {
                                state = State.SearchCreditColumn;
                                debitRight = s.Right;
                            }

                            break;
                        case State.SearchCreditColumn:
                            if (s.Text != "Credit")
                            {
                                throw new Exception($"Money In column expected, but got '{s.Text}'.");
                            }

                            creditRight = s.Right;
                            state = State.SearchTrxn;
                            break;
                        case State.SearchTrxn:
                            if (DateTime.TryParseExact(s.Text, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None,  out _)
                                && s.Left.IsApproximately(valueDateLeft))
                            {
                                if (currentTrxn != null)
                                {
                                    statement.Transactions.Add(currentTrxn);
                                }

                                currentTrxn = new Transaction
                                {
                                    Date = DateTime.ParseExact(s.Text, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None),
                                    Description = ""
                                };
                                state = State.FirstDescription;
                            }
                            else if (s.Text.StartsWith("Created ") && s.Left.IsApproximately(valueDateLeft))
                            {
                                nextPage = true;
                            }
                            else if (s.Left.IsApproximately(paymentDetailsLeft)
                                     && currentTrxn != null)
                            {
                                currentTrxn.Description += " " + s.Text;
                            }

                            break;
                        case State.FirstDescription:
                            if (currentTrxn == null)
                                throw new Exception("Current transaction must not be null.");
                            if (currentTrxn.Description.Length > 0)
                                throw new Exception("Description was expected to be empty but was not.");

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

                            var amount = decimal.Parse(match.Groups["amount"].Value.Replace(",", ""),
                                CultureInfo.InvariantCulture);
                            if (s.Right.IsApproximately(debitRight))
                                currentTrxn.Amount = -amount;
                            else if (s.Right.IsApproximately(creditRight))
                                currentTrxn.Amount = +amount;
                            else
                                throw new Exception($"Found amount at unexpected location: {s.Right}.");
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

            return statements.ToArray();
        }

        enum State
        {
            SearchAccountNumber,
            SearchStatementPeriod,
            SearchValueDateColumn,
            SearchPaymentDetailsColumn,
            SearchBeneficiaryColumn,
            SearchDebitColumn,
            SearchCreditColumn,
            SearchTrxn,
            FirstDescription,
            Amount,
            Balance,
        }
    }
}