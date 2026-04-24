using System;
using System.IO;
using System.Threading.Tasks;
using Pinta.Core;

namespace ScriptEffects;

// Dialog for editing the script of ScriptEffect.
internal sealed class ScriptEffectDialog : Gtk.Dialog
{
    private const string BaseTitle = "Script Effect";

    private readonly ScriptEffectData data;
    private readonly ScriptCodeTextView editor;
    private readonly Gtk.TextView lineNumbers;
    private readonly Gtk.TextTag lineNumberTag;
    private readonly Gtk.ScrolledWindow editorScroll;
    private readonly Gtk.Label statusLabel;
    // Storing the whole saved script just to detect unsaved changes is a bit
    // memory-inefficient, but it's simple to implement and shouldn't be a problem
    // for script editing. If this becomes an issue later, we can just look into storing 
    // a hash of the saved script or something like that.
    private string savedEditorScript;
    private Gio.File? currentFile;

    public ScriptEffectDialog(IChromeService chrome, ScriptEffectData data)
    {
        this.data = data;
        savedEditorScript = data.ScriptCode;

        Title = BaseTitle;
        TransientFor = chrome.MainWindow;
        Modal = true;
        Resizable = true;
        DefaultWidth = 900;
        DefaultHeight = 600;

        Gtk.Box contentArea = this.GetContentAreaBox();
        contentArea.Spacing = 8;
        contentArea.SetAllMargins(8);

        Gtk.Box topBar = Gtk.Box.New(Gtk.Orientation.Horizontal, 4);
        topBar.Halign = Gtk.Align.Start;

        // TODO: Surely we can do without the temporary allocation here?
        (string icon, string tooltip, Func<Task> action)[] topBarButtons = [
            ("document-new-symbolic", "New script", NewScript),
            ("document-open-symbolic", "Open script file", OpenScript),
            ("document-save-symbolic", "Save script", SaveScript),
            ("document-save-as-symbolic", "Save script as", SaveScriptAs)
        ];
        foreach ((string icon, string tooltip, Func<Task> action) in topBarButtons)
        {
            Gtk.Button button = Gtk.Button.NewFromIconName(icon);
            button.TooltipText = tooltip;
            button.OnClicked += async (_, _) => await action();
            topBar.Append(button);
        }

        contentArea.Append(topBar);

        editor = new ScriptCodeTextView();

        lineNumbers = Gtk.TextView.New();
        lineNumbers.Editable = false;
        lineNumbers.CursorVisible = false;
        lineNumbers.CanFocus = false;
        lineNumbers.CanTarget = false;
        lineNumbers.Monospace = true;
        lineNumbers.LeftMargin = 2;
        lineNumbers.RightMargin = 2;
        lineNumbers.WrapMode = Gtk.WrapMode.None;

        lineNumberTag = Gtk.TextTag.New("script-line-number");
        lineNumberTag.ForegroundRgba = new Gdk.RGBA { Red = 0.52f, Green = 0.55f, Blue = 0.60f, Alpha = 1f };
        lineNumbers.Buffer!.GetTagTable()?.Add(lineNumberTag);

        // Load the initial script code into the editor.
        editor.ScriptText = data.ScriptCode;
        RefreshLineNumbers();

        editor.Buffer!.OnChanged += (_, _) => RefreshLineNumbers();

        Gtk.Box editorArea = Gtk.Box.New(Gtk.Orientation.Horizontal, 0);
        editorArea.Append(lineNumbers);
        editorArea.Append(editor);

        editorScroll = Gtk.ScrolledWindow.New();
        editorScroll.SetChild(editorArea);
        editorScroll.Hexpand = true;
        editorScroll.Vexpand = true;
        contentArea.Append(editorScroll);

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

        Gtk.EventControllerKey keyboardController = Gtk.EventControllerKey.New();
        keyboardController.OnKeyPressed += OnKeyPressed;
        AddController(keyboardController);

        UpdateWindowTitle();

        Show();
    }

    // TODO: Surely there is a native way to create shortcuts for buttons?
    /// <summary>
    /// Handles key press events for the dialog, implementing keyboard shortcuts for opening and saving scripts.
    /// - Ctrl+N to "New".
    /// - Ctrl+O to "Open".
    /// - Ctrl+S to "Save".
    /// - Ctrl+Shift+S to "Save As".
    /// </summary>
    private bool OnKeyPressed(Gtk.EventControllerKey controller, Gtk.EventControllerKey.KeyPressedSignalArgs args)
    {
        // We only have ctrl shortcuts
        if (!args.State.IsControlPressed())
            return false;

        uint key = args.GetKey().ToUpper().Value;
        Func<Task>? action = key switch
        {
            Gdk.Constants.KEY_N => NewScript,
            Gdk.Constants.KEY_O => OpenScript,
            Gdk.Constants.KEY_S => args.State.IsShiftPressed() ? SaveScriptAs : SaveScript,
            _ => null,
        };

        if (action is null)
            return false;

        _ = action();
        return true;
    }

    private bool HasUnsavedEditorChanges => editor.ScriptText != savedEditorScript;

