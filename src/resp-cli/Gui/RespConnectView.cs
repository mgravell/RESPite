﻿using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Terminal.Gui;

namespace StackExchange.Redis.Gui;

internal sealed class RespConnectView : View
{
    private readonly TextField hostField, portField, issuerCert, userCert, userKey, userName, password;
    private readonly CheckBox tlsCheck, resp3Check, handshakeCheck;

    public ConnectionOptionsBag? Validate()
    {
        var host = hostField.Text.Trim();
        if (!string.IsNullOrEmpty(host) && int.TryParse(portField.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
        {
            try
            {
                return new()
                {
                    Host = host,
                    Port = port,
                    Resp3 = resp3Check.CheckedState == CheckState.Checked,
                    Tls = tlsCheck.CheckedState == CheckState.Checked,
                    CaCertPath = issuerCert.Text,
                    UserCertPath = userCert.Text,
                    UserKeyPath = userKey.Text,
                    Handshake = handshakeCheck.CheckedState == CheckState.Checked,
                };
            }
            catch
            {
            }
        }
        return null;
    }

    public event Action? Connect;

    public RespConnectView(ConnectionOptionsBag options)
    {
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;

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
            Y = Pos.Bottom(hostField),
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
            Text = "TLS",
            Y = Pos.Bottom(portField) + 1,
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
            Text = "Issuer cert",
            Y = Pos.Bottom(tlsCheck),
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
            Text = "User cert",
            Y = Pos.Bottom(issuerCert),
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
            Text = "User key",
            Y = Pos.Bottom(userCert),
        });
        userKey = new TextField
        {
            X = Pos.Right(lbl) + 1,
            Y = lbl.Y,
            Text = options.UserKeyPath ?? "",
            Width = Dim.Fill(),
        };
        Add(userKey);

        lbl = Add(new Label
        {
            Text = "RESP 3",
            Y = Pos.Bottom(userKey),
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
            Text = "Handshake",
            Y = Pos.Bottom(resp3Check),
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
            Text = "User",
            Y = Pos.Bottom(handshakeCheck),
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
            Text = "Password",
            Y = Pos.Bottom(userName),
        });
        password = new TextField
        {
            X = Pos.Right(lbl) + 1,
            Y = lbl.Y,
            Text = options.Password ?? "",
            Width = Dim.Fill(),
        };
        Add(password);

        var btn = new Button
        {
            Y = Pos.Bottom(lbl) + 2,
            Text = "connect",
            IsDefault = true,
        };
        Add(btn);
        btn.Accept += (s, e) => Connect?.Invoke();
    }
}
