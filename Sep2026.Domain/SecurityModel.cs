using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sep2026.Domain
{
    public abstract class SecurityModel
    {
        //Feilds
        private readonly Guid securityId;
        private string tickerSymbol;
        private decimal? currentValue;
        private string securityType;


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
        // Null means that no market quote has been received yet; zero is a known price.
        public decimal? CurrentValue
        {
            get { return currentValue; }
            private set
            {
                // Argument validation
                if (value.HasValue)
                {
                    ValidatePositiveNumber(value.Value);
                }
                // Log the update action
                Console.WriteLine($"Current value updated to: {value}");
                currentValue = value;
            }
        }
        public string SecurityType
        {
            get { return securityType; }
            private set
            {
                // Argument validation
                ArgumentNullException.ThrowIfNullOrWhiteSpace(value, nameof(value));
                // Log the update action
                Console.WriteLine($"Security type updated to: {value}");
                securityType = value;
            }
        }
        //Default Constructor
        public SecurityModel(string tickerSymbol)
        {
            this.securityId = Guid.NewGuid();
            this.tickerSymbol = tickerSymbol;
            this.currentValue = null;
            this.securityType = string.Empty;
        }

        public void UpdateCurrentValue(decimal newValue)
        {
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
