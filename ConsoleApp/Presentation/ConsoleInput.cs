using System.Globalization;

using System.Text;
using Sep2026.Domain;

namespace ConsoleApp.Presentation
{
    internal sealed class ConsoleInput
    {
        private readonly TextReader input;
        private readonly TextWriter output;

        public ConsoleInput(TextReader input, TextWriter output)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(output);
            this.input = input;
            this.output = output;
        }

        public async Task<UserSelection> ReadUserSelectionAsync(CancellationToken cancellationToken = default)
        {
            while (true)
            {
                var value = await ReadAsync("Email (N: New profile, P: Initial password, Q: Exit): ", cancellationToken);
                if (value is null || value.Equals("Q", StringComparison.OrdinalIgnoreCase))
                    return new UserSelection(UserAction.Exit);
                if (value.Equals("N", StringComparison.OrdinalIgnoreCase))
                    return new UserSelection(UserAction.Create);

                if (value.Equals("P", StringComparison.OrdinalIgnoreCase))
                    return new UserSelection(UserAction.SetPassword);
                try { return new UserSelection(UserAction.SignIn, UserProfileRules.NormalizeEmail(value)); }
                catch (Exception exception) when (exception is ArgumentException or FormatException)
                { output.WriteLine("Enter a valid email address, or N, P, or Q."); }
            }
        }

        public async Task<UserCreationInput> ReadUserCreationAsync(CancellationToken cancellationToken = default)
        {
            output.WriteLine("Enter a name, email, and password. B: Cancel / Q: Exit at name or email.");
            var name = await ReadAsync("Name: ", cancellationToken);
            if (FormAction(name) is { } nameAction) return new UserCreationInput(nameAction);
            var email = await ReadAsync("Email: ", cancellationToken);
            if (FormAction(email) is { } emailAction) return new UserCreationInput(emailAction);
            var password = await ReadNewPasswordAsync(cancellationToken);
            return password is null ? new UserCreationInput(UserAction.Back)
                : new UserCreationInput(UserAction.Create, name, email, password);
        }

        public Task<string?> ReadPasswordAsync(CancellationToken cancellationToken = default)
            => ReadPasswordCoreAsync("Password (blank cancels): ", cancellationToken);

        public async Task<string?> ReadNewPasswordAsync(CancellationToken cancellationToken = default)
        {
            while (true)
            {
                output.WriteLine($"Use {UserProfileRules.MinPasswordLength}-{UserProfileRules.MaxPasswordLength} characters. Spaces are preserved; a blank password cancels.");
                var password = await ReadPasswordCoreAsync("New password: ", cancellationToken);
                if (string.IsNullOrEmpty(password)) return null;
                try { UserProfileRules.ValidatePassword(password); }
                catch (ArgumentException) { output.WriteLine("The password does not meet the length requirement."); continue; }
                var confirmation = await ReadPasswordCoreAsync("Confirm password: ", cancellationToken);
                if (string.IsNullOrEmpty(confirmation)) return null;
                if (string.Equals(password, confirmation, StringComparison.Ordinal)) return password;
                output.WriteLine("Passwords do not match. Please enter them again.");
            }
        }

        public async Task<UserSelection> ReadPasswordSetupEmailAsync(CancellationToken cancellationToken = default)
        {
            var value = await ReadAsync("Existing profile email: ", cancellationToken);
            if (FormAction(value) is { } action) return new UserSelection(action);
            return new UserSelection(UserAction.SetPassword, value);
        }

        private async Task<string?> ReadPasswordCoreAsync(string prompt, CancellationToken cancellationToken)
        {
            if (!ReferenceEquals(input, Console.In) || Console.IsInputRedirected)
                return await ReadAsync(prompt, cancellationToken, trim: false);
            cancellationToken.ThrowIfCancellationRequested();
            output.Write(prompt);
            var password = new StringBuilder();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Console.KeyAvailable) { await Task.Delay(25, cancellationToken); continue; }
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter) { output.WriteLine(); return password.ToString(); }
                if (key.Key == ConsoleKey.Escape) { output.WriteLine(); return null; }
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (password.Length > 0) password.Length--;
                }
                else if (!char.IsControl(key.KeyChar) && password.Length <= UserProfileRules.MaxPasswordLength)
                    password.Append(key.KeyChar);
            }
        }

        private static UserAction? FormAction(string? value)
            => value is null || value.Equals("Q", StringComparison.OrdinalIgnoreCase) ? UserAction.Exit
                : value.Equals("B", StringComparison.OrdinalIgnoreCase) ? UserAction.Back : null;

        public async Task<PortfolioSelection> ReadPortfolioSelectionAsync(
            int portfolioCount, CancellationToken cancellationToken = default)
        {
            output.WriteLine();
            output.WriteLine("R: Reload list   U: Sign out   Q: Exit");
            while (true)
            {
                var prompt = portfolioCount > 0 ? "Select a portfolio number or command: " : "Choose a command: ";
                var value = await ReadAsync(prompt, cancellationToken);
                if (value is null) return new PortfolioSelection(PortfolioAction.Exit);

                switch (value.ToUpperInvariant())
                {
                    case "Q": return new PortfolioSelection(PortfolioAction.Exit);
                    case "U": return new PortfolioSelection(PortfolioAction.ChangeUser);
                    case "R": return new PortfolioSelection(PortfolioAction.Reload);
                }

                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) &&
                    number >= 1 && number <= portfolioCount)
                    return new PortfolioSelection(PortfolioAction.Select, number - 1);

                output.WriteLine(portfolioCount > 0
                    ? $"Enter a number from 1 to {portfolioCount}, or R, U, or Q."
                    : "Enter R, U, or Q.");
            }
        }

        public async Task<PortfolioAction> ReadDetailActionAsync(CancellationToken cancellationToken = default)
        {
            output.WriteLine();
            output.WriteLine("Enter/B: Back to list   R: Reload saved details   U: Sign out   Q: Exit");
            while (true)
            {
                var value = await ReadAsync("Choose a command: ", cancellationToken);
                if (value is null) return PortfolioAction.Exit;

                switch (value.ToUpperInvariant())
                {
                    case "":
                    case "B": return PortfolioAction.Back;
                    case "R": return PortfolioAction.Reload;
                    case "U": return PortfolioAction.ChangeUser;
                    case "Q": return PortfolioAction.Exit;
                }

                output.WriteLine("Press Enter, or enter B, R, U, or Q.");
            }
        }

        private async Task<string?> ReadAsync(string prompt, CancellationToken cancellationToken, bool trim = true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            output.Write(prompt);
            // Some console readers block synchronously despite their async API.
            // Keep that work off the session thread so shutdown can still complete.
            var read = Task.Run(async () =>
                await input.ReadLineAsync(cancellationToken).ConfigureAwait(false));
            var value = await read.WaitAsync(cancellationToken);
            return trim ? value?.Trim() : value;
        }
    }

    internal enum PortfolioAction { Select, Reload, ChangeUser, Exit, Back }

    internal enum UserAction { SignIn, Create, SetPassword, Back, Exit }

    internal readonly record struct UserSelection(UserAction Action, string? Email = null);

    internal readonly record struct UserCreationInput(UserAction Action, string? Name = null, string? Email = null, string? Password = null)
    {
        public override string ToString() => Action.ToString();
    }

    internal readonly record struct PortfolioSelection(PortfolioAction Action, int Index = -1);
}