    /// <summary>
    /// Opens a new blank script in the editor.
    /// </summary>
    public async Task NewScript()
    {
        if (!await ConfirmDiscardUnsavedEditorChangesAsync(
                Translations.GetString("Save script changes before creating a new script?")))
            return;

        editor.ScriptText = ScriptEffectData.DefaultScript;
        data.ScriptCode = ScriptEffectData.DefaultScript;
        savedEditorScript = ScriptEffectData.DefaultScript;
        currentFile = null;
        statusLabel.SetText(string.Empty);
        UpdateWindowTitle();
    }

    /// <summary>
    /// Opens a file dialog to select a script file to open, then loads the selected file into the editor.
    /// </summary>
    public async Task OpenScript()
    {
        if (!await ConfirmDiscardUnsavedEditorChangesAsync(
                Translations.GetString("Save script changes before opening another script file?")))
            return;

        using Gtk.FileFilter scriptFilter = Gtk.FileFilter.New();
        scriptFilter.Name = "Script files";
        scriptFilter.AddPattern("*.cs");

        using Gtk.FileFilter allFilesFilter = Gtk.FileFilter.New();
        allFilesFilter.Name = Translations.GetString("All files");
        allFilesFilter.AddPattern("*");

        using Gio.ListStore fileFilters = Gio.ListStore.New(Gtk.FileFilter.GetGType());
        fileFilters.Append(scriptFilter);
        fileFilters.Append(allFilesFilter);

        using Gtk.FileDialog fileDialog = Gtk.FileDialog.New();
        fileDialog.SetTitle("Open Script File");
        fileDialog.SetFilters(fileFilters);
        fileDialog.Modal = true;

        if (currentFile?.GetParent() is Gio.File directory && directory.QueryExists(null))
            fileDialog.SetInitialFolder(directory);

        Gio.File? selectedFile = await fileDialog.OpenFileAsync(this);

        if (selectedFile is null)
            return;

        try
        {
            using GioStream stream = new(selectedFile.Read(null));
            using StreamReader reader = new(stream);
            string script = reader.ReadToEnd();

            editor.ScriptText = script;
            data.ScriptCode = script;
            savedEditorScript = script;
            currentFile = selectedFile;
            UpdateWindowTitle();
        }
        catch (Exception ex)
        {
            statusLabel.SetText($"Failed to open file: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves the current script to the current file. If none are open, this acts like Save As.
    /// </summary>
    public async Task SaveScript()
    {
        if (currentFile is null)
        {
            await SaveScriptAs();
            return;
        }
        SaveToFile(currentFile);
    }

    /// <summary>
    /// Opens a file dialog to select a location to save the current script, then saves the script to the selected file.
    /// </summary>
    public async Task SaveScriptAs()
    {
        using Gtk.FileFilter scriptFilter = Gtk.FileFilter.New();
        scriptFilter.Name = "Script files";
        scriptFilter.AddPattern("*.cs");

        using Gtk.FileFilter allFilesFilter = Gtk.FileFilter.New();
        allFilesFilter.Name = Translations.GetString("All files");
        allFilesFilter.AddPattern("*");

        using Gio.ListStore fileFilters = Gio.ListStore.New(Gtk.FileFilter.GetGType());
        fileFilters.Append(scriptFilter);
        fileFilters.Append(allFilesFilter);

        using Gtk.FileDialog fileDialog = Gtk.FileDialog.New();
        fileDialog.SetTitle("Save Script File");
        fileDialog.SetFilters(fileFilters);
        fileDialog.Modal = true;

        if (currentFile?.GetParent() is Gio.File currentDirectory && currentDirectory.QueryExists(null))
            fileDialog.SetInitialFolder(currentDirectory);

        Gio.File? selectedFile;
        try
        {
            selectedFile = await fileDialog.SaveAsync(this);
        }
        catch (GLib.GException)
        {
            return;
        }

        if (selectedFile is null)
            return;

        SaveToFile(selectedFile);
        currentFile = selectedFile;
        UpdateWindowTitle();
    }

    /// <summary>
    /// Saves the current script to the specified file.
    /// </summary>
    /// <param name="file"></param>
    private void SaveToFile(Gio.File file)
    {
        try
        {
            string script = editor.ScriptText;
            using GioStream stream = new(file.Replace());
            using StreamWriter writer = new(stream);
            writer.Write(script);
            savedEditorScript = script;
        }
        catch (Exception ex)
        {
            statusLabel.SetText($"Failed to save file: {ex.Message}");
        }
    }

    /// <summary>
    /// Asks for confirmation before discarding unsaved editor changes.
    /// If there are no unsaved changes, this returns true immediately.
    /// </summary>
    /// <param name="heading">The dialog heading shown to the user.</param>
    /// <returns>true if the action can continue, otherwise false.</returns>
    private async Task<bool> ConfirmDiscardUnsavedEditorChangesAsync(string heading)
    {
        if (!HasUnsavedEditorChanges)
            return true;

        using Adw.MessageDialog dialog = Adw.MessageDialog.New(
            this,
            heading,
            Translations.GetString("If you don't save, all changes since the last save will be lost."));

        dialog.AddResponse("cancel", Translations.GetString("_Cancel"));
        dialog.AddResponse("discard", Translations.GetString("_Discard"));
        dialog.AddResponse("save", Translations.GetString("_Save"));

        dialog.SetResponseAppearance("discard", Adw.ResponseAppearance.Destructive);
        dialog.SetResponseAppearance("save", Adw.ResponseAppearance.Suggested);
        dialog.DefaultResponse = "save";
        dialog.CloseResponse = "cancel";

        string response = await dialog.RunAsync();
        if (response == "save")
        {
            await SaveScript();
            // The save could have failed or been cancelled, so we check again
            return !HasUnsavedEditorChanges;
        }

        return response == "discard";
    }

    /// <summary>
    /// Asks for confirmation before discarding pending dialog changes.
    /// </summary>
    /// <returns>true if the dialog can close, otherwise false.</returns>
    public async Task<bool> ConfirmDiscardPendingDialogChanges()
    {
        if (!HasUnsavedEditorChanges)
            return true;

        using Adw.MessageDialog dialog = Adw.MessageDialog.New(
            this,
            Translations.GetString("Discard script changes?"),
            Translations.GetString("If you close now, your script changes will be lost."));

        dialog.AddResponse("cancel", Translations.GetString("_Cancel"));
        dialog.AddResponse("discard", Translations.GetString("_Discard"));

        dialog.SetResponseAppearance("discard", Adw.ResponseAppearance.Destructive);
        dialog.DefaultResponse = "cancel";
        dialog.CloseResponse = "cancel";

        return await dialog.RunAsync() == "discard";
    }

    /// <summary>
    /// Updates the window title to include the name of the currently open file, or "<untitled>" if no file is open.
    /// </summary>
    private void UpdateWindowTitle()
    {
        string fileName = "<untitled>";
        if (currentFile is not null)
        {
            string? path = currentFile.GetPath();
            string parseName = currentFile.GetParseName();
            fileName = Path.GetFileName(path ?? parseName);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = parseName;
        }
        Title = $"{BaseTitle} - {fileName}";
    }

    /// <summary>
    /// Tries to compile the script and apply it to the effect data if successful.
    /// Any compilation errors are shown in the status label.
    /// </summary>
    public bool TryCompileAndApply()
    {
        double? savedH = editorScroll.Hadjustment?.Value;
        double? savedV = editorScroll.Vadjustment?.Value;

        // TODO: should probably block the UI while compiling
        string script = editor.ScriptText;
        data.ScriptCode = script;

        if (!ScriptEffectCompiler.TryCompile(script, out var render, out var errorMessage))
        {
            data.LastCompileError = errorMessage;
            statusLabel.SetText(errorMessage ?? "Compilation failed.");
            editor.GrabFocus();
            RestoreEditorScrollPosition(savedH, savedV);
            return false;
        }

        data.CompiledRender = render;
        data.LastCompileError = null;
        // Notify that the compiled render delegate has changed so that the effect can re-render with the new code
        data.FirePropertyChanged(nameof(ScriptEffectData.CompiledRender));
        statusLabel.SetText("Compilation successful.");
        editor.GrabFocus();
        RestoreEditorScrollPosition(savedH, savedV);

        return true;
    }

    /// <summary>
    /// Regenerates the line-number left bar so it stays in sync with the code editor.
    /// </summary>
    private void RefreshLineNumbers()
    {
        string text = editor.ScriptText;
        int lineCount = Math.Max(1, text.Count(c => c == '\n') + 1);
        int digits = lineCount.ToString().Length;

        // Keep enough width so numbers don't get clipped
        // TODO: this feels quite hacky, there's probably a better way to do this
        lineNumbers.WidthRequest = (digits + 1) * 10;

        string lineNumbersText = string.Join(
            Environment.NewLine,
            Enumerable.Range(1, lineCount).Select(line => line.ToString().PadLeft(digits)));

        Gtk.TextBuffer buffer = lineNumbers.Buffer!;
        buffer.Text = lineNumbersText;
        buffer.GetBounds(out Gtk.TextIter start, out Gtk.TextIter end);
        buffer.ApplyTag(lineNumberTag, start, end);
    }

    /// <summary>
    /// Restores the editor scroll position after UI updates complete.
    /// This is needed, otherwise the scroll position resets to the top after every compilation. Even worse, 
    /// it would only actually "jump" once the user clicks anywhere in the editor, causing to select the wrong place.
    /// </summary>
    private void RestoreEditorScrollPosition(double? horizontal, double? vertical)
    {
        if (horizontal is null || vertical is null)
            return;

        // Queue this to run as soon as the UI thread is idle
        GLib.Functions.IdleAdd(0, () =>
        {
            Gtk.Adjustment? h = editorScroll.Hadjustment;
            Gtk.Adjustment? v = editorScroll.Vadjustment;

            if (h is not null)
                h.Value = Math.Clamp(horizontal.Value, h.Lower, Math.Max(h.Lower, h.Upper - h.PageSize));
            if (v is not null)
                v.Value = Math.Clamp(vertical.Value, v.Lower, Math.Max(v.Lower, v.Upper - v.PageSize));

            return false;
        });
    }
}