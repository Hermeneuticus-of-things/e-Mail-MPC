using EMailMPC.Core.Interaction;

namespace EMailMPC.Core.Tests.Interaction;

public sealed class FocusStateTests
{
    [Fact]
    public void StartsWithItsWindowAsTheOnlyActiveSurface()
    {
        var state = new FocusState("main");

        Assert.Equal("window.main", state.ActiveEntry.SurfaceId);
        Assert.Equal(FocusLevel.Window, state.ActiveEntry.Level);
    }

    [Fact]
    public void PushingAndPoppingSurfaceRestoresItsParentAndSavedCaret()
    {
        var state = new FocusState("main");
        Register(state, "search.controls");
        Register(state, "dialog.account", modal: true);

        state.PushFocus("search.controls", "search.button");
        state.RecordActivePosition("SearchBox", caretStart: 12, caretEnd: 12);
        state.PushFocus("dialog.account", "AddAccountMenuItem");
        Assert.Equal("dialog.account", state.ActiveEntry.SurfaceId);

        Assert.True(state.PopFocus("dialog.account"));
        Assert.Equal("search.controls", state.ActiveEntry.SurfaceId);
        Assert.Equal("SearchBox", state.ActiveEntry.LastFocusedControlId);
        Assert.Equal(12, state.ActiveEntry.CaretStart);
    }

    [Fact]
    public void SwitchingSiblingSurfacesPreservesSelectionAndReactivatesHistory()
    {
        var state = new FocusState("main");
        Register(state, "mail.results");
        Register(state, "mail.reader");

        state.PushFocus("mail.results");
        state.RecordActivePosition("ResultsGrid", selection: "message-7");
        Assert.True(state.SwitchFocus("mail.reader"));
        Assert.Equal("mail.reader", state.ActiveEntry.SurfaceId);
        Assert.True(state.SwitchFocus("mail.results"));
        Assert.Equal("mail.results", state.ActiveEntry.SurfaceId);
        Assert.Equal("message-7", state.ActiveEntry.Selection);
    }

    [Fact]
    public void PopOfParentAlsoRemovesNestedSurfaces()
    {
        var state = new FocusState("main");
        Register(state, "settings.app");
        Register(state, "dropdown.theme", "settings.app");
        Register(state, "dialog.confirm", "dropdown.theme", modal: true);

        state.PushFocus("settings.app");
        state.PushFocus("dropdown.theme");
        state.PushFocus("dialog.confirm");

        Assert.True(state.PopFocus("settings.app"));
        Assert.Equal(["window.main"], state.Stack.Select(entry => entry.SurfaceId));
    }

    [Fact]
    public void UnregisteringParentAlsoUnregistersNestedSurfaces()
    {
        var state = new FocusState("main");
        Register(state, "settings.app");
        Register(state, "dropdown.theme", "settings.app");
        Register(state, "dialog.confirm", "dropdown.theme", modal: true);
        state.PushFocus("settings.app");
        state.PushFocus("dropdown.theme");
        state.PushFocus("dialog.confirm");

        state.Unregister("settings.app");

        Assert.Equal(["window.main"], state.Stack.Select(entry => entry.SurfaceId));
        Assert.DoesNotContain("settings.app", state.RegisteredSurfaceIds);
        Assert.DoesNotContain("dropdown.theme", state.RegisteredSurfaceIds);
        Assert.DoesNotContain("dialog.confirm", state.RegisteredSurfaceIds);
    }

    [Fact]
    public void DispatchStartsAtActiveSurfaceAndBubblesWhenUnhandled()
    {
        var state = new FocusState("main", new Dictionary<string, Func<object?, bool>>
        {
            ["save"] = _ => true
        });
        var handledBySurface = false;
        Register(state, "compose.body", commands: new Dictionary<string, Func<object?, bool>>
        {
            ["save"] = _ =>
            {
                handledBySurface = true;
                return false;
            }
        });
        state.PushFocus("compose.body");

        Assert.True(state.Dispatch("save"));
        Assert.True(handledBySurface);
    }

    [Fact]
    public void ModalSurfaceBlocksBackgroundFocusAndCommandBubbling()
    {
        var rootCommandHandled = false;
        var state = new FocusState("main", new Dictionary<string, Func<object?, bool>>
        {
            ["save"] = _ =>
            {
                rootCommandHandled = true;
                return true;
            }
        });
        Register(state, "mail.results");
        Register(state, "dialog.confirm", modal: true);
        state.PushFocus("mail.results");
        Assert.Throws<InvalidOperationException>(() => state.SwitchFocus("dialog.confirm"));
        state.PushFocus("dialog.confirm");

        Assert.False(state.Dispatch("save"));
        Assert.False(rootCommandHandled);
        Assert.Throws<InvalidOperationException>(() => state.SwitchFocus("mail.results"));
        Assert.Throws<InvalidOperationException>(() => state.PopFocus("mail.results"));
    }

    [Fact]
    public void UnregisteringActiveSurfaceRestoresItsWindow()
    {
        var state = new FocusState("compose");
        Register(state, "compose.body");
        state.PushFocus("compose.body");

        state.Unregister("compose.body");

        Assert.Equal("window.compose", state.ActiveEntry.SurfaceId);
        Assert.DoesNotContain("compose.body", state.RegisteredSurfaceIds);
    }

    [Fact]
    public void RejectsUnknownParentsAndPopsChildBeforeReturningToParent()
    {
        var state = new FocusState("main");
        Assert.Throws<InvalidOperationException>(() => Register(state, "dialog.child", "dialog.parent"));
        Register(state, "settings.app");
        Register(state, "dropdown.theme", "settings.app");
        state.PushFocus("settings.app");
        state.PushFocus("dropdown.theme");

        Assert.True(state.SwitchFocus("settings.app"));
        Assert.Equal("settings.app", state.ActiveEntry.SurfaceId);
        Assert.Equal(["window.main", "settings.app"], state.Stack.Select(entry => entry.SurfaceId));
    }

    private static void Register(
        FocusState state,
        string id,
        string? parentId = null,
        bool modal = false,
        IReadOnlyDictionary<string, Func<object?, bool>>? commands = null)
    {
        state.Register(new FocusSurfaceRegistration
        {
            Id = id,
            Level = FocusLevel.Surface,
            ParentId = parentId ?? state.WindowSurfaceId,
            Modal = modal,
            Commands = commands ?? new Dictionary<string, Func<object?, bool>>()
        });
    }
}
