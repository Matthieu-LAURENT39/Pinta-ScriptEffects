using System;
using Pinta.Core;

namespace ScriptEffects;

// Dialog for editing the script of ScriptEffect.
internal sealed class ScriptEffectDialog : Gtk.Dialog
{
    private readonly ScriptEffectData data;
    // TODO: swap for something more suited, possibly GtkSourceView
    private readonly Gtk.TextView editor;
    private readonly Gtk.Label statusLabel;

    public ScriptEffectDialog(IChromeService chrome, ScriptEffectData data)
    {
        this.data = data;

        Title = "Script Effect";
        TransientFor = chrome.MainWindow;
        Modal = true;
        Resizable = true;
        DefaultWidth = 900;
        DefaultHeight = 600;

        Gtk.Box contentArea = this.GetContentAreaBox();
        contentArea.Spacing = 8;
        contentArea.SetAllMargins(8);

        editor = Gtk.TextView.New();
        editor.Monospace = true;
        editor.Vexpand = true;
        editor.Hexpand = true;
        // Load the initial script code into the editor.
        editor.Buffer!.Text = data.ScriptCode;

        Gtk.ScrolledWindow scroll = Gtk.ScrolledWindow.New();
        scroll.SetChild(editor);
        scroll.Hexpand = true;
        scroll.Vexpand = true;
        contentArea.Append(scroll);

        // This shows the compilation messages
        // TODO: could this support color?
        statusLabel = Gtk.Label.New(string.Empty);
        statusLabel.Xalign = 0;
        statusLabel.Wrap = true;
        statusLabel.Selectable = true;
        contentArea.Append(statusLabel);

        AddButton(Translations.GetString("_Cancel"), (int)Gtk.ResponseType.Cancel);
        // TODO: Make this translatable
        AddButton("Preview", (int)Gtk.ResponseType.Apply);
        AddButton(Translations.GetString("_OK"), (int)Gtk.ResponseType.Ok);
        SetDefaultResponse((int)Gtk.ResponseType.Ok);

        Show();
    }

    /// <summary>
    /// Tries to compile the script and apply it to the effect data if successful.
    /// Any compilation errors are shown in the status label.
    /// </summary>
    public bool TryCompileAndApply()
    {
        // TODO: should probably block the UI while compiling
        string script = editor.Buffer?.Text ?? string.Empty;
        data.ScriptCode = script;

        if (!ScriptEffectCompiler.TryCompile(script, out var render, out var errorMessage))
        {
            data.LastCompileError = errorMessage;
            statusLabel.SetText(errorMessage ?? "Compilation failed.");
            return false;
        }

        data.CompiledRender = render;
        data.LastCompileError = null;
        // Notify that the compiled render delegate has changed so that the effect can re-render with the new code
        data.FirePropertyChanged(nameof(ScriptEffectData.CompiledRender));
        statusLabel.SetText("Compilation successful.");

        return true;
    }
}