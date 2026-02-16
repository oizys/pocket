using Terminal.Gui;

Application.Init();

var top = Application.Top;
var window = new Window("Pockets")
{
    X = 0,
    Y = 0,
    Width = Dim.Fill(),
    Height = Dim.Fill()
};

var label = new Label("Welcome to Pockets! Press Ctrl-Q to quit.")
{
    X = Pos.Center(),
    Y = Pos.Center()
};

window.Add(label);
top.Add(window);
Application.Run();
Application.Shutdown();
