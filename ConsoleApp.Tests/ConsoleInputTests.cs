using ConsoleApp.Presentation;
using Xunit;

namespace ConsoleApp.Tests;

public sealed class ConsoleInputTests
{
    private const string Password = "Correct horse battery staple!";

    [Fact]
    public async Task LinkFormIsAvailableOnAnEmptyListAndDoesNotEchoCredentials()
    {
        using var output = new StringWriter();
        var input = new ConsoleInput(new StringReader("l\nMy paper account\nprivate-id\nprivate-secret\ny\n"), output);
        Assert.Equal(PortfolioAction.Link, (await input.ReadPortfolioSelectionAsync(0)).Action);
        var form = await input.ReadPortfolioLinkAsync();
        Assert.Equal(PortfolioAction.Link, form.Action);
        Assert.Equal("My paper account", form.DisplayName);
        Assert.Equal("private-secret", form.Credentials!.SecretKey);
        Assert.DoesNotContain("private-id", output.ToString());
        Assert.DoesNotContain("private-secret", form.ToString());
    }

    [Theory]
    [InlineData("b\n")]
    [InlineData("Name\n\n")]
    [InlineData("Name\nid\n\n")]
    [InlineData("Name\nid\nsecret\nn\n")]
    public async Task CancelledLinkFormNeverReturnsCredentialsForSubmission(string text)
    {
        var form = await new ConsoleInput(new StringReader(text), TextWriter.Null).ReadPortfolioLinkAsync();
        Assert.Equal(PortfolioAction.Back, form.Action);
        Assert.Null(form.Credentials);
    }

    [Fact]
    public async Task PasswordInputPreservesSpacesAndDoesNotEchoTheSecret()
    {
        using var output = new StringWriter();
        var input = new ConsoleInput(new StringReader($" {Password} \n"), output);
        Assert.Equal($" {Password} ", await input.ReadPasswordAsync());
        Assert.DoesNotContain(Password, output.ToString());
    }

    [Fact]
    public async Task NewPasswordRequiresMatchingConfirmation()
    {
        using var output = new StringWriter();
        var input = new ConsoleInput(new StringReader($"short\n{Password}\nmismatch\n{Password}\n{Password}\n"), output);
        Assert.Equal(Password, await input.ReadNewPasswordAsync());
        Assert.Contains("length requirement", output.ToString());
        Assert.Contains("do not match", output.ToString());
        Assert.DoesNotContain(Password, output.ToString());
    }

    [Fact]
    public async Task InitialPasswordCommandIsAvailableWithoutAcceptingAGuid()
    {
        var input = new ConsoleInput(new StringReader("p\n"), TextWriter.Null);
        Assert.Equal(UserAction.SetPassword, (await input.ReadUserSelectionAsync()).Action);
    }

    [Fact]
    public async Task FormDiagnosticTextNeverIncludesItsPassword()
    {
        var input = new ConsoleInput(new StringReader($"Alice\nalice@example.com\n{Password}\n{Password}\n"), TextWriter.Null);
        var form = await input.ReadUserCreationAsync();
        Assert.Equal(Password, form.Password);
        Assert.DoesNotContain(Password, form.ToString());
    }
    [Fact]
    public async Task SignIn_rejects_guids_and_invalid_emails()
    {
        var userId = Guid.NewGuid();
        using var output = new StringWriter();
        var input = new ConsoleInput(new StringReader($"bad\n{Guid.Empty}\n{userId}\n Alice@example.com \n"), output);

        Assert.Equal(new UserSelection(UserAction.SignIn, "Alice@example.com"), await input.ReadUserSelectionAsync());
        Assert.Contains("valid email address", output.ToString());
    }

    [Theory]
    [InlineData("q\n")]
    [InlineData("")]
    public async Task Quit_or_end_of_input_exits_user_lookup(string text)
    {
        var input = new ConsoleInput(new StringReader(text), TextWriter.Null);
        Assert.Equal(UserAction.Exit, (await input.ReadUserSelectionAsync()).Action);
    }

    [Fact]
    public async Task Selection_rejects_invalid_rows_and_converts_display_number_to_index()
    {
        var input = new ConsoleInput(new StringReader("bad\n0\n-1\n3\n2\n"), TextWriter.Null);
        Assert.Equal(new PortfolioSelection(PortfolioAction.Select, 1),
            await input.ReadPortfolioSelectionAsync(2));
    }

