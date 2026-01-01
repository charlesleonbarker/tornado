using Microsoft.AspNetCore.Components;

namespace Tornado.Services;

public class LayoutState
{
    public RenderFragment? HeaderContent { get; private set; }
    public event Action? OnChange;

    public void SetHeaderContent(RenderFragment? content)
    {
        HeaderContent = content;
        OnChange?.Invoke();
    }

    public void ClearHeaderContent(RenderFragment? content)
    {
        if (HeaderContent != content)
        {
            return;
        }

        HeaderContent = null;
        OnChange?.Invoke();
    }
}
