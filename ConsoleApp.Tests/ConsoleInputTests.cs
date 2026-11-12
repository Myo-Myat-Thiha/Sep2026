using ConsoleApp.Presentation;
using Xunit;

namespace ConsoleApp.Tests;

public sealed class ConsoleInputTests
{
    [Fact]
    public async Task UserId_reprompts_until_a_nonempty_guid_is_entered()
    {
        var userId = Guid.NewGuid();
        using var output = new StringWriter();
        var input = new ConsoleInput(new StringReader($"bad\n{Guid.Empty}\n {userId} \n"), output);

        Assert.Equal(userId, await input.ReadUserIdAsync());
        Assert.Contains("valid, nonempty user ID", output.ToString());
    }

    [Theory]
    [InlineData("q\n")]
    [InlineData("")]
    public async Task Quit_or_end_of_input_exits_user_lookup(string text)
    {
        var input = new ConsoleInput(new StringReader(text), TextWriter.Null);
        Assert.Null(await input.ReadUserIdAsync());
    }

    [Fact]
    public async Task Selection_rejects_invalid_rows_and_converts_display_number_to_index()
    {
        var input = new ConsoleInput(new StringReader("bad\n0\n-1\n3\n2\n"), TextWriter.Null);
        Assert.Equal(new PortfolioSelection(PortfolioAction.Select, 1),
            await input.ReadPortfolioSelectionAsync(2));
    }

    [Fact]
    public async Task Empty_list_does_not_allow_a_row_selection()
    {
        using var output = new StringWriter();
        var input = new ConsoleInput(new StringReader("1\nr\n"), output);
        Assert.Equal(PortfolioAction.Reload, (await input.ReadPortfolioSelectionAsync(0)).Action);
        Assert.Contains("Enter R, U, or Q.", output.ToString());
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
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => input.ReadUserIdAsync(cancellation.Token));
    }

    [Fact]
    public async Task Cancellation_ends_wait_even_when_the_reader_blocks_synchronously()
    {
        using var reader = new BlockingReader();
        using var cancellation = new CancellationTokenSource();
        var input = new ConsoleInput(reader, TextWriter.Null);
        var read = input.ReadUserIdAsync(cancellation.Token);
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
