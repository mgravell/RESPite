using System.Diagnostics.CodeAnalysis;
using Terminal.Gui;

namespace StackExchange.Redis.Gui;

public abstract class TabBase : View
{
    public TabBase()
    {
        Width = Height = Dim.Fill();
        CanFocus = true;
    }

    private string statusCaption = "";
    public string StatusCaption => statusCaption;

    public event Action<string>? StatusChanged;

    [MemberNotNull(nameof(statusCaption))]
    public void SetStatus(string status)
    {
        statusCaption = status;
        OnStatusChanged(status);
    }

    protected void OnStatusChanged(string? status)
    {
        StatusChanged?.Invoke(status ?? "");
    }
}
