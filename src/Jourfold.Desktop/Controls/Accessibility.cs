using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace Jourfold.Desktop;

/// <summary>
/// A button for one option of a choice, such as a status, a category or a view. The "selected" class shows the
/// chosen option; screen readers hear the same state as a pressed toggle.
/// </summary>
public class ChoiceButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);

    public bool IsSelected
    {
        get => Classes.Contains("selected");
        set
        {
            if (value == IsSelected) return;
            Classes.Set("selected", value);
            (ControlAutomationPeer.FromElement(this) as Peer)?.Changed(value);
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(ChoiceButton owner) : ButtonAutomationPeer(owner), IToggleProvider
    {
        public ToggleState ToggleState => owner.IsSelected ? ToggleState.On : ToggleState.Off;
        public void Toggle() => Invoke();
        public void Changed(bool selected) => RaisePropertyChangedEvent(TogglePatternIdentifiers.ToggleStateProperty,
            selected ? ToggleState.Off : ToggleState.On, selected ? ToggleState.On : ToggleState.Off);
    }
}

/// <summary>
/// A border that only decorates: avatars, covers and the like. With an accessible name it is one image with that
/// name (a person's avatar reads as the person, not as initials); without one, screen readers skip it.
/// </summary>
public class Decoration : Border
{
    protected override Type StyleKeyOverride => typeof(Border);
    protected override AutomationPeer OnCreateAutomationPeer() => new DecorativePeer(this);
}

/// <summary>
/// Hides an element's inner parts from screen readers. Avalonia's Linux bridge names unnamed elements after their
/// class ("Border", "Icon"), so decorations also report an empty class name.
/// </summary>
public sealed class DecorativePeer(Control owner) : ControlAutomationPeer(owner)
{
    private bool Named => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(Owner));
    protected override IReadOnlyList<AutomationPeer>? GetChildrenCore() => null;
    protected override string GetClassNameCore() => "";
    protected override AutomationControlType GetAutomationControlTypeCore() => Named ? AutomationControlType.Image : AutomationControlType.None;
    protected override bool IsControlElementCore() => Named;
    protected override bool IsContentElementCore() => Named;
}