    [Fact]
    public async Task NewProfileCommandOpensCreationWithoutAnExistingIdentity()
    {
        var input = new ConsoleInput(new StringReader(" n \n"), TextWriter.Null);
        Assert.Equal(new UserSelection(UserAction.Create), await input.ReadUserSelectionAsync());
    }

    [Fact]
    public async Task ProfileFormCollectsNameAndEmail()
    {
        var input = new ConsoleInput(new StringReader($" Alice \n Alice@Example.COM \n{Password}\n{Password}\n"), TextWriter.Null);
        var form = await input.ReadUserCreationAsync();
        Assert.Equal(UserAction.Create, form.Action);
        Assert.Equal("Alice", form.Name);
        Assert.Equal("Alice@Example.COM", form.Email);
    }

    [Theory]
    [InlineData("b\n", "Back")]
    [InlineData("q\n", "Exit")]
    [InlineData("", "Exit")]
    [InlineData("Alice\nb\n", "Back")]
    [InlineData("Alice\nq\n", "Exit")]
    [InlineData("Alice\n", "Exit")]
    public async Task ProfileFormCanBeCancelledOrEndedBeforeSubmission(string text, string expected)
    {
        var input = new ConsoleInput(new StringReader(text), TextWriter.Null);
        var form = await input.ReadUserCreationAsync();
        Assert.Equal(expected, form.Action.ToString());
        Assert.Null(form.Email);
    }

    [Fact]
    public async Task Empty_list_does_not_allow_a_row_selection()
    {
        using var output = new StringWriter();
        var input = new ConsoleInput(new StringReader("1\nr\n"), output);
        Assert.Equal(PortfolioAction.Reload, (await input.ReadPortfolioSelectionAsync(0)).Action);
        Assert.Contains("Enter L, R, U, or Q.", output.ToString());
    }

    [Theory]
    [InlineData("r\n", "Reload")]
    [InlineData(" u \n", "ChangeUser")]
    [InlineData("q\n", "Exit")]
    [InlineData("", "Exit")]
    public async Task List_commands_are_case_insensitive(string text, string expected)
    {
        var input = new ConsoleInput(new StringReader(text), TextWriter.Null);
        Assert.Equal(expected, (await input.ReadPortfolioSelectionAsync(1)).Action.ToString());
    }

    [Theory]
    [InlineData("\n", "Back")]
    [InlineData("b\n", "Back")]
    [InlineData("r\n", "Reload")]
    [InlineData("u\n", "ChangeUser")]
    [InlineData("q\n", "Exit")]
    [InlineData("", "Exit")]
    public async Task Detail_commands_support_back_reload_change_user_and_exit(string text, string expected)
    {
        var input = new ConsoleInput(new StringReader(text), TextWriter.Null);
        Assert.Equal(expected, (await input.ReadDetailActionAsync()).ToString());
    }

    [Fact]
    public async Task Precancelled_input_does_not_read()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var input = new ConsoleInput(new StringReader("q\n"), TextWriter.Null);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => input.ReadUserSelectionAsync(cancellation.Token));
    }

    [Fact]
    public async Task Cancellation_ends_wait_even_when_the_reader_blocks_synchronously()
    {
        using var reader = new BlockingReader();
        using var cancellation = new CancellationTokenSource();
        var input = new ConsoleInput(reader, TextWriter.Null);
        var read = input.ReadUserSelectionAsync(cancellation.Token);
        try
        {
            await reader.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => read.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            reader.Release();
            await reader.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}

// Models Console.In implementations whose async API still waits synchronously.
internal sealed class BlockingReader : TextReader
{
    private readonly ManualResetEventSlim released = new();
    internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        Started.TrySetResult();
        try
        {
            released.Wait();
            return ValueTask.FromResult<string?>(null);
        }
        finally
        {
            Finished.TrySetResult();
        }
    }

    internal void Release() => released.Set();
    protected override void Dispose(bool disposing)
    {
        if (disposing) released.Dispose();
        base.Dispose(disposing);
    }
}
