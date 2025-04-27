using System.Globalization;
using Terminal.Gui;

namespace StackExchange.Redis.Gui;

internal sealed class RespConnectView : TabBase
{
    private readonly TextField hostField, portField, issuerCert, userCert, userKey, userName, password, sni, database, proxyPort;
    private readonly CheckBox tlsCheck, resp3Check, handshakeCheck, trustServerCert, runProxyServer;
    private readonly Action<string>? debugLog;

    public ConnectionOptionsBag? Validate()
    {
        if (IsInt32(portField.Text, out var port))
        {
            try
            {
                return new()
                {
                    Host = hostField.Text.Trim(),
                    Port = port,
                    Database = IsInt32(database.Text, out int i32) ? i32 : default,
                    Resp3 = resp3Check.CheckedState == CheckState.Checked,
                    Tls = tlsCheck.CheckedState == CheckState.Checked,
                    CaCertPath = issuerCert.Text,
                    UserCertPath = userCert.Text,
                    UserKeyPathOrPassword = userKey.Text,
                    Handshake = handshakeCheck.CheckedState == CheckState.Checked,
                    Sni = sni.Text,
                    TrustServerCert = trustServerCert.CheckedState == CheckState.Checked,
                    DebugLog = debugLog,
                    ProxyPort = IsInt32(proxyPort.Text, out i32) ? i32 : 6379,
                    RunProxyServer = runProxyServer.CheckedState == CheckState.Checked,
                };
            }
            catch
            {
            }
        }
        return null;

        static bool IsInt32(string sValue, out int iValue)
        {
            iValue = default;
            return !string.IsNullOrWhiteSpace(sValue) && int.TryParse(sValue.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out iValue);
        }
    }

    public event Action? Connect;

    public RespConnectView(ConnectionOptionsBag options)
    {
        debugLog = options.DebugLog;
        SetStatus("Create a new RESP connection");
        var lbl = Add(new Label
        {
            Text = "Host ",
        });
        hostField = new TextField
        {
            X = Pos.Right(lbl),
            Y = lbl.Y,
            Width = Dim.Fill(),
            Text = options.Host,
        };
        Add(hostField);

        lbl = Add(new Label
        {
            Text = "Port ",
            Y = Pos.Bottom(lbl),
        });
        portField = new TextField
        {
            X = Pos.Right(lbl),
            Y = lbl.Y,
            Width = Dim.Absolute(8),
            Text = options.Port.ToString(CultureInfo.InvariantCulture),
        };
        Add(portField);

        lbl = Add(new Label
        {
            Text = "Database ",
            Y = Pos.Bottom(lbl),
        });
        database = new TextField
        {
            X = Pos.Right(lbl),
            Y = lbl.Y,
            Width = Dim.Absolute(8),
            Text = options.Database.HasValue ? options.Database.Value.ToString(CultureInfo.InvariantCulture) : "",
        };
        Add(database);

        lbl = Add(new Label
        {
            Text = "TLS ",
            Y = Pos.Bottom(lbl) + 1,
        });
        tlsCheck = new CheckBox
        {
            X = Pos.Right(lbl) + 1,
            Y = lbl.Y,
            CheckedState = options.Tls ? CheckState.Checked : CheckState.UnChecked,
        };
        Add(tlsCheck);

        lbl = Add(new Label
        {
            Text = "Issuer cert ",
            Y = Pos.Bottom(lbl),
        });
        issuerCert = new TextField
        {
            X = Pos.Right(lbl) + 1,
            Y = lbl.Y,
            Text = options.CaCertPath ?? "",
            Width = Dim.Fill(),
        };
        Add(issuerCert);

        lbl = Add(new Label
        {
            Text = "User cert ",
            Y = Pos.Bottom(lbl),
        });
        userCert = new TextField
        {
            X = Pos.Right(lbl) + 1,
            Y = lbl.Y,
            Text = options.UserCertPath ?? "",
            Width = Dim.Fill(),
        };
        Add(userCert);

        lbl = Add(new Label
        {
            Text = "User key ",
            Y = Pos.Bottom(lbl),
        });
        userKey = new TextField
        {
            X = Pos.Right(lbl) + 1,
            Y = lbl.Y,
            Text = options.UserKeyPathOrPassword ?? "",
            Width = Dim.Fill(),
        };
        Add(userKey);

        lbl = Add(new Label
        {
            Text = "RESP 3 ",
            Y = Pos.Bottom(lbl),
        });
        resp3Check = new CheckBox
        {
            X = Pos.Right(lbl) + 1,
            Y = lbl.Y,
            CheckedState = options.Resp3 ? CheckState.Checked : CheckState.UnChecked,
        };
        Add(resp3Check);

        lbl = Add(new Label
        {
            Text = "Handshake ",
            Y = Pos.Bottom(lbl),
        });
        handshakeCheck = new CheckBox
        {
            X = Pos.Right(lbl) + 1,
            Y = lbl.Y,
            CheckedState = options.Handshake ? CheckState.Checked : CheckState.UnChecked,
        };
        Add(handshakeCheck);

        lbl = Add(new Label
        {
            Text = "User ",
            Y = Pos.Bottom(lbl),
        });
        userName = new TextField
        {
            X = Pos.Right(lbl) + 1,
            Y = lbl.Y,
            Text = options.User ?? "",
            Width = Dim.Fill(),
        };
        Add(userName);

        lbl = Add(new Label
        {
            Text = "Password ",
            Y = Pos.Bottom(lbl),
        });
        password = new TextField
        {
            X = Pos.Right(lbl) + 1,
            Y = lbl.Y,
            Text = options.Password ?? "",
            Width = Dim.Fill(),
        };
        Add(password);

        lbl = Add(new Label
        {
            Text = "SNI ",
            Y = Pos.Bottom(lbl),
        });
        sni = new TextField
        {
            X = Pos.Right(lbl) + 1,
            Y = lbl.Y,
            Text = options.Sni ?? "",
            Width = Dim.Fill(),
        };
        Add(sni);

        lbl = Add(new Label
        {
            Text = "Trust Server Certificate ",
            Y = Pos.Bottom(lbl),
        });
        trustServerCert = new CheckBox
        {
            X = Pos.Right(lbl) + 1,
            Y = lbl.Y,
            CheckedState = options.TrustServerCert ? CheckState.Checked : CheckState.UnChecked,
        };
        Add(trustServerCert);

        lbl = Add(new Label
        {
            Text = "Proxy port ",
            Y = Pos.Bottom(lbl),
        });
        proxyPort = new TextField
        {
            X = Pos.Right(lbl) + 1,
            Y = lbl.Y,
            Text = options.ProxyPort.ToString(),
            Width = Dim.Fill(),
        };
        Add(proxyPort);

        lbl = Add(new Label
        {
            Text = "Run proxy server ",
            Y = Pos.Bottom(lbl),
        });
        runProxyServer = new CheckBox
        {
            X = Pos.Right(lbl) + 1,
            Y = lbl.Y,
            CheckedState = options.RunProxyServer ? CheckState.Checked : CheckState.UnChecked,
        };
        Add(runProxyServer);

        var btn = new Button
        {
            Y = Pos.Bottom(lbl) + 2,
            Text = GetButtonLabel(runProxyServer.CheckedState),
            IsDefault = true,
        };
        Add(btn);
        btn.Accept += (s, e) => Connect?.Invoke();

        runProxyServer.CheckedStateChanging += (_, e) => btn.Text = GetButtonLabel(e.NewValue);
    }

    private static string GetButtonLabel(CheckState value) => value == CheckState.Checked ? "start proxy server" : "connect";
}
