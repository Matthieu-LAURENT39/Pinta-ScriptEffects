using System;
using System.IO;
using System.Threading.Tasks;
using Pinta.Core;

namespace ScriptEffects;

// Dialog for editing the script of ScriptEffect.
internal sealed class ScriptEffectDialog : Gtk.Dialog
{
    private const string BaseTitle = "Script Effect";

    internal static readonly Gtk.ResponseType OpenResponse = (Gtk.ResponseType)1001;
    internal static readonly Gtk.ResponseType SaveResponse = (Gtk.ResponseType)1002;
    internal static readonly Gtk.ResponseType SaveAsResponse = (Gtk.ResponseType)1003;

    private readonly ScriptEffectData data;
    private readonly ScriptCodeTextView editor;
    private readonly Gtk.Label statusLabel;
    private Gio.File? currentFile;

    public ScriptEffectDialog(IChromeService chrome, ScriptEffectData data)
    {
        this.data = data;

        Title = BaseTitle;
        TransientFor = chrome.MainWindow;
        Modal = true;
        Resizable = true;
        DefaultWidth = 900;
        DefaultHeight = 600;

        Gtk.Box contentArea = this.GetContentAreaBox();
        contentArea.Spacing = 8;
        contentArea.SetAllMargins(8);

        editor = new ScriptCodeTextView();

        // Load the initial script code into the editor.
        editor.ScriptText = data.ScriptCode;

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
        AddButton(Translations.GetString("_Open"), (int)OpenResponse);
        AddButton(Translations.GetString("_Save"), (int)SaveResponse);
        AddButton(Translations.GetString("Save _As"), (int)SaveAsResponse);
        // TODO: Make this translatable
        AddButton("Preview", (int)Gtk.ResponseType.Apply);
        AddButton(Translations.GetString("_OK"), (int)Gtk.ResponseType.Ok);
        SetDefaultResponse((int)Gtk.ResponseType.Ok);

        UpdateWindowTitle();

        Show();
    }

    /// <summary>
    /// Opens a file dialog to select a script file to open, then loads the selected file into the editor.
    /// </summary>
    public async Task OpenScript()
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
            currentFile = selectedFile;
            UpdateWindowTitle();
            statusLabel.SetText($"Opened: {selectedFile.GetParseName()}");
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
        }
        catch (Exception ex)
        {
            statusLabel.SetText($"Failed to save file: {ex.Message}");
        }
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
        // TODO: should probably block the UI while compiling
        string script = editor.ScriptText;
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