﻿using Terminal.Gui;

namespace StackExchange.Redis.Gui;

internal sealed class RespDesktopWindow : Window
{
    private readonly CancellationTokenSource endOfLifeSource = new();

    private readonly TextField input;
    private readonly TabView servers;
    private readonly StatusBar statusBar;
    private readonly RespConnectView connect;

    private CancellationToken EndOfLife => endOfLifeSource.Token;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            endOfLifeSource.Cancel();
        }

        base.Dispose(disposing);
    }

    public RespDesktopWindow(ConnectionOptionsBag options)
    {
        Title = $"resp-cli desktop ({Application.QuitKey} to exit)";

        statusBar = new()
        {
            Width = Dim.Fill(7),
        };
        var tool = new Button
        {
            X = Pos.Right(statusBar) + 1,
            Y = Pos.Top(statusBar),
            Width = 6,
            Text = "⚠ ",
        };
        tool.Accept += (s, e) => ShowTools();

        var lbl = new Label
        {
            Text = ">",
        };

        input = new TextField
        {
            X = Pos.Right(lbl) + 1,
            Width = Dim.Fill(),
        };
        input.HasFocusChanged += (s, e) =>
        {
            if (input.HasFocus) SetStatusText("Send a RESP command");
        };

        servers = new TabView
        {
            Y = Pos.Bottom(input),
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            Style =
            {
                // TabsOnBottom = true,
                // ShowBorder = true,
                ShowTopLine = true,
            },
        };
        servers.ApplyStyleChanges();
        servers.SelectedTabChanged += (s, e) =>
        {
            if (servers.SelectedTab?.View is TabBase { } selected)
            {
                SetStatusText(selected.StatusCaption);
            }
            else
            {
                SetStatusText("");
            }
        };
        connect = new RespConnectView(options);
        var tab = new Tab
        {
            DisplayText = "⚡",
            View = connect,
        };
        connect.Connect += AddServer;

        servers.AddTab(tab, true);
        AddServer();

        input.KeyDown += async (sender, key) =>
        {
            try
            {
                if (key == Key.Enter)
                {
                    if (servers.SelectedTab?.View is ServerView server)
                    {
                        if (await server.SendAsync(input.Text.ToString()))
                        {
                            input.Text = "";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SetStatusText(ex.Message);
            }
        };

        Add(lbl, input, servers, statusBar, tool);
    }
    public void SetStatusText(string text) => statusBar.Text = text;

    private void ShowTools()
    {
        if (!(servers.SelectedTab?.View is ServerView server && server.Transport is { } transport))
        {
            return;
        }

        int mode = 0;
        using (var popup = new Dialog())
        {
            Button? last = null;
            void Add(string title, int selectedMode)
            {
                Button btn = new Button
                {
                    Text = title,
                    X = 1,
                };
                btn.Accept += (s, e) =>
                {
                    mode = selectedMode;
                    Application.RequestStop(popup);
                };

                if (last is null)
                {
                    btn.IsDefault = true;
                    btn.Y = 1;
                }
                else
                {
                    btn.Y = Pos.Bottom(last) + 1;
                }
                last = btn;
                popup.Add(btn);
            }

            popup.Title = "Tools";

            Add("Keys (SCAN)", 1);
            Add("Create keys", 2);

            Application.Run(popup);
        }

        switch (mode)
        {
            case 1:
                ShowServerDialog<KeysDialog>();
                break;
            case 2:
                ShowServerDialog<CreateKeysDialog>();
                break;
        }
    }

    private bool ShowServerDialog<T>() where T : ServerToolDialog, new()
    {
        if (!(servers.SelectedTab?.View is ServerView server && server.Transport is { } transport))
        {
            return false;
        }

        using var popup = new T();

        popup.SetTransport(transport);
        popup.StatusTextChanged += (s, e) => SetStatusText(e);
        SetStatusText(popup.StatusText);

        Application.Run(popup);

        return true;
    }

    private void Add(TabBase view)
    {
        if (view is null)
        {
            return;
        }

        var tabNumber = servers.Tabs.Count;
        var tab = new Tab
        {
            DisplayText = $" {tabNumber} ",
            View = view,
        };

        var key = tabNumber switch
        {
            1 => Key.F1,
            2 => Key.F2,
            3 => Key.F3,
            4 => Key.F4,
            5 => Key.F5,
            6 => Key.F6,
            7 => Key.F7,
            8 => Key.F8,
            9 => Key.F9,
            10 => Key.F10,
            11 => Key.F11,
            12 => Key.F12,
            _ => null,
        };

        if (key is not null)
        {
            Add(new Shortcut
            {
                Key = key,
                Action = () => servers.SelectedTab = tab,
                Visible = false,
            });
            if (view is TabBase tabView)
            {
                tabView.SetStatus($"({key}) " + tabView.StatusCaption);
            }
        }
        servers.AddTab(tab, true);
    }

    public void AddServer()
    {
        Application.Invoke(() =>
        {
            var options = connect.Validate();

            if (options is null)
            {
                // options are invalid
            }
            else if (options.RunProxyServer)
            {
                var view = new ProxyView(options, EndOfLife);
                view.ClientConnected += client => AddServerView(new ServerView(options, client, EndOfLife));
                view.AutoConnect += proxy => AddServerView(new ServerView(proxy, EndOfLife));
                Add(view);
            }
            else
            {
                AddServerView(new ServerView(options, EndOfLife));
            }
        });
    }
    private void AddServerView(ServerView view)
    {
        view.StatusChanged += SetStatusText;

        view.RepeatCommand += command =>
        {
            input.Text = command;
            input.SetFocus();
        };
        Application.Invoke(() => Add(view));
    }
}
