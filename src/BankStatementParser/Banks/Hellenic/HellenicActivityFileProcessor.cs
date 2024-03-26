using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using BankStatementParser.Extensions;
using Pdf2Text;

namespace BankStatementParser.Banks.Hellenic
{
    public class HellenicActivityFileProcessor
    {
        private readonly PdfParser _pdfParser = new PdfParser();
        private readonly Regex _accountNumberRegex = new Regex(@"^\d+-\d+-\d+-\d+$");
        private readonly Regex _dateRegex = new Regex(@"^\d{2}/\d{2}/\d{4}$");
        private readonly Regex _amountRegex = new Regex(@"^-?(\d{1,3})(\.\d{3})*,\d{2}$");
        private readonly CultureInfo _numberCultureInfo;

        public HellenicActivityFileProcessor()
        {
            _numberCultureInfo = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            _numberCultureInfo.NumberFormat.NumberDecimalSeparator = ",";
            _numberCultureInfo.NumberFormat.NumberGroupSeparator = ".";
        }

        public Statement ProcessFile(string pdfPath)
        {
            var pdfModel = _pdfParser.Parse(pdfPath);

            Transaction currentTrxn = null;
            var currentBalance = 0m;
            var statement = new Statement
            {
                Transactions = new List<Transaction>()
            };

            double dateLeft = 0;
            double descriptionLeft = 0;
            double debitRight = 0;
            double creditRight = 0;
            double balanceRight = 0;
            var hasBalance = false;
            var expectedDescriptionLines = 0;
            double lastDateBottom = 0;

            foreach (var page in pdfModel.Pages)
            {
                var nextPage = false;
                var state = page.PageNumber == 0 ? State.SearchAccountNumber : State.ScrollToTable;

                for (var i = 0; i < page.Sentences.Count && !nextPage; i++)
                {
                    var s = page.Sentences[i];

                    var next = i + 1 < page.Sentences.Count ? page.Sentences[i + 1] : null;
                    var trimmedText = s.Text.Trim();

                    switch (state)
                    {
                        case State.SearchAccountNumber:
                            if (s.Text == "ACCOUNT NO")
                            {
                                if (!_accountNumberRegex.IsMatch(next.Text))
                                    throw new Exception(
                                        $"Account number was expected to be numeric with dashes but was {next.Text}.");
                                statement.AccountNumber = next.Text;
                                state = State.SearchStatementPeriod;
                            }

                            break;
                        case State.SearchStatementPeriod:
                            if (s.Text == "PERIOD")
                            {
                                state = State.SearchStatementPeriodFrom;
                            }

                            break;
                        case State.SearchStatementPeriodFrom:
                            var startMatch = Regex.Match(s.Text, @"^\d{2}/\d{2}/\d{4}$");
                            if (!startMatch.Success)
                                throw new Exception($"Expected start of statement, but found: '{s.Text}'.");
                            statement.FromDate = DateTime.ParseExact(startMatch.Value, "dd/MM/yyyy",
                                CultureInfo.InvariantCulture);
                            state = State.SearchStatementPeriodTo;
                            break;
                        case State.SearchStatementPeriodTo:
                            if (trimmedText != "-")
                                throw new Exception($"Expected '-' between statement dates, but got: '{s.Text}'.");
                            var endMatch = Regex.Match(next.Text, @"^\d{2}/\d{2}/\d{4}$");
                            if (!endMatch.Success)
                                throw new Exception($"Expected end of statement, but found: '{next.Text}'.");
                            statement.ToDate = DateTime.ParseExact(endMatch.Value, "dd/MM/yyyy",
                                CultureInfo.InvariantCulture);
                            state = State.ScrollToTable;
                            break;
                        case State.ScrollToTable:
                            if (s.Text == "DATE" && (page.PageNumber == 0 || s.Left.IsApproximately(dateLeft)))
                            {
                                dateLeft = s.Left;
                                state = State.DescriptionColumn;
                            }

                            break;
                        case State.DescriptionColumn:
                            if (s.Text != "DESCRIPTION")
                            {
                                throw new Exception($"Expected DESCRIPTION column, but found: '{s.Text}'.");
                            }

                            descriptionLeft = s.Left;
                            state = State.DebitColumn;
                            break;
                        case State.DebitColumn:
                            if (s.Text != "DEBIT")
                            {
                                throw new Exception($"Expected DEBIT column, but found: '{s.Text}'.");
                            }

                            debitRight = s.Right;
                            state = State.CreditColumn;
                            break;
                        case State.CreditColumn:
                            if (s.Text != "CREDIT")
                            {
                                throw new Exception($"Expected CREDIT column, but found: '{s.Text}'.");
                            }

                            creditRight = s.Right;
                            state = State.ValueDateColumn;
                            break;
                        case State.ValueDateColumn:
                            if (s.Text != "VALUE DATE")
                            {
                                throw new Exception($"Expected VALUE DATE column, but found: '{s.Text}'.");
                            }

                            state = next.Text == "BALANCE" ? State.BalanceColumn : State.SearchTrxn;
                            break;
                        case State.BalanceColumn:
                            if (s.Text != "BALANCE")
                            {
                                throw new Exception($"Expected BALANCE column, but found: '{s.Text}'.");
                            }

                            hasBalance = true;
                            balanceRight = s.Right;
                            state = State.SearchTrxn;
                            break;
                        case State.SearchTrxn:
                            if (s.Left.IsApproximately(dateLeft)
                                && _dateRegex.Match(s.Text).Success)
                            {
                                if (currentTrxn == null)
                                {
                                    currentTrxn = new Transaction
                                    {
                                        Description = ""
                                    };
                                    expectedDescriptionLines = 0;
                                }

                                currentTrxn.Date =
                                    DateTime.ParseExact(s.Text, "dd/MM/yyyy", CultureInfo.InvariantCulture);
                                lastDateBottom = s.Bottom;
                            }
                            else if (s.Text == "TOTALS:")
                            {
                                nextPage = true;
                                state = State.ScrollToTable;
                            }
                            else if (s.Left > descriptionLeft - 5 && s.Right < debitRight - 50)
                            {
                                if (currentTrxn == null)
                                {
                                    currentTrxn = new Transaction
                                    {
                                        Description = trimmedText
                                    };
                                    expectedDescriptionLines = 1;
                                    lastDateBottom = 0;
                                }
                                else
                                {
                                    if (s.Left.IsApproximately(descriptionLeft))
                                    {
                                        if (lastDateBottom.IsApproximately(0) || lastDateBottom - 3 > s.Bottom)
                                            expectedDescriptionLines++;
                                        else if (!lastDateBottom.IsApproximately(s.Bottom, 4.9) || lastDateBottom + 3 < s.Bottom)
                                            expectedDescriptionLines--;
                                    }

                                    if (trimmedText.Length > 0)
                                        currentTrxn.Description += (currentTrxn.Description.Length == 0 ? "" : " ") +
                                                                   trimmedText;
                                    if (expectedDescriptionLines == 0 && !lastDateBottom.IsApproximately(s.Bottom, 4.9) &&
                                        (next == null || next.Top > s.Bottom) && currentTrxn.Amount != null)
                                    {
                                        statement.Transactions.Add(currentTrxn);
                                        currentTrxn = null;
                                        expectedDescriptionLines = 0;
                                        lastDateBottom = 0;
                                    }
                                }
                            }
                            else if (s.Right.IsApproximately(debitRight))
                            {
                                var debitMatch = _amountRegex.Match(s.Text);
                                if (!debitMatch.Success)
                                    throw new Exception($"Amount was expected but got {s.Text}.");
                                if (currentTrxn == null)
                                    throw new Exception("Current transaction must not be null.");
                                if (currentTrxn.Amount.HasValue)
                                    throw new Exception("Amount was expected to be null but was not.");

                                currentTrxn.Amount = decimal.Parse(s.Text, _numberCultureInfo);
                            }
                            else if (s.Right.IsApproximately(creditRight))
                            {
                                var creditMatch = _amountRegex.Match(s.Text);
                                if (!creditMatch.Success)
                                    throw new Exception($"Amount was expected but got {s.Text}.");
                                if (currentTrxn == null)
                                    throw new Exception("Current transaction must not be null.");
                                if (currentTrxn.Amount.HasValue)
                                    throw new Exception("Amount was expected to be null but was not.");

                                currentTrxn.Amount = decimal.Parse(s.Text, _numberCultureInfo);
                            }
                            else if (hasBalance && s.Right.IsApproximately(balanceRight))
                            {
                                var oldBalance = currentBalance;
                                if (!decimal.TryParse(s.Text, NumberStyles.Number, _numberCultureInfo,
                                        out currentBalance))
                                    throw new Exception($"Balance was expected to be numeric but was {s.Text}.");
                                if (currentTrxn == null)
                                    throw new Exception("Current transaction was not expected to be null but was.");
                                if (oldBalance != 0 && oldBalance + currentTrxn.Amount != currentBalance)
                                    throw new Exception(
                                        $"Balance was expected to be {oldBalance + currentTrxn.Amount} but was {currentBalance}.");
                            }

                            if (currentTrxn != null)
                            {
                                if ((hasBalance && s.Right.IsApproximately(balanceRight) ||
                                     !hasBalance && (s.Right.IsApproximately(debitRight) ||
                                                     s.Right.IsApproximately(creditRight)))
                                    && expectedDescriptionLines == 0)
                                {
                                    statement.Transactions.Add(currentTrxn);
                                    currentTrxn = null;
                                    currentBalance = 0;
                                    expectedDescriptionLines = 0;
                                }
                            }

                            break;

                        default:
                            throw new ArgumentOutOfRangeException(nameof(state), state, "Wrong state");
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
            SearchStatementPeriodFrom,
            SearchStatementPeriodTo,
            ScrollToTable,
            DescriptionColumn,
            DebitColumn,
            CreditColumn,
            ValueDateColumn,
            BalanceColumn,
            SearchTrxn,
        }
    }
}