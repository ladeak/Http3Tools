using Http3Tools;
using Terminal.Gui;

var client = new H3Client();
await client.TestAsync();
var viewModel = new ViewModel(client);
//var view = new View();
//Application.Init();
//Application.Top.Add(new MainWindow(viewModel));
//Application.Run();
//Application.Shutdown();

public class MainWindow : Window
{
    private readonly ViewModel _viewModel;
    public TextField usernameText;

    public MainWindow(ViewModel viewModel)
    {
        _viewModel = viewModel;
        Title = "Http3Tools";

        var frameEditor = new FrameEditorWindow(viewModel) { X = 1, Y = 1, Width = Dim.Percent(50), Height = Dim.Fill() };
        var viewPane = new FramesViewer(viewModel)
        {
            X = Pos.Right(frameEditor),
            Y = 1,
            Width = Dim.Fill(1),
            Height = Dim.Fill(),
        };


        var itemA = new FrameView("frame1", new Border() { BorderStyle = BorderStyle.Single });
        itemA.Add(new TextView() { Text = "test", AutoSize = true });

        //viewPane.Add(new TextView() { Text = "test", AutoSize = true });

        // Add the views to the Window
        Add(frameEditor, viewPane);

        viewPane.SetNeedsDisplay();
    }
}

public class FramesViewer : Window
{
    private readonly ViewModel _viewModel;

    public FramesViewer(ViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    public IList<Frame> Source { get; set; }

    public new void SetNeedsDisplay()
    {
        //var listView = new ListView()
        //{
        //    Width = Dim.Fill(),
        //    Height = Dim.Fill()
        //};
        this.RemoveAll();
        foreach (var command in _viewModel.Commands)
        {
            var label = new TextView()
            {
                Text = "aaaaaaaaaaaaaaaaaaaaaaaaaa bbb aaaaaaaaaaa aaaaaaaaa aaaaaaaaaa bb aaaaaaaaaaaaaaaaaa aaaaaaaaaaaaa aaaaaaaaaaaaaaaaaaaaaaaaaa bbbb aaaaaaaaaaa aaaaaaaaa aaaaaaaaaa bb aaaaaaaaaaaaaaaaaa aaaaaaaaaaaaa aaaaaaaaaaaaa aaaaaaaaaaaaaaaaaaaaaaaaaa cccc aaaaaaaaaaa aaaaaaaaa aaaaaaaaaa cc aaaaaaaaaaaaaaaaaa aaaaaaaaaaaaa aaaaaaaaaaaaaaaaaaaaaaaaaa dddd aaaaaaaaaaa aaaaaaaaa aaaaaaaaaa dd aaaaaaaaaaaaaaaaaa aaaaaaaaaaaaa aaaaaaaaaaaaaaaaaaaaaaaaaa dddd aaaaaaaaaaa aaaaaaaaa aaaaaaaaaa dd aaaaaaaaaaaaaaaaaa aaaaaaaaaaaaa aaaaaaaaaaaaa aaaaaaaaaaaaaaaaaaaaaaaaaa eeee aaaaaaaaaaa aaaaaaaaa aaaaaaaaaa ee aaaaaaaaaaaaaaaaaa aaaaaaaaaaaaa ",
                AutoSize = true,
                Width = Dim.Fill(),
                Height = 5,
                LayoutStyle = LayoutStyle.Computed,
                TextAlignment = TextAlignment.Left,
                ReadOnly = true,
                WordWrap = true,
                BottomOffset = 1,
                RightOffset = 1
            };
            Add(label);
            var scrollBar = new ScrollBarView(label, true);
            scrollBar.ChangedPosition += () => {
                label.TopRow = scrollBar.Position;
                if (label.TopRow != scrollBar.Position)
                {
                    scrollBar.Position = label.TopRow;
                }
                label.SetNeedsDisplay();
            };

            scrollBar.OtherScrollBarView.ChangedPosition += () => {
                label.LeftColumn = scrollBar.OtherScrollBarView.Position;
                if (label.LeftColumn != scrollBar.OtherScrollBarView.Position)
                {
                    scrollBar.OtherScrollBarView.Position = label.LeftColumn;
                }
                label.SetNeedsDisplay();
            };

            scrollBar.VisibleChanged += () => {
                if (scrollBar.Visible && label.RightOffset == 0)
                {
                    label.RightOffset = 1;
                }
                else if (!scrollBar.Visible && label.RightOffset == 1)
                {
                    label.RightOffset = 0;
                }
            };

            scrollBar.OtherScrollBarView.VisibleChanged += () => {
                if (scrollBar.OtherScrollBarView.Visible && label.BottomOffset == 0)
                {
                    label.BottomOffset = 1;
                }
                else if (!scrollBar.OtherScrollBarView.Visible && label.BottomOffset == 1)
                {
                    label.BottomOffset = 0;
                }
            };

            label.DrawContent += (e) =>
            {
                scrollBar.Size = label.Lines;
                scrollBar.Position = label.TopRow;
                if (scrollBar.OtherScrollBarView != null)
                {
                    scrollBar.OtherScrollBarView.Size = label.Maxlength;
                    scrollBar.OtherScrollBarView.Position = label.LeftColumn;
                }
                scrollBar.LayoutSubviews();
                scrollBar.Refresh();
            };

            //Add(label2);
            //listView.Add(new Label("test") { Width = Dim.Fill(), Height = 1 });
        }

        base.SetNeedsDisplay();
    }

}

public class FrameEditorWindow : Window
{
    private readonly ViewModel _viewModel;
    public TextField usernameText;

    public FrameEditorWindow(ViewModel viewModel)
    {
        _viewModel = viewModel;
        Title = "Http3Tools";

        // Create input components and labels
        var usernameLabel = new Label()
        {
            Text = "Username:"
        };

        usernameText = new TextField("")
        {
            // Position text field adjacent to the label
            X = Pos.Right(usernameLabel) + 1,

            // Fill remaining horizontal space
            Width = Dim.Fill(),
        };

        var passwordLabel = new Label()
        {
            Text = "Password:",
            X = Pos.Left(usernameLabel),
            Y = Pos.Bottom(usernameLabel) + 1
        };

        var passwordText = new TextField("")
        {
            Secret = true,
            // align with the text box above
            X = Pos.Left(usernameText),
            Y = Pos.Top(passwordLabel),
            Width = Dim.Fill(),
        };

        // Create login button
        var btnLogin = new Button()
        {
            Text = "Login",
            Y = Pos.Bottom(passwordLabel) + 1,
            // center the login button horizontally
            X = Pos.Center(),
            IsDefault = true,
        };

        var statusBar = new StatusBar();
        statusBar.Items = new StatusItem[] {
            new StatusItem(Key.F1, "F1: Test", async () =>
            {
                await _viewModel.ExecuteCommandAsync("TestAsync");
            })
        };

        // When login button is clicked display a message popup
        btnLogin.Clicked += () =>
        {
            if (usernameText.Text == "admin" && passwordText.Text == "password")
            {
                MessageBox.Query("Logging In", "Login Successful", "Ok");
                Application.RequestStop();
            }
            else
            {
                MessageBox.ErrorQuery("Logging In", "Incorrect username or password", "Ok");
            }
        };

        // Add the views to the Window
        Add(usernameLabel, usernameText, passwordLabel, passwordText, btnLogin, statusBar);
        _viewModel = viewModel;
    }
}