using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sep2026.FinancePortfolio
{
    public abstract class SecurityModel
    {
        //Feilds
        private readonly Guid securityId;
        private string tickerSymbol { get; private set; }
        private decimal currentValue { get; set; }


        //Properties
        /**
         * Some of the properties are only set within the class itself.
         * But for the Updating the data, have the public methods to update the data, which will have the validation and logging and 
         * also that will go through the properties to set the data.
         */
        public Guid SecurityId => securityId;
        public string TickerSymbol
        {
            get { return tickerSymbol; }
            private set
            {
                // Argument validation
                ArgumentNullException.ThrowIfNullOrWhiteSpace(value, nameof(value));
                // Log the update action
                Console.WriteLine($"Ticker symbol updated to: {value}");
                tickerSymbol = value;
            }
        }
        public decimal CurrentValue
        {
            get { return currentValue; }
            private set
            {
                // Argument validation
                ArgumentNullException.ThrowIfNull(value, nameof(value));
                ValidatePositiveNumber(value);
                // Log the update action
                Console.WriteLine($"Current value updated to: {value}");
                currentValue = value;
            }
        }
        public string SecurityType
        {
            get { return SecurityType; }
            private set
            {
                // Argument validation
                ArgumentNullException.ThrowIfNullOrWhiteSpace(value, nameof(value));
                // Log the update action
                Console.WriteLine($"Security type updated to: {value}");
                SecurityType = value;
            }
        }
        //Default Constructor
        public SecurityModel(string tickerSymbol)
        {
            this.securityId = Guid.NewGuid();
            this.tickerSymbol = tickerSymbol;
            this.currentValue = 0;
            this.SecurityType = string.Empty;
        }

        public void UpdateCurrentValue(decimal newValue)
        {
            ArgumentNullException.ThrowIfNull(newValue, nameof(newValue));
            ValidatePositiveNumber(newValue);
            CurrentValue = newValue;
        }
        public void UpdateTickerSymbol(string newTickerSymbol)
        {
            ArgumentNullException.ThrowIfNullOrWhiteSpace(newTickerSymbol, nameof(newTickerSymbol));
            TickerSymbol = newTickerSymbol;
        }

        public void UpdateSecurityType(string newSecurityType)
        {
            ArgumentNullException.ThrowIfNullOrWhiteSpace(newSecurityType, nameof(newSecurityType));
            SecurityType = newSecurityType;
        }

        internal void ValidatePositiveNumber(decimal value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "must be greater than or equal to zero.");
            }
        }
    }
}
