using System.Text;
using Http3Repl;
using Spectre.Console;

var client = new H3Client();
var viewModel = new ViewModel(client);
var view = new View(viewModel);

public class View
{
    private readonly ViewModel _viewModel;

    public View(ViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    public async Task RunAsync()
    {
        while (true)
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            var commandTask = (new SelectionPrompt<string>()
                .Title("Choose [green]command[/]?")
                .PageSize(5)
                .MoreChoicesText("[grey](Move up and down to reveal more options)[/]")
                .AddChoices(new[] { "Run Test" }).ShowAsync(AnsiConsole.Console, cts.Token));

            var updateRequired = _viewModel.AwaitUpdateAsync(cts.Token);

            var completedTask = await Task.WhenAny(commandTask, updateRequired);

            cts.Cancel();

            if (commandTask == completedTask)
            {
                await _viewModel.ExecuteCommandAsync(commandTask.Result);
            }
            else if (completedTask == updateRequired)
            {
                foreach (var item in updateRequired.Result)
                {
                    var panel = new Panel(item.SourceStream);
                    panel.Header = new PanelHeader(item.Data);
                    panel.Border = BoxBorder.Square;
                    AnsiConsole.Write(panel);
                }
            }
        }
    }
}