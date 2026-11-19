using System.Globalization;

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
                var value = await ReadAsync("Enter user ID, N to create an owner profile, or Q to exit: ", cancellationToken);
                if (value is null || value.Equals("Q", StringComparison.OrdinalIgnoreCase))
                    return new UserSelection(UserAction.Exit);
                if (value.Equals("N", StringComparison.OrdinalIgnoreCase))
                    return new UserSelection(UserAction.Create);

                if (Guid.TryParse(value, out var userId) && userId != Guid.Empty)
                    return new UserSelection(UserAction.Select, userId);

                output.WriteLine("Enter a valid, nonempty user ID, N to create a profile, or Q to exit.");
            }
        }

        public async Task<UserCreationInput> ReadUserCreationAsync(CancellationToken cancellationToken = default)
        {
            output.WriteLine("Enter a name and email. B: Cancel   Q: Exit");
            var name = await ReadAsync("Name: ", cancellationToken);
            if (FormAction(name) is { } nameAction) return new UserCreationInput(nameAction);
            var email = await ReadAsync("Email: ", cancellationToken);
            if (FormAction(email) is { } emailAction) return new UserCreationInput(emailAction);
            return new UserCreationInput(UserAction.Create, name, email);
        }

        private static UserAction? FormAction(string? value)
            => value is null || value.Equals("Q", StringComparison.OrdinalIgnoreCase) ? UserAction.Exit
                : value.Equals("B", StringComparison.OrdinalIgnoreCase) ? UserAction.Back : null;

        public async Task<PortfolioSelection> ReadPortfolioSelectionAsync(
            int portfolioCount, CancellationToken cancellationToken = default)
        {
            output.WriteLine();
            output.WriteLine("R: Reload list   U: Change user   Q: Exit");
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
            output.WriteLine("Enter/B: Back to list   R: Reload saved details   U: Change user   Q: Exit");
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

        private async Task<string?> ReadAsync(string prompt, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            output.Write(prompt);
            // Some console readers block synchronously despite their async API.
            // Keep that work off the session thread so shutdown can still complete.
            var read = Task.Run(async () =>
                await input.ReadLineAsync(cancellationToken).ConfigureAwait(false));
            return (await read.WaitAsync(cancellationToken))?.Trim();
        }
    }

    internal enum PortfolioAction { Select, Reload, ChangeUser, Exit, Back }

    internal enum UserAction { Select, Create, Back, Exit }

    internal readonly record struct UserSelection(UserAction Action, Guid? UserId = null);

    internal readonly record struct UserCreationInput(UserAction Action, string? Name = null, string? Email = null);

    internal readonly record struct PortfolioSelection(PortfolioAction Action, int Index = -1);
}
